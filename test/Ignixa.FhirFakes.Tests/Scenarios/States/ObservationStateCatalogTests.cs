// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.FhirFakes.Tests.Scenarios;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios.States;

[Collection(CatalogRegistrationGroup.Name)]
public class ObservationStateCatalogTests
{
    private static readonly string[] PinnedNames =
    [
        "BloodGlucose",
        "BloodPressure",
        "BodyHeight",
        "BodyMassIndex",
        "BodyTemperature",
        "BodyWeight",
        "FetalHeartRate",
        "HeartRate",
        "HemoglobinA1c",
        "PeakFlow",
        "RespiratoryRate",
    ];

    [Fact]
    public void GivenObservationStateCatalog_WhenGettingNames_ThenReturnsKnownStates()
    {
        var names = ObservationStateCatalog.GetNames().ToList();

        names.ShouldContain("BloodGlucose");
        names.ShouldContain("HemoglobinA1c");
        names.ShouldContain("BloodPressure");
    }

    [Fact]
    public void GivenTheCatalog_WhenListingNames_ThenMatchesThePinnedContract()
    {
        // Scoped to this library's own assembly: other tests may have registered additional
        // assemblies via ObservationStateCatalog.RegisterAssembly, and that registration is
        // process-lifetime (no unregister), so the pinned contract must only cover this library's
        // built-in states.
        var builtInNames = typeof(ObservationState)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(ObservationState))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var names = ObservationStateCatalog.GetNames()
            .Where(builtInNames.Contains)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        names.ShouldBe(PinnedNames);
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

    [Fact]
    public void GivenExternalAssemblyRegistered_WhenGettingNames_ThenItsFactoryIsDiscovered()
    {
        ObservationStateCatalog.RegisterAssembly(typeof(ObservationStateCatalogTests).Assembly);

        var names = ObservationStateCatalog.GetNames();

        names.ShouldContain("RegisteredTestObservation");
    }

    [Fact]
    public void GivenExternalAssemblyRegistered_WhenCreating_ThenItRunsLikeAnyOtherState()
    {
        ObservationStateCatalog.RegisterAssembly(typeof(ObservationStateCatalogTests).Assembly);

        var created = ObservationStateCatalog.TryCreate("RegisteredTestObservation", out var state);

        created.ShouldBeTrue();
        state.ShouldNotBeNull();
        state.Code.Code.ShouldBe("test-code");
    }

    [Fact]
    public void GivenAssemblyRegisteredTwice_WhenGettingNames_ThenItsFactoryAppearsOnlyOnce()
    {
        ObservationStateCatalog.RegisterAssembly(typeof(ObservationStateCatalogTests).Assembly);
        ObservationStateCatalog.RegisterAssembly(typeof(ObservationStateCatalogTests).Assembly);

        var matches = ObservationStateCatalog.GetNames().Count(n => n == "RegisteredTestObservation");

        matches.ShouldBe(1);
    }

    [Fact]
    public void GivenNullAssembly_WhenRegistering_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => ObservationStateCatalog.RegisterAssembly(null!));
    }
}
