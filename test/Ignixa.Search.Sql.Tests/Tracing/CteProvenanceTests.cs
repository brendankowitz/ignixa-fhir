using Ignixa.Search.Sql.Tracing;
using Shouldly;

namespace Ignixa.Search.Sql.Tests.Tracing;

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
}
