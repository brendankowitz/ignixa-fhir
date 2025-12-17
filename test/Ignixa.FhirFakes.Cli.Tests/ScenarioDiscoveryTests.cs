using Shouldly;
using Ignixa.FhirFakes.Cli.Discovery;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Cli.Tests;

public class ScenarioDiscoveryTests
{
    [Fact]
    public void GivenScenarioDiscovery_WhenGettingScenarioNames_ThenReturnsKnownScenarios()
    {
        // Act
        var names = ScenarioDiscovery.GetScenarioNames().ToList();

        // Assert
        names.ShouldNotBeEmpty();
        names.ShouldContain("DiabeticPatient");
        names.ShouldContain("AsthmaticChild");
        names.ShouldContain("PediatricEarInfection");
    }

    [Fact]
    public void GivenValidScenarioName_WhenCreatingScenario_ThenReturnsContext()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var context = ScenarioDiscovery.CreateScenario(schemaProvider, "DiabeticPatient");

        // Assert
        context.ShouldNotBeNull();
        context!.Patient.ShouldNotBeNull();
        context.AllResources.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenInvalidScenarioName_WhenCreatingScenario_ThenReturnsNull()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var context = ScenarioDiscovery.CreateScenario(schemaProvider, "InvalidScenario");

        // Assert
        context.ShouldBeNull();
    }

    [Fact]
    public void GivenDifferentCasing_WhenCreatingScenario_ThenStillWorks()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var context = ScenarioDiscovery.CreateScenario(schemaProvider, "diabeticpatient");

        // Assert
        context.ShouldNotBeNull();
    }
}
