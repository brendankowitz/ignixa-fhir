using System.Runtime.CompilerServices;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Mcp.Tools.FhirOperations;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.Extensions;
using Medino;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Mcp;

public class SearchResourcesToolPagingTests
{
    [Theory]
    [InlineData("empty", false)]
    [InlineData("short", true)]
    [InlineData("lone", true)]
    [InlineData("full-probe", true)]
    [InlineData("terminal", false)]
    [InlineData("legacy-extra", true)]
    [InlineData("deleted-probe", true)]
    [InlineData("mixed-probe", true)]
    [InlineData("mixed-terminal", false)]
    public async Task GivenControlledSearchPage_WhenMcpSearches_ThenOnlyResourcesAreRenderedAndContinuationIsAccurate(
        string scenario, bool expectedHasMore)
    {
        var entries = Page(scenario);
        var tool = CreateTool(Stream(entries));

        var result = await tool.SearchResourcesAsync(
            "Patient", new Dictionary<string, string> { ["ct"] = ContinuationToken.Encode(4, 2) },
            count: 2, tenantId: 1, cancellationToken: CancellationToken.None);
        try
        {
            string[] expectedIds = scenario switch
            {
                "empty" or "lone" => [],
                "short" or "deleted-probe" => ["p1"],
                "mixed-probe" or "mixed-terminal" => ["before", "warning", "p1", "p2", "after"],
                _ => ["p1", "p2"],
            };
            result.Entries.Select(e => e.Resource.RootElement.GetProperty("id").GetString())
                .ShouldBe(expectedIds);
            result.HasMore.ShouldBe(expectedHasMore);
            result.Total.ShouldBe(9);
            result.ContinuationToken.ShouldBeNull();

            if (scenario.StartsWith("mixed", StringComparison.Ordinal))
            {
                result.Entries.Select(e => e.SearchMode)
                    .ShouldBe(["INCLUDE", "OUTCOME", "MATCH", "MATCH", "INCLUDE"]);
            }
        }
        finally
        {
            foreach (var entry in result.Entries)
            {
                entry.Resource.Dispose();
            }
        }
    }

    [Fact]
    public async Task GivenSourceCursorAndShortProbedPage_WhenSearching_ThenTheSourceCursorIsPreserved()
    {
        var tool = CreateTool(Stream([Entry("p1"), Probe()]), "source-next");

        var result = await tool.SearchResourcesAsync("Patient", [], count: 2, tenantId: 1);
        try
        {
            result.HasMore.ShouldBeTrue();
            result.ContinuationToken.ShouldBe("source-next");
            result.Entries.Count.ShouldBe(1);
        }
        finally
        {
            foreach (var entry in result.Entries)
            {
                entry.Resource.Dispose();
            }
        }
    }

    [Fact]
    public async Task GivenCancellationAfterFullPage_WhenMcpCollectsBoundary_ThenCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var tool = CreateTool(CancelAfterFullPage(cancellation));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            tool.SearchResourcesAsync("Patient", [], count: 2, tenantId: 1, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task GivenEmptyContentWithoutProbeFlag_WhenMcpSearches_ThenMalformedContentIsNotHidden()
    {
        var tool = CreateTool(Stream([new SearchEntryResult(
            "Patient", "corrupt", "1", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty)]));

        await Should.ThrowAsync<JsonException>(() =>
            tool.SearchResourcesAsync("Patient", [], count: 2, tenantId: 1, cancellationToken: CancellationToken.None));
    }

    private static SearchResourcesTool CreateTool(IAsyncEnumerable<SearchEntryResult> entries, string? continuationToken = null)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(entries, Total: 9, ContinuationToken: continuationToken));
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(new FhirRequestContext { TenantId = 1 });
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Search test", FhirVersion = "4.0" });
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetBaseSchemaProvider(FhirVersion.R4).Returns(FhirVersion.R4.GetSchemaProvider());
        var builder = Substitute.For<ISearchOptionsBuilder>();
        builder.Build(Arg.Any<string?>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<ISchema?>())
            .Returns(new SearchOptions
            {
                ResourceType = "Patient",
                MaxItemCount = 2,
                ContinuationToken = ContinuationToken.Encode(4, 2),
            });
        var factory = Substitute.For<ISearchOptionsBuilderFactory>();
        factory.Create(Arg.Any<FhirVersion>()).Returns(builder);
        return new SearchResourcesTool(accessor, store, mediator, factory, versionContext, accessor);
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
