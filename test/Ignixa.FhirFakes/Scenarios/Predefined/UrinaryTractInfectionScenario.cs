// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating Urinary Tract Infection (UTI) scenarios.
/// </summary>
public static class UrinaryTractInfectionScenario
{
    /// <summary>
    /// Generates a Urinary Tract Infection (UTI) scenario - a simple acute condition.
    ///
    /// Timeline:
    /// 1. Initial ambulatory visit with UTI symptoms
    /// 2. Elevated body temperature
    /// 3. Pain severity observation
    /// 4. Urinalysis showing positive findings (leukocyte esterase, nitrite, bacteria)
    /// 5. Antibiotic prescription (Nitrofurantoin 100mg, twice daily for 7 days)
    /// 6. Optional: Follow-up visit after 7 days with condition resolution
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 35).</param>
    /// <param name="gender">Patient gender (default: "female").</param>
    /// <param name="includeFollowUp">Whether to include follow-up visit with resolution (default: true).</param>
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetUrinaryTractInfection(
        this IFhirSchemaProvider schemaProvider,
        int age = 35,
        string gender = "female",
        bool includeFollowUp = true)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Urinary Tract Infection")
            .WithDescription("Patient with acute UTI, urinalysis showing infection, antibiotic treatment, and resolution.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Initial visit with UTI symptoms
            .AddEncounter("UTI symptoms visit")
            .AddConditionOnset(FhirCode.Conditions.UrinaryTractInfection, severity: 2, assignToAttribute: "uti_condition")

            // Vital signs - elevated temperature
            .AddObservation(FhirCode.Observations.BodyTemperature, 38.5m, "Cel", "Cel")

            // Pain severity (dysuria/pelvic discomfort)
            .AddObservation(FhirCode.Observations.PainSeverity, 5m, "{score}", "{score}")

            // Urinalysis diagnostic report with positive findings
            .AddDiagnosticReport(
                DiagnosticReports.Urinalysis,
                observations:
                [
                    (LabObservations.LeukocyteEsterase, 1m, "positive"),
                    (LabObservations.Nitrite, 1m, "positive"),
                    (LabObservations.Bacteria, 1m, "present")
                ])

            // Antibiotic treatment: Nitrofurantoin 100mg twice daily for 7 days
            .AddMedicationOrder(new MedicationOrderState
            {
                Name = "Medication_Nitrofurantoin",
                Code = FhirCode.Medications.Nitrofurantoin100mg,
                IsChronic = false,
                Frequency = "twice-daily",
                DoseQuantity = 1,
                DoseUnit = "capsule",
                DurationDays = 7,
                ReasonConditionAttribute = "uti_condition"
            });

        // Optional follow-up visit with resolution
        if (includeFollowUp)
        {
            builder
                .DelayDays(7)
                .AddEncounter("UTI follow-up visit")
                .EndCondition("uti_condition", clinicalStatus: ConditionClinicalStatus.Resolved);
        }

        return builder.Build();
    }
}
