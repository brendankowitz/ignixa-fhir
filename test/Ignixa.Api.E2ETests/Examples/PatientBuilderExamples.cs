// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Harness;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.Api.E2ETests.Examples;

/// <summary>
/// Example E2E tests demonstrating the new PatientBuilder integration with ScenarioBuilder.
/// These tests showcase best practices for creating realistic patient scenarios.
/// </summary>
/// <remarks>
/// Key API methods demonstrated:
/// - WithPatient: Flexible patient builder (simple or realistic demographics)
/// - WithSeattlePatient: Seattle-specific demographics shorthand
/// - WithPatientFromCity: City-specific demographics (e.g., KnownCities.Philadelphia)
/// </remarks>
public class PatientBuilderE2EExamples : CapabilityDrivenTestBase
{
    public PatientBuilderE2EExamples(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Demonstrates a complete clinical scenario with realistic patient,
    /// encounter, and observation. Shows how the new PatientBuilder API
    /// integrates seamlessly with existing ScenarioBuilder methods.
    /// </summary>
    [Fact]
    public async Task GivenRealisticPatientWithEncounterAndObservation_WhenSearchingByCode_ThenFindsObservation()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange - Complete clinical scenario
        var tag = Guid.NewGuid().ToString();

        var scenario = CreateScenario()
            .WithName("Complete Clinical Scenario")
            .WithDescription("Demonstrates PatientBuilder integration with clinical states")
            .WithTag(tag)
            // Start with a realistic patient from Philadelphia
            .WithPatientFromCity(
                KnownCities.Philadelphia,
                p => p
                    .WithAge(50)
                    .WithGender(g => g.Male)
                    .WithRealisticBMI())
            // Add clinical encounter
            .AddEncounter("Annual Physical")
            // Add vital signs observations
            .AddObservation(VitalSigns.BloodPressureSystolic, 128m, "mmHg")
            .AddObservation(VitalSigns.BloodPressureDiastolic, 82m, "mmHg")
            .AddObservation(VitalSigns.HeartRate, 72m, "beats/minute", "/min")
            .Build();

        await Harness.CreateResourcesAsync(scenario.AllResources.ToArray());

        // Act - Search for blood pressure observations
        var results = await Harness.SearchAsync(
            "Observation",
            $"code={VitalSigns.BloodPressureSystolic.Code}&_tag={tag}");

        // Assert
        results.Should().ContainSingle();
        results[0].ResourceType.Should().Be("Observation");

        // Verify observation references the patient
        var subjectRef = results[0].MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Contain(scenario.Patient!.Id);
    }

}
