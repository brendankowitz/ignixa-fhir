using Shouldly;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class CteProvenanceTests
{
    [Fact]
    public void GivenANegativeCteIndex_WhenConstructed_ThenItThrows()
        => Should.Throw<ArgumentOutOfRangeException>(() => new CteProvenance(-1, null, null));

    [Fact]
    public void GivenANegativeParameterOrdinal_WhenConstructed_ThenItThrows()
        => Should.Throw<ArgumentOutOfRangeException>(() => new CteProvenance(0, -1, null));

    [Fact]
    public void GivenAnExemptCte_WhenConstructed_ThenItContributesNoOrdinals()
    {
        var provenance = new CteProvenance(0, null, null);

        provenance.ParameterOrdinal.ShouldBeNull();
        provenance.ContributingOrdinals.ShouldBeEmpty();
    }

    [Fact]
    public void GivenADirectlyAttributedCte_WhenConstructed_ThenItContributesItsOwnOrdinal()
    {
        // Arrange & Act -- consumers read ContributingOrdinals uniformly rather than branching on
        // whether ParameterOrdinal happens to be set.
        var provenance = new CteProvenance(2, 1, null);

        // Assert
        provenance.ContributingOrdinals.ShouldBe([1]);
    }

    [Fact]
    public void GivenADirectOrdinalMissingFromTheContributors_WhenConstructed_ThenItIsFoldedIn()
    {
        // The documented invariant is that a directly-attributed CTE always contributes itself, so the
        // ordinal is merged rather than trusted to already be present.
        var provenance = new CteProvenance(3, 5, null, [1]);

        provenance.ContributingOrdinals.ShouldBe([1, 5]);
    }

    [Fact]
    public void GivenUnsortedOrDuplicatedContributors_WhenConstructed_ThenTheyAreNormalized()
    {
        // The doc promises ascending and distinct; normalizing makes that true by construction rather
        // than by producer discipline.
        var provenance = new CteProvenance(4, null, null, [3, 1, 3, 0]);

        provenance.ContributingOrdinals.ShouldBe([0, 1, 3]);
    }

    [Fact]
    public void GivenANegativeContributor_WhenConstructed_ThenItThrows()
        => Should.Throw<ArgumentOutOfRangeException>(() => new CteProvenance(0, null, null, [-1]));

    [Fact]
    public void GivenACallerMutatingTheListAfterwards_WhenRead_ThenTheProvenanceIsUnchanged()
    {
        // Arrange
        var ordinals = new List<int> { 0, 1 };
        var provenance = new CteProvenance(2, null, null, ordinals);

        // Act
        ordinals.Add(99);

        // Assert -- the record copied on the way in.
        provenance.ContributingOrdinals.ShouldBe([0, 1]);
    }

    [Fact]
    public void GivenTwoProvenancesWithTheSameContributors_WhenCompared_ThenTheyAreEqual()
    {
        // A record compares a collection property by reference, so this needs the explicit Equals.
        var left = new CteProvenance(2, null, null, [0, 1]);
        var right = new CteProvenance(2, null, null, [0, 1]);

        left.ShouldBe(right);
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }
}
