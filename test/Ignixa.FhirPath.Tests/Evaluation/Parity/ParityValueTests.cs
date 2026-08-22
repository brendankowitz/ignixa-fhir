namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class ParityValueTests
{
    [Fact]
    public void GivenEqualTextWithDifferentCarriers_WhenRendered_ThenCarrierRemainsObservable()
    {
        // Arrange & Act
        var integer = ParityValue.Render(1, "integer");
        var text = ParityValue.Render("1", "integer");

        // Assert
        integer.ShouldBe("integer|1");
        text.ShouldBe("string|1");
    }

    [Fact]
    public void GivenANullComplexValue_WhenRendered_ThenNullCarrierIsExplicit()
    {
        // Arrange & Act
        var rendered = ParityValue.Render(null, "Quantity");

        // Assert
        rendered.ShouldBe("null|<null>");
    }
}
