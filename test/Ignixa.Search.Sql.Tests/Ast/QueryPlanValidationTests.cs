using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

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
