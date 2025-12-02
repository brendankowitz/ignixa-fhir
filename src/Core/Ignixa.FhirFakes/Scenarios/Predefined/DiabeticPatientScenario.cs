// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating Type 2 Diabetes scenarios.
/// </summary>
public static class DiabeticPatientScenario
{
    /// <summary>
    /// Generates a Type 2 Diabetes scenario with medication escalation.
    ///
    /// Timeline:
    /// 1. Patient presents with symptoms
    /// 2. Initial diagnosis with blood glucose and A1C tests
    /// 3. Metformin 500mg prescribed
    /// 4. 3-month follow-up with improved A1C
    /// 5. 6-month follow-up - if still elevated, escalate to Metformin 1000mg
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 52).</param>
    /// <param name="gender">Patient gender (default: random).</param>
    /// <param name="severity">Initial diabetes severity 1-5 (default: 2).</param>
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetDiabeticPatient(
        this IFhirSchemaProvider schemaProvider,
        int age = 52,
        string? gender = null,
        int severity = 2)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Type 2 Diabetes with Medication Escalation")
            .WithDescription("Patient diagnosed with Type 2 Diabetes, receives initial treatment with Metformin, monitored with A1C tests, medication escalated as needed.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Initial visit - symptoms and diagnosis
            .AddEncounter("Routine checkup - fatigue and increased thirst")
            .AddObservation(ObservationState.BloodGlucose())
            .AddConditionOnset(FhirCode.Conditions.DiabetesType2, severity: severity, assignToAttribute: "diabetes_condition")
            .AddObservation(ObservationState.HemoglobinA1c())

            // Initial medication
            .AddMedicationOrder(MedicationOrderState.Metformin500mg())

            // 3-month follow-up
            .DelayMonths(3)
            .AddEncounter("Diabetes follow-up - 3 month")
            .AddObservation(ObservationState.BloodGlucose())
            .AddObservation(ObservationState.HemoglobinA1c())

            // Disease progression check - if severity >= 2, escalate medication
            .AddState(new ConditionalMedicationEscalationState
            {
                Name = "Check_Medication_Escalation",
                SeverityAttribute = "diabetes_condition_severity",
                ThresholdSeverity = 2,
                EscalateMedication = MedicationOrderState.Metformin1000mg()
            })

            // 6-month follow-up
            .DelayMonths(3)
            .AddEncounter("Diabetes follow-up - 6 month")
            .AddObservation(ObservationState.BloodGlucose())
            .AddObservation(ObservationState.HemoglobinA1c())

            .Build();
    }
}
