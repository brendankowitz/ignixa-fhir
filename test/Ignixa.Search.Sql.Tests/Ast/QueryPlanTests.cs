using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class QueryPlanTests
{
    [Fact]
    public void GivenAMatchPageSpec_WhenConstructingAPlan_ThenMatchRemainsItsPrePageRoot()
    {
        // Arrange
        var spec = new MatchPageSpec(new CteRef(0), Top: 10);

        // Act
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], spec);

        // Assert
        plan.Match.ShouldBe(new CteRef(0));
        plan.MatchSpec.ShouldBeSameAs(spec);
        plan.Top.ShouldBe(10);
    }

    [Fact]
    public void GivenWrapperCtesSharingTheCanonicalSpec_WhenConstructingAnIncludePlan_ThenTheSeedReferencesTheWrapper()
    {
        // Arrange
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var page = new CteDefinition.MatchPage(spec);
        var seed = new CteDefinition.MatchSeed(new CteRef(1), spec);

        // Act
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103), page, seed],
            spec,
            Includes: [ForwardIncludeStage()],
            IncludeSeed: new CteRef(2));

        // Assert
        plan.Ctes[1].ShouldBeSameAs(page);
        plan.Ctes[2].ShouldBeSameAs(seed);
        plan.IncludeSeed.ShouldBe(new CteRef(2));
    }

    [Fact]
    public void GivenAnIncludePlanWithoutAnOffsetProbe_WhenCreatingItsFixture_ThenThePageWrapperIsItsSeed()
    {
        // Arrange
        var spec = new MatchPageSpec(new CteRef(0));
        var includes = new[] { ForwardIncludeStage() };

        // Act
        var plan = IncludePlanFactory.Create([new CteDefinition.ResourceSource(103)], spec, includes);

        // Assert
        plan.Ctes.ShouldBe(
        [
            new CteDefinition.ResourceSource(103),
            new CteDefinition.MatchPage(spec),
        ]);
        plan.IncludeSeed.ShouldBe(new CteRef(1));
    }

    [Fact]
    public void GivenAnIncludePlanWithAnOffsetProbe_WhenCreatingItsFixture_ThenTheProbeFreeWrapperIsItsSeed()
    {
        // Arrange
        var spec = new MatchPageSpec(new CteRef(0), OffsetPage: new OffsetSpec(0, 5, ProbeExtraRow: true));
        var includes = new[] { ForwardIncludeStage() };

        // Act
        var plan = IncludePlanFactory.Create([new CteDefinition.ResourceSource(103)], spec, includes);

        // Assert
        plan.Ctes.ShouldBe(
        [
            new CteDefinition.ResourceSource(103),
            new CteDefinition.MatchPage(spec),
            new CteDefinition.MatchSeed(new CteRef(1), spec),
        ]);
        plan.IncludeSeed.ShouldBe(new CteRef(2));
    }

    [Fact]
    public void GivenAParamSourceAndAnIntersectReferencingIt_WhenConstructed_ThenTheGraphIsWellFormed()
    {
        // Arrange
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var stringPredicate = new Predicate.Equal(
            new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var tokenPredicate = new Predicate.Equal(new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"));

        // Act
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 103, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 103, 44, tokenPredicate),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            new MatchPageSpec(new CteRef(2), Top: 10));

        // Assert
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
        var intersect = plan.Ctes[2].ShouldBeOfType<CteDefinition.Intersect>();
        intersect.Left.ShouldBe(new CteRef(0));
        intersect.Right.ShouldBe(new CteRef(1));
        plan.Match.ShouldBe(new CteRef(2));
        plan.Top.ShouldBe(10);
    }

    [Fact]
    public void GivenAUnionOfTwoCteRefs_WhenConstructed_ThenPartsPreserveOrder()
    {
        // Act
        var union = new CteDefinition.Union([new CteRef(0), new CteRef(1)]);

        // Assert
        union.Parts.ShouldBe([new CteRef(0), new CteRef(1)]);
    }

    private static IncludeStage ForwardIncludeStage()
        => new(IncludeDirection.Forward, 210, [103], [111], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
}
