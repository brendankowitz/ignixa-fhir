using Ignixa.Search.Sql.Builders;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Builders;

public class SqlVectorTextTests
{
    [Fact]
    public void GivenFloatsIncludingSubnormalAndNegativeZero_WhenFormatted_ThenRoundTripJsonArrayInInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            SqlVectorText.Format([1.5f, -0f, float.Epsilon, 0.1f, -3.4028235E+38f])
                .ShouldBe("[1.5,-0,1E-45,0.1,-3.4028235E+38]");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void GivenNoValues_WhenFormatted_ThenEmptyArray()
        => SqlVectorText.Format([]).ShouldBe("[]");
}
