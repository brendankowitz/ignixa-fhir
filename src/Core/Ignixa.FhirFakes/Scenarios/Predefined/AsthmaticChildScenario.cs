// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Abstractions;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating Pediatric Asthma scenarios.
/// </summary>
public static class AsthmaticChildScenario
{
    /// <summary>
    /// Generates a Pediatric Asthma scenario with exacerbations.
    ///
    /// Timeline:
    /// 1. Initial diagnosis in early childhood
    /// 2. Controller medication prescribed
    /// 3. Periodic check-ups with peak flow monitoring
    /// 4. Occasional exacerbations requiring rescue medication
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Child's age (default: 7).</param>
    /// <param name="gender">Child's gender (default: random).</param>
    /// <param name="severity">Asthma severity 1-4 (default: 2 - mild persistent).</param>
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetAsthmaticChild(
        this IFhirSchemaProvider schemaProvider,
        int age = 7,
        string? gender = null,
        int severity = 2)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Pediatric Asthma with Exacerbations")
            .WithDescription("Child with asthma diagnosis, controller medication, peak flow monitoring, and periodic exacerbations.")

            // Initial patient (child)
            .WithPatient(age: age, gender: gender)

            // Initial diagnosis
            .AddEncounter("Initial evaluation - recurrent wheezing")
            .AddObservation(ObservationState.PeakFlow())
            .AddConditionOnset(FhirCode.Conditions.Asthma, severity: severity, assignToAttribute: "asthma_condition")

            // Rescue inhaler
            .AddMedicationOrder(MedicationOrderState.Albuterol())

            // 3-month follow-up
            .DelayMonths(3)
            .AddEncounter("Asthma follow-up - 3 month")
            .AddObservation(ObservationState.PeakFlow())

            // Exacerbation event (simulate acute visit)
            .DelayMonths(2)
            .AddEmergencyVisit("Asthma exacerbation - wheezing and shortness of breath")
            .AddObservation(ObservationState.PeakFlow(250m)) // Lower than normal
            .IncrementAttribute("asthma_exacerbation_count")

            // Post-exacerbation follow-up
            .DelayWeeks(2)
            .AddEncounter("Post-exacerbation follow-up")
            .AddObservation(ObservationState.PeakFlow()) // Should be improving

            // 6-month follow-up
            .DelayMonths(3)
            .AddEncounter("Asthma follow-up - 6 month")
            .AddObservation(ObservationState.PeakFlow())

            // Annual follow-up
            .DelayMonths(6)
            .AddEncounter("Asthma annual review")
            .AddObservation(ObservationState.PeakFlow())

            .Build();
    }
}
