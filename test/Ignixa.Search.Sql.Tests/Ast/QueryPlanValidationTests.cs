using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Tests.Ast;

public class QueryPlanValidationTests
{
    [Fact]
    public void GivenAMatchPageWithANullSpec_WhenRunOrExplained_ThenBothRejectThePublicPlan()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103), new CteDefinition.MatchPage(null!)],
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(1));

        AssertRejectedBySqlBuilderAndExplain(plan, "Ctes[1].Spec");
    }

    [Fact]
    public void GivenAMatchSeedWithANullSpec_WhenRunOrExplained_ThenBothRejectThePublicPlan()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), null!),
            ],
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(2));

        AssertRejectedBySqlBuilderAndExplain(plan, "Ctes[2].Spec");
    }

    [Fact]
    public void GivenAMatchSeedWithoutAnOffsetProbe_WhenRunOrExplained_ThenBothRejectInsteadOfDereferencingNull()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(2));

        AssertRejectedBySqlBuilderAndExplain(plan, "ProbeExtraRow");
    }

    [Fact]
    public void GivenACountPlanWithAnIncludeSeedButNoIncludes_WhenRunOrExplained_ThenBothRejectThePublicPlan()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0), Shape: new ResultShape.Count.AllMatches()),
            IncludeSeed: new CteRef(0));

        AssertRejectedBySqlBuilderAndExplain(plan, "CountOnly plans cannot carry IncludeSeed");
    }

    [Fact]
    public void GivenAnIncludeStageWithoutAnySeed_WhenRunOrExplained_ThenBothRejectBeforeSqlIsEmitted()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var stage = ForwardIncludeStage() with { SeedFromMatch = false };
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103), new CteDefinition.MatchPage(spec)],
            spec,
            Includes: [stage],
            IncludeSeed: new CteRef(1));

        AssertRejectedBySqlBuilderAndExplain(plan, "SeedFromMatch or SeedStages");
    }

    [Fact]
    public void GivenAnIncludePlanWithNoMatchPageWrapper_WhenRunOrExplained_ThenBothRejectThePublicPlan()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(0));

        AssertRejectedBySqlBuilderAndExplain(plan, "exactly one MatchPage");
    }

    [Fact]
    public void GivenAnIncludePlanWithANonFinalSoleMatchPage_WhenRunOrExplained_ThenBothRejectThePublicPlan()
    {
        var spec = new MatchPageSpec(new CteRef(0));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.ResourceSource(111),
            ],
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(1));

        AssertRejectedBySqlBuilderAndExplain(plan, "final CTE");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void GivenAMatchPageWhoseRootIsSelfOrForward_WhenRunOrExplained_ThenBothRejectThePublicPlan(int rootIndex)
    {
        var spec = new MatchPageSpec(new CteRef(rootIndex));
        IReadOnlyList<CteDefinition> ctes = rootIndex == 1
            ? [new CteDefinition.ResourceSource(103), new CteDefinition.MatchPage(spec)]
            :
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.ResourceSource(111),
            ];
        var plan = new QueryPlan(
            ctes,
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(1));

        AssertRejectedBySqlBuilderAndExplain(plan, "Ctes[1].Spec");
    }

    [Fact]
    public void GivenAnOffsetProbedIncludePlan_WhenDescribed_ThenMatchSeedReferencesItsMatchPage()
    {
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.MatchPage(spec),
                new CteDefinition.MatchSeed(new CteRef(1), spec),
            ],
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(2));

        var rows = PlanExplainer.Describe(plan);

        var matchSeed = rows.Single(row => row.Kind == PlanRowKind.MatchSeedCte);
        matchSeed.ReferencedCteIndexes.ShouldBe([1]);
    }

    [Theory]
    [MemberData(nameof(ShapeLevelViolations))]
    public void GivenAShapeLevelViolation_WhenRunOrExplained_ThenBothRejectItRatherThanOnlyRun(
        MatchPageSpec spec,
        string expectedMessage)
    {
        // Shape, paging and sort guards used to live behind a second entry point that only SqlBuilder.Run
        // called, so Explain happily described plans that could never be emitted -- and a golden fixture
        // drifted onto exactly such a plan. Validation now has one entry point; this pins that.
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], spec);

        AssertRejectedBySqlBuilderAndExplain(plan, expectedMessage);
    }

    public static TheoryData<MatchPageSpec, string> ShapeLevelViolations() => new()
    {
        { new MatchPageSpec(new CteRef(0), Top: -1), "Top must not be negative" },
        {
            new MatchPageSpec(new CteRef(0), Sort: new SortSpec(
                [
                    new SortKey(1, SortKeyKind.String, SortOrder.Ascending),
                    new SortKey(2, SortKeyKind.String, SortOrder.Ascending),
                    new SortKey(3, SortKeyKind.String, SortOrder.Ascending),
                    new SortKey(4, SortKeyKind.String, SortOrder.Ascending),
                ],
                SortPhase.Valued)),
            "at most 3 keys"
        },
        {
            new MatchPageSpec(new CteRef(0), Top: 5, OffsetPage: new OffsetSpec(0, 5)),
            "OffsetPage cannot be combined with Top"
        },
        {
            new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(-1, 5)),
            "non-negative row count"
        },
        {
            new MatchPageSpec(new CteRef(0), Sort: new SortSpec([], (SortPhase)99)),
            "is not a phase this compiler recognises"
        },
    };

    [Fact]
    public void GivenAnAggregatedSortKeyWithNoTableOrColumn_WhenRunOrExplained_ThenBothRejectItRatherThanDereferencingNull()
    {
        // SortKey is a public record whose Table and Column default to null, and whose doc says they are
        // non-null only for Aggregated -- documentation, not enforcement. Unguarded, AggregatedSortKeyEmitter
        // reads key.Column!.Name and throws NullReferenceException, which neither TryCompile's filter nor the
        // plan-trace guard catches, so it escapes a Try* method as a bare "Object reference not set".
        var spec = new MatchPageSpec(
            new CteRef(0),
            Sort: new SortSpec([new SortKey(202, SortKeyKind.Aggregated, SortOrder.Ascending)], SortPhase.Valued));
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], spec);

        AssertRejectedBySqlBuilderAndExplain(plan, "names no Table");
    }

    [Theory]
    [InlineData(SortKeyKind.String)]
    [InlineData(SortKeyKind.Date)]
    public void GivenASearchParamSortKeyWithNoSearchParamId_WhenRunOrExplained_ThenBothRejectItRatherThanEmittingInvalidSql(SortKeyKind kind)
    {
        // A null SearchParamId interpolates to nothing, emitting "SearchParamId =  AND ..." -- SQL that is
        // handed back as a successful compile and fails as an opaque syntax error at execution, far from the
        // cause. Exactly what the empty-type-list guard exists to prevent, for a different member.
        var spec = new MatchPageSpec(
            new CteRef(0),
            Sort: new SortSpec([new SortKey(null, kind, SortOrder.Ascending)], SortPhase.Valued));
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], spec);

        AssertRejectedBySqlBuilderAndExplain(plan, "names no SearchParamId");
    }

    [Fact]
    public void GivenAnEmptyUnion_WhenRunOrExplained_ThenItIsRejectedWithAMessageAboutPartsNotResourceTypes()
    {
        // The parts are CteRefs joined with UNION, so an empty list leaves the CTE body empty and does not
        // parse -- the same failure as an empty type list, but a caller told to "name the resource types"
        // would be sent to add something this node does not have.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103), new CteDefinition.Union([])],
            new MatchPageSpec(new CteRef(1)));

        AssertRejectedBySqlBuilderAndExplain(plan, "names no CTEs to union");
    }

    [Fact]
    public void GivenALikePredicateCarryingANonStringValue_WhenRunOrExplained_ThenBothRejectItRatherThanThrowingAnUncatchableCast()
    {
        // EscapeLike used to cast its value to string unchecked. Predicate is a public construction surface,
        // so a non-string Like threw InvalidCastException -- a type neither TryCompile's filter nor the
        // plan-trace guard matches, so it escaped a Try* method as an unhandled exception. The refusal has
        // to be NotSupportedException for either filter to see it.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Like(
            new SqlColumnRef(table.TableName, "Text"),
            new SqlParameterRef(42),
            LikeMatch.Contains);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new MatchPageSpec(new CteRef(0)));

        AssertRejectedBySqlBuilderAndExplain(plan, "requires a string value");
    }

    private static void AssertRejectedBySqlBuilderAndExplain(QueryPlan plan, string expectedMessage)
    {
        var builderError = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
        builderError.Message.ShouldContain(expectedMessage);

        var explainerError = Should.Throw<NotSupportedException>(() => plan.Explain());
        explainerError.Message.ShouldContain(expectedMessage);
    }

    private static IncludeStage ForwardIncludeStage()
        => new(IncludeDirection.Forward, 210, [103], [111], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
}
