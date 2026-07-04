// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.States;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios.States;

public class ObservationStateCatalogTests
{
    [Fact]
    public void GivenObservationStateCatalog_WhenGettingNames_ThenReturnsKnownStates()
    {
        var names = ObservationStateCatalog.GetNames().ToList();

        names.ShouldContain("BloodGlucose");
        names.ShouldContain("HemoglobinA1c");
        names.ShouldContain("BloodPressure");
    }

    [Fact]
    public void GivenValidStateName_WhenCreating_ThenReturnsTrueWithState()
    {
        var created = ObservationStateCatalog.TryCreate("BloodGlucose", out var state);

        created.ShouldBeTrue();
        state.ShouldNotBeNull();
        state.Code.ShouldNotBeNull();
    }

    [Fact]
    public void GivenDifferentCasing_WhenCreating_ThenStillMatches()
    {
        var created = ObservationStateCatalog.TryCreate("bloodglucose", out var state);

        created.ShouldBeTrue();
        state.ShouldNotBeNull();
    }

    [Fact]
    public void GivenInvalidStateName_WhenCreating_ThenReturnsFalse()
    {
        var created = ObservationStateCatalog.TryCreate("InvalidState", out var state);

        created.ShouldBeFalse();
        state.ShouldBeNull();
    }

    [Fact]
    public void GivenNullName_WhenCreating_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => ObservationStateCatalog.TryCreate(null!, out _));
    }
}
