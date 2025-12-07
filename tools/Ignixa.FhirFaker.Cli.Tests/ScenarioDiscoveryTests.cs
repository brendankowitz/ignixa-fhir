using FluentAssertions;
using Ignixa.FhirFaker.Cli.Discovery;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFaker.Cli.Tests;

public class ScenarioDiscoveryTests
{
    [Fact]
    public void GetScenarioNames_ShouldReturnKnownScenarios()
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
    public void CreateScenario_WithValidName_ShouldReturnContext()
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
    public void CreateScenario_WithInvalidName_ShouldReturnNull()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var context = ScenarioDiscovery.CreateScenario(schemaProvider, "InvalidScenario");

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public void CreateScenario_WithDifferentCasing_ShouldWork()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var context = ScenarioDiscovery.CreateScenario(schemaProvider, "diabeticpatient");

        // Assert
        context.Should().NotBeNull();
    }
}
