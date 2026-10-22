using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Shouldly;
using Xunit;
using static Ignixa.Search.Sql.Tests.TestSupport.IncludesShapePlans;
using SearchSortOrder = Ignixa.Search.Expressions.SortOrder;

namespace Ignixa.Search.Sql.Tests.Ast;

/// <summary>
/// Pins <see cref="MatchPageReplay"/> on an <see cref="ResultShape.IncludesPage"/>: a sorted <c>$includes</c>
/// page seeds its stages from exactly the match page the original search returned — the first PageSize
/// matches in the requested order after the keyset boundary, with no has-more probe row — while its include
/// rows still page by <see cref="IncludeBoundary"/> over (T1, Sid1).
/// </summary>
public class EmitIncludesMatchReplayTests
{
    private static readonly SortSpec DateDescending = new([new SortKey(203, SortKeyKind.Date, SearchSortOrder.Descending)], SortPhase.Valued);

    private static readonly PageSpec TypelessDateBoundary = new([new SqlParameterRef("2024-03-01")], BoundaryResourceTypeId: null, new SqlParameterRef(4200L));

    [Fact]
    public void GivenAReplayedCustomSortPage_WhenEmitted_ThenTheMatchSeedIsTheTopPageSizeRowsAfterTheBoundaryInSortOrder()
    {
        var plan = Replay(new MatchPageReplay(10, TypelessDateBoundary), DateDescending, resume: new IncludeBoundary(105, 900));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.Resource\n" +
            "    WHERE ResourceTypeId = @p0 AND IsHistory = 0 AND IsDeleted = 0\n" +
            "),\n" +
            "cteMatchPage AS (\n" +
            "    SELECT TOP (10) m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "INNER JOIN dbo.DateTimeSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 203 AND sk0.IsMax = 1\n" +
            "    WHERE (sk0.StartDateTime < @p1\n" +
            "       OR (sk0.StartDateTime = @p1 AND m.Sid1 > @p2))\n" +
            "    ORDER BY sk0.StartDateTime DESC, m.Sid1 ASC\n" +
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
            "SELECT DISTINCT TOP (11) T1, Sid1, IsMatch,\n" +
            "       CAST(CASE WHEN COUNT_BIG(*) OVER() > 10 THEN 1 ELSE 0 END AS bit) AS IsPartial\n" +
            "FROM (\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit) AS IsMatch FROM inc0 i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            ") includeUnion\n" +
            "WHERE (T1 > @p3 OR (T1 = @p3 AND Sid1 > @p4))\n" +
            "ORDER BY T1 ASC, Sid1 ASC");
    }

    public static TheoryData<string, SortSpec?, PageSpec?> ReplayedPages()
    {
        var typedFirstKeyless = new PageSpec([], new SqlParameterRef((short)103), new SqlParameterRef(4200L));
        var typedSid = new PageSpec([new SqlParameterRef(4200L)], new SqlParameterRef((short)103), new SqlParameterRef(4200L));
        return new TheoryData<string, SortSpec?, PageSpec?>
        {
            { "default order, first page", null, null },
            { "default order, later page", null, typedFirstKeyless },
            { "_lastUpdated", new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SearchSortOrder.Ascending)], SortPhase.Valued), typedSid },
            { "-_lastUpdated", new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SearchSortOrder.Descending)], SortPhase.Valued), typedSid },
            { "-date, first page", DateDescending, null },
            { "-date, later page", DateDescending, TypelessDateBoundary },
            { "date, missing-value phase", new SortSpec([new SortKey(203, SortKeyKind.Date, SearchSortOrder.Ascending)], SortPhase.MissingPrimary), new PageSpec([], null, new SqlParameterRef(4200L)) },
            { "name,-date (two keys)", new SortSpec([new SortKey(202, SortKeyKind.String, SearchSortOrder.Ascending), new SortKey(203, SortKeyKind.Date, SearchSortOrder.Descending)], SortPhase.Valued), new PageSpec([new SqlParameterRef("smith"), new SqlParameterRef("2024-03-01")], null, new SqlParameterRef(4200L)) },
        };
    }

    [Theory]
    [MemberData(nameof(ReplayedPages))]
    public void GivenAReplayedPage_WhenEmitted_ThenItSeeksAndOrdersExactlyAsTheMatchPageItReplays(string page, SortSpec? sort, PageSpec? boundary)
    {
        // The whole point of the replay: the include seed must be the same rows the original search page
        // returned. Both read the match set through the same joins, seek and ORDER BY -- only the select list
        // differs (the original projects SortValueN for its own assembly and over-fetches a probe row).
        _ = page;
        var replay = new MatchPageReplay(10, boundary);

        var original = MatchPageFromFrom(SqlBuilder.Run(OriginalMatchesPage(replay, sort)).Sql);
        var replayed = MatchPageFromFrom(SqlBuilder.Run(Replay(replay, sort)).Sql);

        replayed.ShouldBe(original);
    }

    [Theory]
    [MemberData(nameof(ReplayedPages))]
    public void GivenAReplayedPage_WhenEmitted_ThenItFetchesExactlyPageSizeRowsAndNoProbeRow(string page, SortSpec? sort, PageSpec? boundary)
    {
        _ = page;
        var sql = SqlBuilder.Run(Replay(new MatchPageReplay(10, boundary), sort)).Sql;

        MatchPageBody(sql).ShouldStartWith("cteMatchPage AS (\n    SELECT TOP (10) m.T1, m.Sid1\n");
        sql.ShouldNotContain("TOP (11) m.T1");
        sql.ShouldNotContain(SqlLabels.MatchSeed);
        sql.ShouldContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId");
    }

    [Fact]
    public void GivenADescendingLastUpdatedReplay_WhenEmitted_ThenItSeeksDownwardFromTheBoundaryWithNoSurrogateWindow()
    {
        // The case the surrogate-id window could not express without first > last: under -_lastUpdated the page
        // walks surrogate ids downward. The replay needs no window at all -- it seeks below the boundary in the
        // sort's own direction.
        var sort = new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SearchSortOrder.Descending)], SortPhase.Valued);
        var boundary = new PageSpec([new SqlParameterRef(4200L)], new SqlParameterRef((short)103), new SqlParameterRef(4200L));

        var body = MatchPageBody(SqlBuilder.Run(Replay(new MatchPageReplay(10, boundary), sort)).Sql);

        body.ShouldContain("m.Sid1 < @p1");
        body.ShouldContain("ORDER BY m.Sid1 DESC");
        body.ShouldNotContain("m.Sid1 >=");
        body.ShouldNotContain("m.Sid1 <=");
    }

    [Fact]
    public void GivenAReplayAndAResumeBoundary_WhenEmitted_ThenTheIncludeRowsStillPageByT1Sid1AfterTheSeekParameters()
    {
        var plan = Replay(new MatchPageReplay(10, TypelessDateBoundary), DateDescending, resume: new IncludeBoundary(105, 900));

        var emitted = SqlBuilder.Run(plan);

        // @p0 is the match type, @p1/@p2 the seek's date and surrogate id, then the resume boundary.
        emitted.Sql.ShouldContain(") includeUnion\nWHERE (T1 > @p3 OR (T1 = @p3 AND Sid1 > @p4))");
        emitted.Sql.TrimEnd().ShouldEndWith("ORDER BY T1 ASC, Sid1 ASC");
        emitted.Parameters.Select(p => p.Value).ShouldBe([(short)103, "2024-03-01", 4200L, (short)105, 900L]);
    }

    [Fact]
    public void GivenAReplay_WhenExplained_ThenEveryParameterIsNamedInBindOrder()
    {
        var plan = Replay(new MatchPageReplay(10, TypelessDateBoundary), DateDescending, resume: new IncludeBoundary(105, 900));

        var text = PlanExplainer.Print(PlanExplainer.Describe(plan));

        text.ShouldContain("MatchPageCte(top=10, sortJoins=true, resourceJoin=false) PageSpec(boundary=[@p1], type=none, sid=@p2)");
        text.ShouldContain("includeBoundary = IncludeBoundary(type=@p3, sid=@p4)");
    }

    [Fact]
    public void GivenAReplayWithAProbeRow_WhenEmitted_ThenItIsRejected()
    {
        var replay = new MatchPageReplay(10, TypelessDateBoundary);
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(PatientTypeId)],
            ReplaySpec(replay, DateDescending) with { TopIncludesProbeRow = true },
            [ForwardStage()]);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan)).Message.ShouldContain("probe row");
    }

    [Fact]
    public void GivenAReplayWithAnOffsetPage_WhenEmitted_ThenItIsRejected()
    {
        var replay = new MatchPageReplay(10);
        var spec = ReplaySpec(replay, sort: null) with { Top = null, OffsetPage = new OffsetSpec(0, 10) };
        var plan = IncludePlanFactory.Create([new CteDefinition.ResourceSource(PatientTypeId)], spec, [ForwardStage()]);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan)).Message.ShouldContain("OffsetPage");
    }

    public static TheoryData<string, Func<MatchPageSpec, MatchPageSpec>> DisagreeingSpecs() => new()
    {
        { "a different Top", spec => spec with { Top = 11 } },
        { "no Top", spec => spec with { Top = null } },
        { "a different boundary", spec => spec with { Page = new PageSpec([new SqlParameterRef("2023-01-01")], null, new SqlParameterRef(1L)) } },
        { "no boundary", spec => spec with { Page = null } },
    };

    [Theory]
    [MemberData(nameof(DisagreeingSpecs))]
    public void GivenAMatchPageSpecThatDisagreesWithItsReplay_WhenEmitted_ThenItIsRejected(string disagreement, Func<MatchPageSpec, MatchPageSpec> rewrite)
    {
        _ = disagreement;
        var spec = rewrite(ReplaySpec(new MatchPageReplay(10, TypelessDateBoundary), DateDescending));
        var plan = IncludePlanFactory.Create([new CteDefinition.ResourceSource(PatientTypeId)], spec, [ForwardStage()]);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan)).Message.ShouldContain("MatchReplay disagrees");
    }

    [Fact]
    public void GivenATypedBoundaryUnderACustomSort_WhenReplayed_ThenTheExistingKeysetGuardStillRejectsIt()
    {
        var typed = new PageSpec([new SqlParameterRef("2024-03-01")], new SqlParameterRef((short)103), new SqlParameterRef(4200L));

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(Replay(new MatchPageReplay(10, typed), DateDescending)))
            .Message.ShouldContain("typed keyset Page");
    }

    [Fact]
    public void GivenAnIncludesPageWithoutAReplay_WhenEmitted_ThenItsSqlIsUnchanged()
    {
        // The replay is opt-in: an includes page that names none still seeds from the whole (windowed) match
        // set, so the statements fhir-server's custom procedures are keyed on keep their hashes.
        var plain = IncludePlanFactory.Create([new CteDefinition.ResourceSource(PatientTypeId)], new MatchPageSpec(new CteRef(0), Sort: DateDescending, Shape: new ResultShape.IncludesPage(new IncludeBoundary(105, 900))), [ForwardStage()]);

        var body = MatchPageBody(SqlBuilder.Run(plain).Sql);

        body.ShouldNotContain("TOP");
        body.ShouldNotContain("ORDER BY");
    }

    /// <summary>The match-page CTE from its FROM clause to its closing paren: everything but the select list.</summary>
    private static string MatchPageFromFrom(string sql)
    {
        var body = MatchPageBody(sql);
        return body[body.IndexOf("\n    FROM ", StringComparison.Ordinal)..];
    }

    private static string MatchPageBody(string sql)
    {
        var start = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        return sql[start..sql.IndexOf("\n)", start, StringComparison.Ordinal)];
    }
}
