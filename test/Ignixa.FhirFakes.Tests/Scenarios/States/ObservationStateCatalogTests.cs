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
        var names = ObservationStateCatalog.Names().ToList();

        names.ShouldContain("BloodGlucose");
        names.ShouldContain("HemoglobinA1c");
        names.ShouldContain("BloodPressure");
    }

    [Fact]
    public void GivenValidStateName_WhenCreating_ThenReturnsState()
    {
        var state = ObservationStateCatalog.Create("BloodGlucose");

        state.ShouldNotBeNull();
        state!.Code.ShouldNotBeNull();
    }

    [Fact]
    public void GivenDifferentCasing_WhenCreating_ThenStillMatches()
    {
        var state = ObservationStateCatalog.Create("bloodglucose");

        state.ShouldNotBeNull();
    }

    [Fact]
    public void GivenInvalidStateName_WhenCreating_ThenReturnsNull()
    {
        var state = ObservationStateCatalog.Create("InvalidState");

        state.ShouldBeNull();
    }

    [Fact]
    public void GivenNullName_WhenCreating_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => ObservationStateCatalog.Create(null!));
    }
}
