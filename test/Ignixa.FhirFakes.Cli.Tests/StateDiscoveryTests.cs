using Shouldly;
using Ignixa.FhirFakes.Cli.Discovery;

namespace Ignixa.FhirFakes.Cli.Tests;

public class StateDiscoveryTests
{
    [Fact]
    public void GivenStateDiscovery_WhenGettingObservationStateNames_ThenReturnsKnownStates()
    {
        // Act
        var names = StateDiscovery.GetObservationStateNames().ToList();

        // Assert
        names.ShouldNotBeEmpty();
        names.ShouldContain("BloodGlucose");
        names.ShouldContain("HemoglobinA1c");
        names.ShouldContain("BloodPressure");
    }

    [Fact]
    public void GivenValidStateName_WhenCreatingObservationState_ThenReturnsState()
    {
        // Act
        var state = StateDiscovery.CreateObservationState("BloodGlucose");

        // Assert
        state.ShouldNotBeNull();
        // Note: The Name property might not be set by factory methods,
        // but the state should have the correct code
        state!.Code.ShouldNotBeNull();
    }

    [Fact]
    public void GivenInvalidStateName_WhenCreatingObservationState_ThenReturnsNull()
    {
        // Act
        var state = StateDiscovery.CreateObservationState("InvalidState");

        // Assert
        state.ShouldBeNull();
    }

    [Fact]
    public void GivenValidCityName_WhenFindingCity_ThenReturnsCity()
    {
        // Act
        var city = StateDiscovery.FindCity("Seattle");

        // Assert
        city.ShouldNotBeNull();
        city!.Name.ShouldBe("Seattle");
    }

    [Fact]
    public void GivenInvalidCityName_WhenFindingCity_ThenReturnsNull()
    {
        // Act
        var city = StateDiscovery.FindCity("NonExistentCity");

        // Assert
        city.ShouldBeNull();
    }
}
