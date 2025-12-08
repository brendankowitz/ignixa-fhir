using FluentAssertions;
using Ignixa.FhirFaker.Cli.Discovery;

namespace Ignixa.FhirFaker.Cli.Tests;

public class StateDiscoveryTests
{
    [Fact]
    public void GivenStateDiscovery_WhenGettingObservationStateNames_ThenReturnsKnownStates()
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
    public void GivenValidStateName_WhenCreatingObservationState_ThenReturnsState()
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
    public void GivenInvalidStateName_WhenCreatingObservationState_ThenReturnsNull()
    {
        // Act
        var state = StateDiscovery.CreateObservationState("InvalidState");

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public void GivenValidCityName_WhenFindingCity_ThenReturnsCity()
    {
        // Act
        var city = StateDiscovery.FindCity("Seattle");

        // Assert
        city.Should().NotBeNull();
        city!.Name.Should().Be("Seattle");
    }

    [Fact]
    public void GivenInvalidCityName_WhenFindingCity_ThenReturnsNull()
    {
        // Act
        var city = StateDiscovery.FindCity("NonExistentCity");

        // Assert
        city.Should().BeNull();
    }
}
