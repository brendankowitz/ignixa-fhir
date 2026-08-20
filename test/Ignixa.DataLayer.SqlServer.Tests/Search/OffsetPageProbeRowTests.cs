using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.DataLayer.SqlServer.Tests.Search;

/// <summary>
/// Covers the translation of a caller's <see cref="SearchOptions"/> into the compiler's
/// <see cref="OffsetSpec"/>, and the consequence that translation has for _include seeding.
///
/// The adapter used to infer the over-fetch by subtracting one from <see cref="SearchOptions.MaxItemCount"/>,
/// which was only ever true of the paged search handler. Every other caller — $includes above all, since it
/// exists to mine its match rows for their includes — had its last match row demoted to a probe row, and a
/// probe row seeds no includes. These tests pin both halves: the arithmetic, and the SQL it produces.
/// </summary>
public class OffsetPageProbeRowTests
{
    private const short MatchTypeId = 103;
    private const short IncludedTypeId = 111;

    [Fact]
    public void GivenAProbingCallerWithNoToken_WhenBuildingThePage_ThenThePageIsTheCallersCountAndTheProbeIsOnTop()
    {
        // Arrange
        var options = new SearchOptions { MaxItemCount = 10, ProbeExtraRow = true };

        // Act
        OffsetSpec page = SqlServerCompiledSearchService.DefaultOffsetPage(options);

        // Assert
        page.ShouldBe(new OffsetSpec(0, 10, ProbeExtraRow: true));
        page.FetchCount.ShouldBe(11);
    }

    [Fact]
    public void GivenANonProbingCallerWithNoToken_WhenBuildingThePage_ThenEveryFetchedRowIsOnThePage()
    {
        // Arrange: $includes' widened match budget — a caller that never over-fetched.
        var options = new SearchOptions { MaxItemCount = 100 };

        // Act
        OffsetSpec page = SqlServerCompiledSearchService.DefaultOffsetPage(options);

        // Assert
        page.ShouldBe(new OffsetSpec(0, 100));
        page.FetchCount.ShouldBe(100);
    }

    [Fact]
    public void GivenAProbingCallerWithAToken_WhenBuildingThePage_ThenTheTokensCountIsThePageAndTheProbeIsOnTop()
    {
        // Arrange: the token stores the caller's own page size, never a pre-incremented one.
        var options = new SearchOptions
        {
            MaxItemCount = 10,
            ProbeExtraRow = true,
            ContinuationToken = ContinuationToken.Encode(offset: 40, count: 20),
        };

        // Act
        OffsetSpec page = SqlServerCompiledSearchService.DefaultOffsetPage(options);

        // Assert
        page.ShouldBe(new OffsetSpec(40, 20, ProbeExtraRow: true));
        page.FetchCount.ShouldBe(21);
    }

    [Fact]
    public void GivenANonProbingCallerWithAToken_WhenBuildingThePage_ThenNoProbeRowIsFetched()
    {
        // Arrange: the probe describes the request, not the cursor, so a token does not imply one.
        var options = new SearchOptions
        {
            MaxItemCount = 10,
            ContinuationToken = ContinuationToken.Encode(offset: 40, count: 20),
        };

        // Act
        OffsetSpec page = SqlServerCompiledSearchService.DefaultOffsetPage(options);

        // Assert
        page.ShouldBe(new OffsetSpec(40, 20));
        page.FetchCount.ShouldBe(20);
    }

    [Fact]
    public void GivenAnUndecodableToken_WhenBuildingThePage_ThenThePageFallsBackToTheCallersCount()
    {
        // Arrange
        var options = new SearchOptions { MaxItemCount = 25, ProbeExtraRow = true, ContinuationToken = "not-base64!" };

        // Act
        OffsetSpec page = SqlServerCompiledSearchService.DefaultOffsetPage(options);

        // Assert
        page.ShouldBe(new OffsetSpec(0, 25, ProbeExtraRow: true));
    }

    [Fact]
    public void GivenANonProbingCallerWithIncludes_WhenTheEmittedPageIsInspected_ThenIncludesSeedFromEveryFetchedRow()
    {
        // Arrange
        var options = new SearchOptions { MaxItemCount = 100 };

        // Act
        EmittedSql emitted = EmitWithInclude(options);

        // Assert: no seed CTE at all — the include stage correlates against the whole match page, so the
        // hundredth row's includes are in the bundle alongside it.
        emitted.Sql.ShouldNotContain("cteMatchSeed");
        emitted.Sql.ShouldContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        emitted.Parameters.Select(p => p.Value).ShouldBe([MatchTypeId, 0, 100]);
    }

    [Fact]
    public void GivenAProbingCallerWithIncludes_WhenTheEmittedPageIsInspected_ThenIncludesSeedFromThePageWithoutTheProbeRow()
    {
        // Arrange
        var options = new SearchOptions { MaxItemCount = 10, ProbeExtraRow = true };

        // Act
        EmittedSql emitted = EmitWithInclude(options);

        // Assert: eleven rows are read, ten of them seed includes.
        emitted.Sql.ShouldContain("SELECT TOP (10) T1, Sid1\n    FROM cteMatchPage");
        emitted.Sql.ShouldContain("SELECT 1 FROM cteMatchSeed m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        emitted.Parameters.Select(p => p.Value).ShouldBe([MatchTypeId, 0, 11]);
    }

    [Theory]
    [InlineData(10, true, 11)]
    [InlineData(10, false, 10)]
    [InlineData(100, false, 100)]
    [InlineData(50_000, false, 50_000)]
    public void GivenAnyCaller_WhenTheEmittedPageIsInspected_ThenTheFetchedRowCountIsThePagePlusTheProbe(
        int maxItemCount,
        bool probeExtraRow,
        int expectedFetchCount)
    {
        // Arrange
        var options = new SearchOptions { MaxItemCount = maxItemCount, ProbeExtraRow = probeExtraRow };

        // Act
        EmittedSql emitted = EmitWithInclude(options);

        // Assert
        emitted.Parameters[^1].Value.ShouldBe(expectedFetchCount);
    }

    private static EmittedSql EmitWithInclude(SearchOptions options)
    {
        // The spec instance must be shared between the MatchPage CTE and QueryPlan.MatchSpec: the
        // emitter rejects a plan whose wrapper CTE carries a copy rather than the canonical spec.
        var spec = new MatchPageSpec(
            new CteRef(0),
            OffsetPage: SqlServerCompiledSearchService.DefaultOffsetPage(options));

        List<CteDefinition> ctes =
        [
            new CteDefinition.ResourceSource(MatchTypeId),
            new CteDefinition.MatchPage(spec),
        ];

        // The trimmed seed exists only when the page over-fetches a probe row. Without one, include
        // stages correlate against the whole match page, which is what the non-probing case asserts.
        if (spec.OffsetPage!.ProbeExtraRow)
        {
            ctes.Add(new CteDefinition.MatchSeed(new CteRef(1), spec));
        }

        return SqlBuilder.Run(new QueryPlan(
            ctes,
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(ctes.Count - 1)));
    }

    private static IncludeStage ForwardIncludeStage()
        => new(
            IncludeDirection.Forward,
            ReferenceSearchParamId: 210,
            SeedTypeIds: [MatchTypeId],
            OutputTypeIds: [IncludedTypeId],
            SeedStages: [],
            SeedFromMatch: true,
            Iterate: false,
            Limit: 1000);
}
