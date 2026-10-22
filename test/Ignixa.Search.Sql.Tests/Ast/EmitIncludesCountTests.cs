using System.Text.RegularExpressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Shouldly;
using Xunit;
using static Ignixa.Search.Sql.Tests.TestSupport.IncludesShapePlans;

namespace Ignixa.Search.Sql.Tests.Ast;

/// <summary>
/// Pins <see cref="ResultShape.IncludesCount"/>: <c>$includes</c> under <c>_summary=count</c> counts the
/// distinct included resources of the whole search, through the same stage bodies an includes page reads,
/// never counting a match and never bounded by a page, sort, window, budget or resume boundary.
/// </summary>
public class EmitIncludesCountTests
{
    private const string ExcludeMatches = "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)";

    [Fact]
    public void GivenAnIncludesCountWithOneStage_WhenEmitted_ThenItCountsTheIncludeUnionOverTheUnpagedMatchSet()
    {
        var plan = IncludesCount(ForwardStage());

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.Resource\n" +
            "    WHERE ResourceTypeId = @p0 AND IsHistory = 0 AND IsDeleted = 0\n" +
            "),\n" +
            "cteMatchPage AS (\n" +
            "    SELECT m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "),\n" +
            "inc0 AS (\n" +
            "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam rsp\n" +
            "    INNER JOIN dbo.Resource r\n" +
            "        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "    WHERE rsp.SearchParamId = 55\n" +
            "      AND rsp.ResourceTypeId = 103\n" +
            "      AND r.ResourceTypeId = 105\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )\n" +
            ")\n" +
            "SELECT COUNT_BIG(*) FROM (\n" +
            "SELECT i.T1, i.Sid1 FROM inc0 i\n" +
            ExcludeMatches + "\n" +
            ") includeUnion");
        emitted.Parameters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenForwardReverseIterateAndWildcardStages_WhenCounted_ThenEveryStageIsADeduplicatedArmOfTheCount()
    {
        // :iterate and wildcard stages are counted, and a resource two stages both reach is counted once:
        // plain UNION dedupes on (T1, Sid1) before COUNT_BIG(*) runs.
        var plan = IncludesCount(ForwardStage(), ReverseStage(), IterateStage(seedStage: 0), WildcardReverseStage());

        var sql = SqlBuilder.Run(plan).Sql;

        var count = sql[sql.IndexOf("SELECT COUNT_BIG(*) FROM (", StringComparison.Ordinal)..];
        count.Split("\nUNION\n").Length.ShouldBe(4);
        count.ShouldNotContain("UNION ALL");
        for (var i = 0; i < 4; i++)
        {
            count.ShouldContain($"SELECT i.T1, i.Sid1 FROM inc{i} i\n{ExcludeMatches}");
        }

        // The iterate stage seeds from stage 0's uncapped body, and the wildcard stage names no parameter.
        sql.ShouldContain("SELECT 1 FROM inc0 m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        IncludeStageBody(sql, 3).ShouldNotContain("rsp.SearchParamId");
    }

    [Fact]
    public void GivenAnIncludesCount_WhenEmitted_ThenEveryArmExcludesTheWholeMatchSet()
    {
        // A match is never counted as an include. The exclusion reads cteMatchPage, which for this shape is the
        // whole match set rather than a page of it, so a match reachable as an include is excluded wherever it
        // falls in the search.
        var plan = IncludesCount(ForwardStage(), ReverseStage());

        var sql = SqlBuilder.Run(plan).Sql;

        Regex.Matches(sql, Regex.Escape(ExcludeMatches)).Count.ShouldBe(2);
        MatchPageBody(sql).ShouldNotContain("TOP");
        MatchPageBody(sql).ShouldNotContain("ORDER BY");
    }

    [Fact]
    public void GivenStagesWithIncludeLimits_WhenCounted_ThenNoBudgetTruncationOrOrderingAppliesAnywhere()
    {
        var plan = IncludesCount(ForwardStage(limit: 10), ReverseStage(limit: 10));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldNotContain("TOP");
        sql.ShouldNotContain("ORDER BY");
        sql.ShouldNotContain("IsPartial");
        sql.ShouldNotContain("OVER()");
        sql.ShouldNotContain("inc0lim");
        sql.ShouldNotContain("inc1lim");
        sql.ShouldNotContain("IsMatch");
    }

    [Fact]
    public void GivenAMatchSetFilteredByAResourceColumnAndAHash_WhenCounted_ThenTheMatchSeedKeepsBothFilters()
    {
        // The filters define which resources are matches -- they are the search, not a page of it -- so the
        // count's seed keeps them where the bounds below are rejected.
        var outer = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("abc"));
        var spec = new MatchPageSpec(new CteRef(0), OuterPredicate: outer, Shape: new ResultShape.IncludesCount(), SearchParameterHash: new SqlParameterRef("hash"));

        var sql = SqlBuilder.Run(IncludesCount(spec, ForwardStage())).Sql;

        MatchPageBody(sql).ShouldContain("INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1");
        MatchPageBody(sql).ShouldContain("WHERE r.ResourceId = @p1 AND (r.SearchParamHash IS NULL OR r.SearchParamHash <> @p2)");
    }

    [Fact]
    public void GivenAProjection_WhenCounted_ThenItIsIgnored()
    {
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(PatientTypeId)],
            new MatchPageSpec(new CteRef(0), Shape: new ResultShape.IncludesCount()),
            [ForwardStage()],
            projection: new ProjectionSpec(["RawResource"]));

        SqlBuilder.Run(plan).Sql.ShouldNotContain("RawResource");
    }

    public static TheoryData<string, MatchPageSpec> BoundedSeeds()
    {
        var root = new CteRef(0);
        var shape = new ResultShape.IncludesCount();
        var sort = new SortSpec([new SortKey(203, SortKeyKind.Date, Ignixa.Search.Expressions.SortOrder.Ascending)], SortPhase.Valued);
        return new TheoryData<string, MatchPageSpec>
        {
            { "a Top cap", new MatchPageSpec(root, Top: 10, Shape: shape) },
            { "a keyset Page", new MatchPageSpec(root, Page: new PageSpec([], new SqlParameterRef((short)103), new SqlParameterRef(42L)), Shape: shape) },
            { "an OffsetPage", new MatchPageSpec(root, Shape: shape, OffsetPage: new OffsetSpec(0, 10)) },
            { "a SurrogateRange", new MatchPageSpec(root, Shape: shape, SurrogateRange: new SurrogateIdRange(new SqlParameterRef(1L), new SqlParameterRef(2L))) },
            { "a Sort", new MatchPageSpec(root, Sort: sort, Shape: shape) },
        };
    }

    [Theory]
    [MemberData(nameof(BoundedSeeds))]
    public void GivenAHandBuiltIncludesCountWithABoundedSeed_WhenEmitted_ThenItIsRejectedRatherThanCountingPartOfTheIncludes(string bound, MatchPageSpec spec)
    {
        var plan = IncludesCount(spec, ForwardStage());

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan))
            .Message.ShouldContain($"ResultShape.IncludesCount cannot be combined with {bound}");
    }

    [Fact]
    public void GivenAnIncludesCountWithNoStages_WhenEmitted_ThenItIsRejected()
    {
        var plan = new QueryPlan([new CteDefinition.ResourceSource(PatientTypeId)], new MatchPageSpec(new CteRef(0), Shape: new ResultShape.IncludesCount()));

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan))
            .Message.ShouldContain("IncludesCount requires at least one include stage");
    }

    [Fact]
    public void GivenAnIncludesCount_WhenEmitted_ThenItIsNotACountOnlyOrIncludesPagePlan()
    {
        var plan = IncludesCount(ForwardStage());

        plan.IncludesCountOnly.ShouldBeTrue();
        plan.CountOnly.ShouldBeFalse();
        plan.IncludesOnly.ShouldBeFalse();
    }

    [Fact]
    public void GivenAnIncludesCount_WhenExplained_ThenItNamesTheShapeAndConsumesEveryParameter()
    {
        var outer = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("abc"));
        var plan = IncludesCount(new MatchPageSpec(new CteRef(0), OuterPredicate: outer, Shape: new ResultShape.IncludesCount()), ForwardStage());

        var rows = PlanExplainer.Describe(plan);

        rows.ShouldContain(row => row.Kind == PlanRowKind.IncludesCount && row.Body == "true");
        rows.ShouldNotContain(row => row.Kind == PlanRowKind.CountOnly);
    }

    [Fact]
    public void GivenAnIncludesCount_WhenEmittedWithTextRanges_ThenTheCountIsLabelledAsAnIncludeCount()
    {
        var emitted = SqlBuilder.Run(IncludesCount(ForwardStage()), new EmitOptions(IncludeTextRanges: true));

        var range = emitted.TextRanges!.Where(r => r.Kind == SqlRangeKind.IncludeCount).ShouldHaveSingleItem();
        range.Label.ShouldBe(SqlLabels.IncludeCount);
        emitted.Sql.Substring(range.Start, range.Length).ShouldStartWith("SELECT COUNT_BIG(*) FROM (");
    }

    private static string MatchPageBody(string sql)
    {
        var start = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        return sql[start..sql.IndexOf("\n)", start, StringComparison.Ordinal)];
    }

    private static string IncludeStageBody(string sql, int index)
    {
        var start = sql.IndexOf($"inc{index} AS (", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        return sql[start..sql.IndexOf("\n)", start, StringComparison.Ordinal)];
    }
}
