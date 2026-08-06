using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Tests.Ast;

/// <summary>
/// An over-fetched page returns Limit + 1 rows so the caller can detect a further page, then discards that
/// last row. These cover the consequence for _include/_revinclude: the discarded probe row must not seed
/// include stages, or its included resources survive into a bundle its own match row was trimmed out of.
/// </summary>
public class EmitProbeRowIncludeSeedTests
{
    [Fact]
    public void GivenAnOverFetchingPageWithAnInclude_WhenEmitted_ThenTheIncludeStageSeedsFromTheProbeFreeMatchSeed()
    {
        // Arrange
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111)],
            OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true));

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldContain(
            "cteMatchSeed AS (\n" +
            "    SELECT TOP (10) T1, Sid1\n" +
            "    FROM cteMatchPage\n" +
            "    ORDER BY T1 ASC, Sid1 ASC\n" +
            ")");
        sql.ShouldContain("SELECT 1 FROM cteMatchSeed m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        sql.ShouldNotContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId");
    }

    [Fact]
    public void GivenAnOverFetchingPageWithAnInclude_WhenEmitted_ThenTheMatchPageStillFetchesTheProbeRow()
    {
        // Arrange -- the probe row is what hasMore detection reads, so trimming the include seed must not
        // shrink the match page itself.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111)],
            OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("OFFSET @p1 ROWS FETCH NEXT @p2 ROWS ONLY");
        emitted.Parameters.Select(p => p.Value).ShouldBe([(short)103, 20, 11]);
    }

    [Fact]
    public void GivenAPageThatDoesNotOverFetch_WhenEmitted_ThenTheIncludeStageSeedsFromTheWholeMatchPage()
    {
        // Arrange -- no probe row means every fetched row is a genuine page member, so narrowing the seed
        // would drop a legitimate match's includes.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111)],
            OffsetPage: new OffsetSpec(20, 10));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("cteMatchSeed");
        emitted.Sql.ShouldContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        emitted.Parameters.Select(p => p.Value).ShouldBe([(short)103, 20, 10]);
    }

    [Fact]
    public void GivenAnUnpagedPlanWithAnInclude_WhenEmitted_ThenTheIncludeStageSeedsFromTheWholeMatchPage()
    {
        // Arrange -- a Top-capped plan has no OffsetSpec at all and therefore no probe row.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Top: 50,
            Includes: [ForwardIncludeStage(103, 111)]);

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldNotContain("cteMatchSeed");
        sql.ShouldContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
    }

    [Fact]
    public void GivenAnOverFetchingPageWithARevInclude_WhenEmitted_ThenTheRevIncludeStageAlsoSeedsFromTheMatchSeed()
    {
        // Arrange -- a reverse stage correlates through r, not rsp, so it reaches the seed by a different
        // alias and would regress independently of the forward direction.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ReverseIncludeStage(103, 112)],
            OffsetPage: new OffsetSpec(0, 1, ProbeExtraRow: true));

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldContain("SELECT TOP (1) T1, Sid1\n    FROM cteMatchPage");
        sql.ShouldContain("SELECT 1 FROM cteMatchSeed m WHERE m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId");
        sql.ShouldNotContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = r.ResourceTypeId");
    }

    [Fact]
    public void GivenAnIncludeReachableFromBothAKeptMatchAndTheProbeRow_WhenEmitted_ThenTheSeedIsAnExistentialOverEveryKeptMatch()
    {
        // Arrange -- the sharing case cannot be resolved by dropping include rows after the fact: an
        // included resource reachable from BOTH a kept match and the probe row belongs in the page.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111)],
            OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- EXISTS over the seed set, so ONE qualifying kept match is enough to keep the row; and
        // the stage body is DISTINCT, so reachability from several kept matches still yields one row.
        sql.ShouldContain(
            "    WHERE rsp.SearchParamId = 210\n" +
            "      AND rsp.ResourceTypeId = 103\n" +
            "      AND r.ResourceTypeId = 111\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchSeed m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )");
        sql.ShouldContain("SELECT DISTINCT TOP (");
    }

    [Fact]
    public void GivenAnOverFetchingPageWithAnInclude_WhenEmitted_ThenTheMatchArmAndAntiJoinStillReadTheFullMatchPage()
    {
        // Arrange -- only the SEED narrows. The match arm must still emit the probe row (hasMore reads it),
        // and the anti-join must still exclude every match-page row from the include arm, or the probe row
        // would surface twice: once as a Match and once as an Include.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111)],
            OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldContain("CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage");
        sql.ShouldContain("WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)");
        sql.ShouldNotContain("NOT EXISTS (SELECT 1 FROM cteMatchSeed");
    }

    [Fact]
    public void GivenAnOverFetchingSortedPageWithAnInclude_WhenEmitted_ThenTheMatchSeedRepeatsTheMatchPageOrdering()
    {
        // Arrange -- "the first N rows" is only well defined under the match page's own ordering. A custom
        // sort key drops the T1 tiebreak, so the seed must drop it too or it takes a different N rows.
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Descending)], SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111)],
            Sort: sort,
            OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldContain("    ORDER BY sk0.Text DESC, m.Sid1 ASC\n    OFFSET");
        sql.ShouldContain(
            "cteMatchSeed AS (\n" +
            "    SELECT TOP (5) T1, Sid1\n" +
            "    FROM cteMatchPage\n" +
            "    ORDER BY SortValue0 DESC, Sid1 ASC\n" +
            ")");
    }

    [Fact]
    public void GivenAnOverFetchingPageWhoseWholeBudgetIsTheProbeRow_WhenEmitted_ThenTheMatchSeedAdmitsNoRows()
    {
        // Arrange -- the two-phase sort executor's floor case: the earlier phase already filled the page, so
        // this phase's only row is the lookahead. It is fetched, but nothing on it may pull includes.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111)],
            OffsetPage: new OffsetSpec(7, 0, ProbeExtraRow: true));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        SqlGrammar.AssertValid(emitted.Sql);
        emitted.Sql.ShouldContain("SELECT TOP (0) T1, Sid1\n    FROM cteMatchPage");
        emitted.Parameters.Select(p => p.Value).ShouldBe([(short)103, 7, 1]);
    }

    [Fact]
    public void GivenAnOverFetchingPageWhoseOnlyStageSeedsFromAnEarlierStage_WhenEmitted_ThenNoUnreferencedMatchSeedIsEmitted()
    {
        // Arrange -- nothing reads the match page, so emitting a seed CTE would be dead SQL.
        var iterateOnly = ForwardIncludeStage(103, 111) with { SeedFromMatch = false, SeedStages = [], Iterate = false };
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [iterateOnly],
            OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldNotContain("cteMatchSeed");
    }

    [Fact]
    public void GivenAnOverFetchingPageWithChainedIncludeStages_WhenEmitted_ThenTheEmittedSqlIsValidAndFullyDefined()
    {
        // Arrange -- an :iterate stage seeds from the earlier stage's limit companion, not from the match
        // page, so the two seed labels coexist in one statement.
        var stage0 = ForwardIncludeStage(103, 111);
        var stage1 = ForwardIncludeStage(111, 112) with { SeedStages = [0], Iterate = true };
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage0, stage1],
            OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true));

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        SqlGrammar.AssertValid(sql);
        SqlGrammar.AssertEveryReferencedCteIsDefined(sql);
        sql.ShouldContain("SELECT 1 FROM cteMatchSeed m WHERE");
        sql.ShouldContain("SELECT 1 FROM inc0lim m WHERE");
    }

    [Fact]
    public void GivenAnOverFetchingPageWithNoIncludes_WhenEmitted_ThenTheFetchStillCountsTheProbeRow()
    {
        // Arrange -- the no-includes shape has no seed to narrow, but must still fetch the probe row.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("OFFSET @p1 ROWS FETCH NEXT @p2 ROWS ONLY");
        emitted.Sql.ShouldNotContain("cteMatchSeed");
        emitted.Parameters.Select(p => p.Value).ShouldBe([(short)103, 20, 10 + 1]);
    }

    [Fact]
    public void GivenAZeroLimitPageWithoutAProbeRow_WhenEmitted_ThenThrowsNotSupportedException()
    {
        // Arrange -- a zero fetch is what OFFSET/FETCH rejects; a zero page WITH a probe row still fetches
        // one and is legal, so the guard has to test the fetch count rather than the limit.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            OffsetPage: new OffsetSpec(0, 0));

        // Act & Assert
        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
    }

    private static IncludeStage ForwardIncludeStage(short seedType, short outputType)
        => new(IncludeDirection.Forward, ReferenceSearchParamId: 210, SeedTypeIds: [seedType], OutputTypeIds: [outputType],
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);

    private static IncludeStage ReverseIncludeStage(short seedType, short outputType)
        => new(IncludeDirection.Reverse, ReferenceSearchParamId: 211, SeedTypeIds: [seedType], OutputTypeIds: [outputType],
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
}
