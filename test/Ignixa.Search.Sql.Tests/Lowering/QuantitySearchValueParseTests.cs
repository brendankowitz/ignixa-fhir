using Ignixa.Search.Indexing.SearchValues;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Pins the three-state System/Code convention <see cref="QuantitySearchValue.Parse"/> produces, because
/// it is the input contract <c>QuantityColumnPredicate</c> lowers against: null means "not supplied" and
/// constrains nothing, empty means "supplied but empty" and constrains the stored value to be absent.
/// Collapsing the two at parse time would silently turn a <c>SystemId IS NULL</c> search into an
/// unconstrained one, which no lowering test could catch.
/// </summary>
public class QuantitySearchValueParseTests
{
    [Fact]
    public void GivenAValueWithNoSegments_WhenParsed_ThenSystemAndCodeAreNull()
    {
        // Act
        var value = QuantitySearchValue.Parse("5.4");

        // Assert
        value.Low.ShouldBe(5.4m);
        value.High.ShouldBe(5.4m);
        value.System.ShouldBeNull();
        value.Code.ShouldBeNull();
    }

    [Fact]
    public void GivenAValueWithAnEmptySystemSegment_WhenParsed_ThenSystemIsEmptyNotNull()
    {
        // Act
        var value = QuantitySearchValue.Parse("5.4||mg");

        // Assert
        value.System.ShouldBe(string.Empty);
        value.Code.ShouldBe("mg");
    }

    [Fact]
    public void GivenAValueWithBothSegments_WhenParsed_ThenSystemAndCodeAreBothPopulated()
    {
        // Act
        var value = QuantitySearchValue.Parse("5.4|http://unitsofmeasure.org|mg");

        // Assert
        value.System.ShouldBe("http://unitsofmeasure.org");
        value.Code.ShouldBe("mg");
    }

    [Fact]
    public void GivenAValueWithASystemButNoCodeSegment_WhenParsed_ThenCodeIsNull()
    {
        // Act
        var value = QuantitySearchValue.Parse("5.4|http://unitsofmeasure.org");

        // Assert
        value.System.ShouldBe("http://unitsofmeasure.org");
        value.Code.ShouldBeNull();
    }

    [Fact]
    public void GivenAValueWithAnEmptyCodeSegment_WhenParsed_ThenCodeIsEmptyNotNull()
    {
        // Act
        var value = QuantitySearchValue.Parse("5.4|http://unitsofmeasure.org|");

        // Assert
        value.System.ShouldBe("http://unitsofmeasure.org");
        value.Code.ShouldBe(string.Empty);
    }
}
