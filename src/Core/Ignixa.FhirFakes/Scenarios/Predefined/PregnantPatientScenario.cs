// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating Pregnancy scenarios.
/// </summary>
public static class PregnantPatientScenario
{
    /// <summary>
    /// Generates a Pregnancy scenario with prenatal visits.
    ///
    /// Timeline:
    /// 1. Initial pregnancy confirmation
    /// 2. First trimester visits (monthly)
    /// 3. Second trimester visits (every 4 weeks)
    /// 4. Third trimester visits (every 2 weeks, then weekly)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 28).</param>
    /// <param name="weekOfPregnancy">Starting week of pregnancy for scenario (default: 8).</param>
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetPregnantPatient(
        this IFhirSchemaProvider schemaProvider,
        int age = 28,
        int weekOfPregnancy = 8)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Pregnancy with Prenatal Care")
            .WithDescription("Patient pregnancy with standard prenatal visit schedule and monitoring.")

            // Initial patient
            .WithPatient(age: age, gender: "female")

            // Pregnancy confirmation visit
            .AddEncounter("Pregnancy confirmation visit")
            .AddConditionOnset(FhirCode.Conditions.PregnancyNormal, assignToAttribute: "pregnancy_condition")
            .SetAttribute("pregnancy_week", weekOfPregnancy)

            // Prenatal vitamins and folic acid
            .AddMedicationOrder(MedicationOrderState.PrenatalVitamins())
            .AddMedicationOrder(MedicationOrderState.FolicAcid());

        // First trimester remaining visits (weeks 8-12)
        for (int week = weekOfPregnancy + 4; week <= 12; week += 4)
        {
            builder
                .DelayWeeks(4)
                .SetAttribute("pregnancy_week", week)
                .AddEncounter($"Prenatal visit - Week {week}")
                .AddObservation(ObservationState.FetalHeartRate());
        }

        // Second trimester visits (weeks 16-28, every 4 weeks)
        for (int week = 16; week <= 28; week += 4)
        {
            builder
                .DelayWeeks(4)
                .SetAttribute("pregnancy_week", week)
                .AddEncounter($"Prenatal visit - Week {week}")
                .AddObservation(ObservationState.FetalHeartRate());
        }

        // Third trimester visits (weeks 30-36, every 2 weeks)
        for (int week = 30; week <= 36; week += 2)
        {
            builder
                .DelayWeeks(2)
                .SetAttribute("pregnancy_week", week)
                .AddEncounter($"Prenatal visit - Week {week}")
                .AddObservation(ObservationState.FetalHeartRate());
        }

        // Final weeks (37-40, weekly)
        for (int week = 37; week <= 40; week++)
        {
            builder
                .DelayWeeks(1)
                .SetAttribute("pregnancy_week", week)
                .AddEncounter($"Prenatal visit - Week {week}")
                .AddObservation(ObservationState.FetalHeartRate());
        }

        return builder.Build();
    }
}
