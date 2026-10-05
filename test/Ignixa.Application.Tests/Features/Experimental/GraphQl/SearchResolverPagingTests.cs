using System.Runtime.CompilerServices;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using FhirISchema = Ignixa.Abstractions.ISchema;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class SearchResolverPagingTests
{
    public static IEnumerable<object[]> SearchPages()
    {
        foreach (string path in new[] { "list", "reverse-list", "connection", "reverse-connection" })
        {
            foreach (string scenario in new[]
            {
                "empty", "short", "lone", "full-probe", "terminal", "legacy-extra",
                "deleted-probe", "mixed-probe", "mixed-terminal",
            })
            {
                yield return [path, scenario];
            }
        }
    }

    [Theory]
    [MemberData(nameof(SearchPages))]
    public async Task GivenControlledSearchPage_WhenGraphQlCollects_ThenProbeIsNotRenderedAndPageBoundaryIsPreserved(
        string path, string scenario)
    {
        var (resolver, context) = CreateResolver(Stream(Page(scenario)));
        IReadOnlyList<JsonElement> resources;
        SearchConnectionResult? connection = null;
        switch (path)
        {
            case "list":
                resources = await resolver.SearchListAsync("Patient", context, CancellationToken.None);
                break;
            case "reverse-list":
                resources = await resolver.SearchReverseListAsync(
                    "Patient", "general-practitioner", "Practitioner", "source", context, CancellationToken.None);
                break;
            case "connection":
                connection = await resolver.SearchAsync("Patient", context, CancellationToken.None);
                resources = connection.Edges.Select(e => e.Resource).ToArray();
                break;
            case "reverse-connection":
                connection = await resolver.SearchReverseAsync(
                    "Patient", "general-practitioner", "Practitioner", "source", context, CancellationToken.None);
                resources = connection.Edges.Select(e => e.Resource).ToArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(path));
        }

        string[] expectedIds = scenario switch
        {
            "empty" or "lone" => [],
            "short" or "deleted-probe" => ["p1"],
            "mixed-probe" or "mixed-terminal" => ["before", "warning", "p1", "p2", "after"],
            _ => ["p1", "p2"],
        };
        resources.Select(r => r.GetProperty("id").GetString()).ShouldBe(expectedIds);
        if (connection is not null)
        {
            connection.Count.ShouldBe(9);
            connection.Offset.ShouldBe(4);
            connection.Pagesize.ShouldBe(2);
            ContinuationToken.TryDecode(connection.First!, out int firstOffset, out int firstCount).ShouldBeTrue();
            firstOffset.ShouldBe(0);
            firstCount.ShouldBe(2);
            bool expectedHasMore = scenario is not ("empty" or "terminal" or "mixed-terminal");
            if (expectedHasMore)
            {
                ContinuationToken.TryDecode(connection.Next!, out int offset, out int count).ShouldBeTrue();
                offset.ShouldBe(6);
                count.ShouldBe(2);
            }
            else
            {
                connection.Next.ShouldBeNull();
            }
            if (scenario.StartsWith("mixed", StringComparison.Ordinal))
            {
                connection.Edges.Select(e => e.Mode)
                    .ShouldBe(["include", "outcome", "match", "match", "include"]);
            }
        }
    }

    [Theory]
    [InlineData("list")]
    [InlineData("reverse-list")]
    [InlineData("connection")]
    [InlineData("reverse-connection")]
    public async Task GivenCancellationAfterFullPage_WhenCollectingBoundary_ThenCancellationPropagates(string path)
    {
        using var cancellation = new CancellationTokenSource();
        var (resolver, context) = CreateResolver(CancelAfterFullPage(cancellation));

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            switch (path)
            {
                case "list":
                    await resolver.SearchListAsync("Patient", context, cancellation.Token);
                    break;
                case "reverse-list":
                    await resolver.SearchReverseListAsync(
                        "Patient", "general-practitioner", "Practitioner", "source", context, cancellation.Token);
                    break;
                case "connection":
                    await resolver.SearchAsync("Patient", context, cancellation.Token);
                    break;
                case "reverse-connection":
                    await resolver.SearchReverseAsync(
                        "Patient", "general-practitioner", "Practitioner", "source", context, cancellation.Token);
                    break;
            }
        });
    }

    [Fact]
    public async Task GivenEmptyContentWithoutProbeFlag_WhenSearching_ThenMalformedContentIsNotHidden()
    {
        var (resolver, context) = CreateResolver(Stream([new SearchEntryResult(
            "Patient", "corrupt", "1", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty)]));

        await Should.ThrowAsync<JsonException>(() => resolver.SearchAsync("Patient", context, CancellationToken.None));
    }

    private static (SearchResolver Resolver, IResolverContext Context) CreateResolver(IAsyncEnumerable<SearchEntryResult> entries)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(entries, Total: 9));
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(new FhirRequestContext { TenantId = 1 });
        var builder = Substitute.For<ISearchOptionsBuilder>();
        builder.Build(Arg.Any<string?>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<FhirISchema?>())
            .Returns(new SearchOptions
            {
                ResourceType = "Patient", MaxItemCount = 2, ContinuationToken = ContinuationToken.Encode(4, 2),
            });
        var factory = Substitute.For<ISearchOptionsBuilderFactory>();
        factory.Create(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(builder);
        var context = Substitute.For<IResolverContext>();
        context.ArgumentOptional<int?>("_count").Returns(new Optional<int?>(2));
        context.ArgumentOptional<string?>("_cursor").Returns(new Optional<string?>(ContinuationToken.Encode(4, 2)));
        var arguments = Substitute.For<IFieldCollection<IInputField>>();
        arguments.GetEnumerator().Returns(_ => Enumerable.Empty<IInputField>().GetEnumerator());
        var field = Substitute.For<IObjectField>();
        field.Arguments.Returns(arguments);
        var selection = Substitute.For<ISelection>();
        selection.Field.Returns(field);
        context.Selection.Returns(selection);
        return (new SearchResolver(
            mediator, factory, accessor, Options.Create(new ExperimentalOptions()), NullLogger<SearchResolver>.Instance), context);
    }

    private static SearchEntryResult[] Page(string scenario) => scenario switch
    {
        "empty" => [],
        "short" => [Entry("p1"), Probe()],
        "lone" => [Probe()],
        "full-probe" => [Entry("p1"), Entry("p2"), Probe()],
        "terminal" => [Entry("p1"), Entry("p2")],
        "legacy-extra" => [Entry("p1"), Entry("p2"), Entry("p3")],
        "deleted-probe" => [Entry("p1"), Probe() with { IsDeleted = true, SearchMode = SearchEntryMode.Outcome }],
        "mixed-probe" =>
        [
            Entry("before", SearchEntryMode.Include), Entry("warning", SearchEntryMode.Outcome),
            Entry("p1"), Entry("p2"), Probe(), Entry("after", SearchEntryMode.Include),
        ],
        "mixed-terminal" =>
        [
            Entry("before", SearchEntryMode.Include), Entry("warning", SearchEntryMode.Outcome),
            Entry("p1"), Entry("p2"), Entry("after", SearchEntryMode.Include),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static SearchEntryResult Entry(string id, SearchEntryMode mode = SearchEntryMode.Match)
    {
        string type = mode switch
        {
            SearchEntryMode.Include => "Organization",
            SearchEntryMode.Outcome => "OperationOutcome",
            _ => "Patient",
        };
        byte[] json = mode == SearchEntryMode.Outcome
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                resourceType = type, id,
                issue = new[] { new { severity = "warning", code = "incomplete" } },
            })
            : JsonSerializer.SerializeToUtf8Bytes(new { resourceType = type, id });
        return new SearchEntryResult(type, id, "1", DateTimeOffset.UnixEpoch, json) { SearchMode = mode };
    }

    private static SearchEntryResult Probe() =>
        new("", "", "", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty) { IsPagingProbe = true };

    private static async IAsyncEnumerable<SearchEntryResult> Stream(
        IReadOnlyList<SearchEntryResult> entries, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    private static async IAsyncEnumerable<SearchEntryResult> CancelAfterFullPage(
        CancellationTokenSource cancellation, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return Entry("p1");
        yield return Entry("p2");
        await cancellation.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
