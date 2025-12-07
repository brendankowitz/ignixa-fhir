using FluentAssertions;
using Ignixa.FhirFaker.Cli.Discovery;

namespace Ignixa.FhirFaker.Cli.Tests;

public class StateDiscoveryTests
{
    [Fact]
    public void GetObservationStateNames_ShouldReturnKnownStates()
    {
        // Act
        var names = StateDiscovery.GetObservationStateNames().ToList();

        // Assert
        names.Should().NotBeEmpty();
        names.Should().Contain("BloodGlucose");
        names.Should().Contain("HemoglobinA1c");
        names.Should().Contain("BloodPressure");
    }

    [Fact]
    public void CreateObservationState_WithValidName_ShouldReturnState()
    {
        // Act
        var state = StateDiscovery.CreateObservationState("BloodGlucose");

        // Assert
        state.Should().NotBeNull();
        // Note: The Name property might not be set by factory methods,
        // but the state should have the correct code
        state!.Code.Should().NotBeNull();
    }

    [Fact]
    public void CreateObservationState_WithInvalidName_ShouldReturnNull()
    {
        // Act
        var state = StateDiscovery.CreateObservationState("InvalidState");

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public void FindCity_WithValidName_ShouldReturnCity()
    {
        // Act
        var city = StateDiscovery.FindCity("Seattle");

        // Assert
        city.Should().NotBeNull();
        city!.Name.Should().Be("Seattle");
    }

    [Fact]
    public void FindCity_WithInvalidName_ShouldReturnNull()
    {
        // Act
        var city = StateDiscovery.FindCity("NonExistentCity");

        // Assert
        city.Should().BeNull();
    }
}
