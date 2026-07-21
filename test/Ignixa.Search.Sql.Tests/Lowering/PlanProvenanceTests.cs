using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class PlanProvenanceTests
{
    [Fact]
    public void GivenALeafPredicate_WhenLowered_ThenItsCteTracesBackToThatNodeByReference()
    {
        // Arrange
        var (expression, symbols) = LowerTestFixtures.SingleStringPredicate();

        // Act
        var lowered = Lower.Run(
            expression, symbols, targetResourceType: "Patient",
            includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        var origin = lowered.Provenance.Origins.ShouldHaveSingleItem();
        ReferenceEquals(origin.SourceNode, expression).ShouldBeTrue();
    }

    [Fact]
    public void GivenANotModifiedPredicate_WhenLowered_ThenProvenanceIsTheOriginalNotTheClone()
    {
        // Arrange
        var (wrapper, inner, symbols) = LowerTestFixtures.NotModifiedPredicate();

        // Act
        var lowered = Lower.Run(
            wrapper, symbols, targetResourceType: "Patient",
            includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        lowered.Provenance.Origins.ShouldContain(o => ReferenceEquals(o.SourceNode, inner));
    }

    [Fact]
    public void GivenAnOrOfCompositeAlternatives_WhenLowered_ThenEachAlternativeHasItsOwnProvenance()
    {
        // Arrange
        var (wrapper, alternative1, alternative2, symbols) = LowerTestFixtures.OrOfCompositeAlternatives();

        // Act
        var lowered = Lower.Run(
            wrapper, symbols, targetResourceType: "Observation",
            includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        lowered.Provenance.Origins.Count.ShouldBe(2);
        var sourceNodes = lowered.Provenance.Origins.Select(o => o.SourceNode).ToList();
        ReferenceEquals(sourceNodes[0], sourceNodes[1]).ShouldBeFalse();
        ReferenceEquals(sourceNodes[0], alternative1).ShouldBeTrue();
        ReferenceEquals(sourceNodes[1], alternative2).ShouldBeTrue();
        sourceNodes.ShouldNotContain(n => ReferenceEquals(n, wrapper));
    }
}
