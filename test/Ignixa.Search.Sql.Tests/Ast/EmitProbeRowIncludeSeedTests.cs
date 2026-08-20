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
    public void GivenAnOverFetchingIncludePlan_WhenConstructed_ThenMatchSeedFollowsMatchPage()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(2));

        plan.Ctes[^2].ShouldBeOfType<CteDefinition.MatchPage>();
        plan.Ctes[^1].ShouldBeOfType<CteDefinition.MatchSeed>();
        plan.IncludeSeed.ShouldBe(new CteRef(plan.Ctes.Count - 1));
    }

    [Fact]
    public void GivenAnOverFetchingIncludePlan_WhenEmittedFromWrapperCtes_ThenItsSqlAndParametersMatchThePinnedGolden()
    {
        var emitted = SqlBuilder.Run(OverFetchingIncludePlan());

        emitted.Sql.ShouldContain("cteMatchPage AS (\n    SELECT");
        emitted.Sql.ShouldContain("cteMatchSeed AS (\n    SELECT TOP (10) T1, Sid1");
        emitted.Parameters.Select(p => p.Name).ShouldBe(["@p0", "@p1", "@p2"]);
        emitted.Parameters.Select(p => p.Value).ShouldBe([(short)103, 20, 11]);
    }

    [Fact]
    public void GivenATopCappedOverFetchingIncludePlan_WhenEmitted_ThenTheMatchSeedTrimsTheProbeRowFromTheCap()
    {
        // A keyset page carries its over-fetch on the Top cap rather than an OffsetSpec, but the consequence
        // for include seeding is identical: the seed must trim to Top - 1. Before TopIncludesProbeRow existed
        // this plan emitted no cteMatchSeed at all and stages seeded from the probe row.
        var spec = new MatchPageSpec(new CteRef(0), Top: 11, TopIncludesProbeRow: true);
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(2));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("cteMatchPage AS (\n    SELECT TOP (11) m.T1, m.Sid1");
        sql.ShouldContain("cteMatchSeed AS (\n    SELECT TOP (10) T1, Sid1");
        sql.ShouldContain("SELECT 1 FROM cteMatchSeed m");

        // The Top - 1 arithmetic is reachable only on this path; the offset path's explain golden pins the
        // same row via OffsetSpec.Limit, so without this the subtraction is unpinned on the explain side.
        plan.Explain().ShouldContain("matchSeed = MatchSeedCte(limit=10)");
    }

    [Fact]
    public void GivenAMatchSeedOnAPageThatDoesNotOverFetch_WhenEmitted_ThenItIsRejectedBeforeWritingSql()
    {
        // The seed exists to trim a probe row; without one there is nothing to trim, and emitting it would
        // silently drop a genuine match row from the include seed.
        var spec = new MatchPageSpec(new CteRef(0), Top: 11);
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(2));

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan))
            .Message.ShouldContain("either an OffsetPage with ProbeExtraRow enabled");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void GivenTopIncludesProbeRowWithoutAUsableCap_WhenEmitted_ThenItIsRejectedBeforeWritingSql(int? top)
    {
        // The flag states that the cap is the page size plus a probe row, so an absent cap says nothing and a
        // cap of 0 leaves no page once the probe row is subtracted. Both are rejected before any SQL exists.
        var spec = new MatchPageSpec(new CteRef(0), Top: top, TopIncludesProbeRow: true);
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], spec);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan))
            .Message.ShouldContain("TopIncludesProbeRow");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void GivenTopIncludesProbeRowWithoutAUsableCap_WhenAsked_ThenTheSpecReportsNoTrimmedPageRatherThanANegativeOne(int? top)
    {
        // TrimmedPageSize's summary calls its result a row count, so a cap of 0 must answer "no trimmed page"
        // rather than "-1 rows are on it". The incoherent spec is still rejected -- by the guards asserted in
        // the theory above -- but no caller sees a row count that could not be one on the way there. Before
        // the two derived members collapsed into this one, the same state answered "yes, over-fetches" and
        // "no page size" simultaneously.
        var spec = new MatchPageSpec(new CteRef(0), Top: top, TopIncludesProbeRow: true);

        spec.TrimmedPageSize.ShouldBeNull();
    }

    [Fact]
    public void GivenAnOffsetProbeWithANegativeLimit_WhenAsked_ThenTheSpecReportsNoTrimmedPageRatherThanANegativeOne()
    {
        // The OFFSET branch answers first, so the Top branch's clamp does not cover it. OffsetSpec.Limit is
        // guarded in PlanShapeValidator, which QueryPlanValidator runs AFTER the reads of this member, and
        // Lower reads it with no validator in between at all -- so the member has to hold the line itself or
        // it hands out a row count that could not be one while validation is still deciding.
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, -5, ProbeExtraRow: true));

        spec.TrimmedPageSize.ShouldBeNull();
    }

    [Fact]
    public void GivenACapOfOne_WhenAsked_ThenItIsAcceptedAsAnEmptyPageRatherThanRejectedAsTooSmall()
    {
        // The accepted side of the cap boundary, which two caller-visible refusal messages assert is legal:
        // _count=0 arrives as Top = MaxItemCount + 1 = 1, one probe row and no page. Without this, tightening
        // the threshold to `cap <= 1` would break _count=0 with the whole suite still green.
        var spec = new MatchPageSpec(new CteRef(0), Top: 1, TopIncludesProbeRow: true);

        spec.TrimmedPageSize.ShouldBe(0);
    }

    [Fact]
    public void GivenAnOverFetchingPageWhoseIncludeStagesBypassTheMatchSeed_WhenEmitted_ThenItIsRejected()
    {
        // The original bug, reconstructed: a page that over-fetches, an include stage seeding from the match
        // set, and no MatchSeed to trim the probe row. Emitting this resolves includes for a resource the
        // caller is about to discard. Reachable through the documented `plan with { Query = rewritten }`
        // rewrite, so it has to be rejected as data rather than assumed away.
        var spec = new MatchPageSpec(new CteRef(0), Top: 11, TopIncludesProbeRow: true);
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(1));

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan))
            .Message.ShouldContain("requires a MatchSeed wrapper CTE");
    }

    [Fact]
    public void GivenAnIncludeConstraintTargetingMatchSeed_WhenEmitted_ThenItsGuardReferencesTheWrapperLabel()
    {
        var plan = OverFetchingIncludePlan();
        var constrainedStage = plan.Includes![0] with
        {
            Constraints = [new IncludeConstraint(ConstraintTypeId: 111, ConstraintCteIndex: plan.IncludeSeed!.Value.Index)],
        };
        plan = plan with { Includes = [constrainedStage] };

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("r.ResourceTypeId <> 111 OR EXISTS (SELECT 1 FROM cteMatchSeed ac");
        sql.ShouldNotContain("EXISTS (SELECT 1 FROM cte2 ac");
    }

    [Fact]
    public void GivenAnIncludePlanWithADanglingMatchSeed_WhenEmitted_ThenItThrowsBeforeWritingSql()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(2), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(2));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("Ctes[2].Page");
    }

    [Fact]
    public void GivenAnIncludePlanWithACopiedMatchPageSpec_WhenEmitted_ThenItThrowsBeforeWritingSql()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var copiedSpec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103), new CteDefinition.MatchPage(copiedSpec)],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(1));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("canonical MatchPageSpec");
    }

    [Fact]
    public void GivenAnIncludePlanWithAMatchSeedTargetingANonPageCte_WhenEmitted_ThenItThrowsBeforeWritingSql()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(0), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(2));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("MatchPage");
    }

    [Fact]
    public void GivenAnIncludePlanWithoutAnIncludeSeed_WhenEmitted_ThenItThrowsBeforeWritingSql()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103), new CteDefinition.MatchPage(spec)],
            spec,
            Includes: [ForwardIncludeStage(103, 111)]);

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("IncludeSeed");
    }

    [Fact]
    public void GivenAnIncludePlanWithAnIncludeSeedOutsideTheWrappers_WhenEmitted_ThenItThrowsBeforeWritingSql()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103), new CteDefinition.MatchPage(spec)],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(0));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("IncludeSeed");
    }

    [Fact]
    public void GivenAnIncludePlanWithMultipleMatchPages_WhenEmitted_ThenItRejectsTheNonCanonicalWrapperTail()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchPage(spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(2));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("exactly one MatchPage");
    }

    [Fact]
    public void GivenAnIncludePlanWithANonAdjacentMatchSeed_WhenEmitted_ThenItRejectsTheNonCanonicalWrapperTail()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.ResourceSource(111),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(3));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("canonical wrapper tail");
    }

    [Fact]
    public void GivenAnIncludePlanWithMultipleMatchSeeds_WhenEmitted_ThenItRejectsTheNonCanonicalWrapperTail()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(3));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("at most one MatchSeed");
    }

    [Fact]
    public void GivenAnIncludePlanWithMatchSeedButPageIncludeSeed_WhenEmitted_ThenItRejectsTheWrongSeed()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(1));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("MatchSeed");
    }

    [Fact]
    public void GivenAnIncludePlanWithMatchSeedWithoutAProbeRow_WhenEmitted_ThenItRejectsTheSeed()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(2));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("ProbeExtraRow");
    }

    [Fact]
    public void GivenAnIncludePlanWithMatchSeedButNoMatchSeedingStage_WhenEmitted_ThenItRejectsTheSeed()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var stage = ForwardIncludeStage(103, 111) with { SeedFromMatch = false };
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [stage],
            IncludeSeed: new CteRef(2));

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("SeedFromMatch");
    }

    [Fact]
    public void GivenNoIncludeStages_WhenCreateIsCalled_ThenItRejectsTheInvalidFixture()
    {
        var error = Should.Throw<ArgumentException>(() => IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0)),
            []));

        error.ParamName.ShouldBe("includes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenANonIncludeOrCountPlanWithMatchPageWrapper_WhenEmitted_ThenItThrowsBeforeWritingSql(bool countOnly)
    {
        var spec = new MatchPageSpec(
            new CteRef(0),
            Shape: countOnly ? new ResultShape.Count.AllMatches() : null);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103), new CteDefinition.MatchPage(spec)],
            spec);

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain(countOnly ? "CountOnly" : "include");
    }

    [Fact]
    public void GivenAnOverFetchingPageWithAnInclude_WhenEmitted_ThenTheIncludeStageSeedsFromTheProbeFreeMatchSeed()
    {
        // Arrange
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true)),
            [ForwardIncludeStage(103, 111)]);

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
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true)),
            [ForwardIncludeStage(103, 111)]);

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
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(20, 10)),
            [ForwardIncludeStage(103, 111)]);

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
        // Arrange -- a Top-capped plan has no OffsetSpec at all, so this trim never engages for it.
        //
        // KNOWN GAP: this pins today's behavior, not a guarantee it is safe. Some Top callers use the same
        // "ask for Top+1 to detect hasMore" convention OffsetSpec used to rely on before ProbeExtraRow made it
        // explicit (see SearchPaging.Keyset's remarks) -- if such a caller combines that convention with
        // Include stages, the same probe-row-leaks-into-includes bug this PR fixes for OffsetSpec would still
        // reproduce here, undetected. No caller in this repo drives that combination yet (Ignixa.Search.Sql has
        // no consumer here besides its own generator/tests until the SqlServer data-layer migration lands), so
        // extending the trim to Top is deferred rather than speculatively designed against a caller that
        // doesn't exist yet. See the NOTE in SqlBuilder.EmitIncludesShape.
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), Top: 50),
            [ForwardIncludeStage(103, 111)]);

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
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 1, ProbeExtraRow: true)),
            [ReverseIncludeStage(103, 112)]);

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
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true)),
            [ForwardIncludeStage(103, 111)]);

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
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true)),
            [ForwardIncludeStage(103, 111)]);

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
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), Sort: sort, OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true)),
            [ForwardIncludeStage(103, 111)]);

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
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(7, 0, ProbeExtraRow: true)),
            [ForwardIncludeStage(103, 111)]);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        SqlGrammar.AssertValid(emitted.Sql);
        emitted.Sql.ShouldContain("SELECT TOP (0) T1, Sid1\n    FROM cteMatchPage");
        emitted.Parameters.Select(p => p.Value).ShouldBe([(short)103, 7, 1]);
    }

    [Fact]
    public void GivenAnIncludeStageWithoutAMatchOrStageSeed_WhenEmitted_ThenItRejectsTheMalformedPlan()
    {
        // Arrange
        var iterateOnly = ForwardIncludeStage(103, 111) with { SeedFromMatch = false, SeedStages = [], Iterate = false };
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true)),
            [iterateOnly]);

        var error = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));

        error.Message.ShouldContain("SeedFromMatch or SeedStages");
    }

    [Fact]
    public void GivenAnOverFetchingPageWithChainedIncludeStages_WhenEmitted_ThenTheEmittedSqlIsValidAndFullyDefined()
    {
        // Arrange -- an :iterate stage seeds from the earlier stage's limit companion, not from the match
        // page, so the two seed labels coexist in one statement.
        var stage0 = ForwardIncludeStage(103, 111);
        var stage1 = ForwardIncludeStage(111, 112) with { SeedStages = [0], Iterate = true };
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true)),
            [stage0, stage1]);

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
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true)));

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
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 0)));

        // Act & Assert
        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
    }

    private static IncludeStage ForwardIncludeStage(short seedType, short outputType)
        => new(IncludeDirection.Forward, ReferenceSearchParamId: 210, SeedTypeIds: [seedType], OutputTypeIds: [outputType],
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);

    private static QueryPlan OverFetchingIncludePlan()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true));
        return new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage(103, 111)],
            IncludeSeed: new CteRef(2));
    }

    private static IncludeStage ReverseIncludeStage(short seedType, short outputType)
        => new(IncludeDirection.Reverse, ReferenceSearchParamId: 211, SeedTypeIds: [seedType], OutputTypeIds: [outputType],
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
}
