using FluentAssertions;
using Ignixa.FhirFaker.Cli.Discovery;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFaker.Cli.Tests;

public class ScenarioDiscoveryTests
{
    [Fact]
    public void GivenScenarioDiscovery_WhenGettingScenarioNames_ThenReturnsKnownScenarios()
    {
        // Act
        var names = ScenarioDiscovery.GetScenarioNames().ToList();

        // Assert
        names.Should().NotBeEmpty();
        names.Should().Contain("DiabeticPatient");
        names.Should().Contain("AsthmaticChild");
        names.Should().Contain("PediatricEarInfection");
    }

    [Fact]
    public void GivenValidScenarioName_WhenCreatingScenario_ThenReturnsContext()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var context = ScenarioDiscovery.CreateScenario(schemaProvider, "DiabeticPatient");

        // Assert
        context.Should().NotBeNull();
        context!.Patient.Should().NotBeNull();
        context.AllResources.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenInvalidScenarioName_WhenCreatingScenario_ThenReturnsNull()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var context = ScenarioDiscovery.CreateScenario(schemaProvider, "InvalidScenario");

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public void GivenDifferentCasing_WhenCreatingScenario_ThenStillWorks()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var context = ScenarioDiscovery.CreateScenario(schemaProvider, "diabeticpatient");

        // Assert
        context.Should().NotBeNull();
    }
}
