using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Resource;

public class IncludesResourceHandlerOutcomeTests
{
    [Fact]
    public async Task GivenWarningAndPagingProbe_WhenIncludesArePaged_ThenWarningSurvivesWithoutConsumingTheIncludeBudget()
    {
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            MaxItemCount = 2,
            IncludesMaxItemCount = 1,
            IncludesContinuationToken = IncludesContinuationToken.Encode(1, 1),
        };
        var handler = CreateHandler(StreamEntries(
        [
            Entry("Patient", "match", SearchEntryMode.Match),
            new SearchEntryResult("", "", "", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty)
            {
                IsPagingProbe = true,
                SearchMode = SearchEntryMode.Include,
            },
            Entry("Organization", "skipped"),
            Warning(),
            Entry("Organization", "kept"),
            Entry("Organization", "next"),
        ]));

        var result = await handler.HandleAsync(new IncludesResourceQuery("Patient", options), CancellationToken.None);
        using var output = new MemoryStream();
        await SerializeAsync(output, result.Resources, options, cancellationToken: CancellationToken.None);
        using var bundle = JsonDocument.Parse(output.ToArray());

        var entries = bundle.RootElement.GetProperty("entry").EnumerateArray().ToArray();
        entries.Length.ShouldBe(2);
        entries.Single(e => e.GetProperty("search").GetProperty("mode").GetString() == "outcome")
            .GetProperty("resource").GetProperty("issue")[0].GetProperty("code").GetString().ShouldBe("incomplete");
        entries.Single(e => e.GetProperty("search").GetProperty("mode").GetString() == "include")
            .GetProperty("resource").GetProperty("id").GetString().ShouldBe("kept");
        var links = bundle.RootElement.GetProperty("link").EnumerateArray().ToArray();
        links.ShouldNotContain(l => l.GetProperty("relation").GetString() == "next");
        var related = links.Single(l => l.GetProperty("relation").GetString() == "related")
            .GetProperty("url").GetString()!;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(related).Query);
        IncludesContinuationToken.TryDecode(query["_includesContinuationToken"].ToString(), out int offset, out int count)
            .ShouldBeTrue();
        offset.ShouldBe(2);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenCancellationAfterWarning_WhenStreamingIncludes_ThenCancellationPropagatesAndCommittedWarningRemainsValid()
    {
        using var cancellation = new CancellationTokenSource();
        var options = new SearchOptions { ResourceType = "Patient", MaxItemCount = 2, IncludesMaxItemCount = 1 };
        var handler = CreateHandler(CancelAfterWarning(cancellation));
        var result = await handler.HandleAsync(
            new IncludesResourceQuery("Patient", options), cancellation.Token);
        using var output = new MemoryStream();

        await Should.ThrowAsync<OperationCanceledException>(
            () => SerializeAsync(output, result.Resources, options, flushThresholdBytes: 1, cancellationToken: cancellation.Token));

        output.Length.ShouldBeGreaterThan(0);
        using var bundle = JsonDocument.Parse(output.ToArray());
        bundle.RootElement.GetProperty("entry")[0].GetProperty("search").GetProperty("mode")
            .GetString().ShouldBe("outcome");
        bundle.RootElement.GetProperty("link").EnumerateArray()
            .ShouldNotContain(l => l.GetProperty("relation").GetString() == "next" ||
                l.GetProperty("relation").GetString() == "related");
    }

    private static IncludesResourceHandler CreateHandler(IAsyncEnumerable<SearchEntryResult> entries)
    {
        var context = Substitute.For<IFhirRequestContextAccessor>();
        context.RequestContext.Returns(new FhirRequestContext { TenantId = 1 });
        var partitionStrategy = Substitute.For<IPartitionStrategy>();
        partitionStrategy.DetermineReadPartition(
            Arg.Any<PartitionResolutionContext>(), "Patient", Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(new RequestPartition { PartitionIds = [1], Mode = PartitionMode.Isolated });
        var execution = Substitute.For<IQueryExecutionStrategy>();
        execution.SearchStreamAsync(
            Arg.Any<RequestPartition>(), Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(entries);
        return new IncludesResourceHandler(
            partitionStrategy, execution, context, NullLogger<IncludesResourceHandler>.Instance);
    }

    private static Task SerializeAsync(
        Stream output, IAsyncEnumerable<SearchEntryResult> entries, SearchOptions options,
        int flushThresholdBytes = 65536, CancellationToken cancellationToken = default) =>
        StreamingBundleSerializer.SerializeWithPaginationAsync(
            output, "searchset", null, entries, options, "http://localhost/Patient/$includes",
            "?_count=2&_includesCount=1", flushThresholdBytes: flushThresholdBytes, cancellationToken: cancellationToken);

    private static SearchEntryResult Entry(string type, string id, SearchEntryMode mode = SearchEntryMode.Include) =>
        new(type, id, "1", DateTimeOffset.UnixEpoch, JsonSerializer.SerializeToUtf8Bytes(new { resourceType = type, id }))
        {
            SearchMode = mode,
        };

    private static SearchEntryResult Warning() =>
        new("OperationOutcome", "warning", "1", DateTimeOffset.UnixEpoch, Encoding.UTF8.GetBytes(
            """{"resourceType":"OperationOutcome","issue":[{"severity":"warning","code":"incomplete","diagnostics":"An included resource could not be read."}]}"""))
        {
            SearchMode = SearchEntryMode.Outcome,
        };

    private static async IAsyncEnumerable<SearchEntryResult> StreamEntries(
        IReadOnlyList<SearchEntryResult> entries, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    private static async IAsyncEnumerable<SearchEntryResult> CancelAfterWarning(
        CancellationTokenSource cancellation, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return Warning();
        await cancellation.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
