using System.Globalization;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class ResourceParitySweepTests
{
    [Fact]
    public void GivenResourceBackedQuantityEquivalence_WhenSwept_ThenKnownAsymmetryIsReported()
    {
        // Arrange
        var resource = TargetedParityCorpus.Build()
            .Single(item => item.Version == FhirVersion.R4
                            && item.Features.Contains(ParityResourceFeature.QuantityEquivalence));

        // Act
        var report = ResourceParitySweep.Run([resource]);

        // Assert
        var divergence = report.Divergences.Single(
            item => item.Expression == "component.value.first() ~ component.value.skip(1).first()");
        divergence.Firely.Results.ShouldBe(["BOOLEAN|boolean|false"]);
        divergence.Ignixa.Results.ShouldBe(["BOOLEAN|boolean|true"]);
        report.Divergences.ShouldNotContain(
            item => item.Expression == "component.value.first() ~ component.value.last()");
    }

    [Fact]
    public void GivenCultureSpecificResource_WhenSwept_ThenCallingCultureIsRestored()
    {
        // Arrange
        var originalCulture = CultureInfo.CurrentCulture;
        var resource = TargetedParityCorpus.Build()
            .Single(item => item.Version == FhirVersion.R4
                            && item.Features.Contains(ParityResourceFeature.EquivalentOffsetTemporal));

        // Act
        ResourceParitySweep.Run([resource]);

        // Assert
        CultureInfo.CurrentCulture.ShouldBe(originalCulture);
    }
}
