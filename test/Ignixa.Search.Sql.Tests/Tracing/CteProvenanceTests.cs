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
    public void GivenAnExemptCte_WhenConstructed_ThenTheOrdinalAndSpanStayNull()
    {
        var provenance = new CteProvenance(0, null, null);

        provenance.ParameterOrdinal.ShouldBeNull();
        provenance.Span.ShouldBeNull();
    }
}
