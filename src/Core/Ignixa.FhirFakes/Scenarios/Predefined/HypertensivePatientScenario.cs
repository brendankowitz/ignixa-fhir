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
/// Provides extension methods for generating Hypertension scenarios.
/// </summary>
public static class HypertensivePatientScenario
{
    /// <summary>
    /// Generates a Hypertension scenario with blood pressure monitoring.
    ///
    /// Timeline:
    /// 1. Patient presents with high blood pressure
    /// 2. Initial diagnosis and Lisinopril prescribed
    /// 3. Monthly follow-ups with BP checks
    /// 4. If uncontrolled, add Amlodipine
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 58).</param>
    /// <param name="gender">Patient gender (default: random).</param>
    /// <param name="severity">Initial hypertension severity 1-4 (default: 2).</param>
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetHypertensivePatient(
        this IFhirSchemaProvider schemaProvider,
        int age = 58,
        string? gender = null,
        int severity = 2)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Hypertension with Blood Pressure Monitoring")
            .WithDescription("Patient diagnosed with essential hypertension, receives ACE inhibitor, blood pressure monitored monthly.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Initial visit - elevated BP discovered
            .AddEncounter("Routine checkup - headaches")
            .AddObservation(ObservationState.BloodPressure(
                systolicSeverityAttr: "hypertension_condition_severity",
                diastolicSeverityAttr: "hypertension_condition_severity"))
            .AddConditionOnset(FhirCode.Conditions.Hypertension, severity: severity, assignToAttribute: "hypertension_condition")

            // Initial medication
            .AddMedicationOrder(MedicationOrderState.Lisinopril10mg())

            // 1-month follow-up
            .DelayMonths(1)
            .AddEncounter("Hypertension follow-up - 1 month")
            .AddObservation(ObservationState.BloodPressure(
                systolicSeverityAttr: "hypertension_condition_severity",
                diastolicSeverityAttr: "hypertension_condition_severity"))

            // 2-month follow-up
            .DelayMonths(1)
            .AddEncounter("Hypertension follow-up - 2 month")
            .AddObservation(ObservationState.BloodPressure(
                systolicSeverityAttr: "hypertension_condition_severity",
                diastolicSeverityAttr: "hypertension_condition_severity"))

            // Check if BP still elevated - add second medication
            .AddState(new ConditionalMedicationEscalationState
            {
                Name = "Add_Second_Antihypertensive",
                SeverityAttribute = "hypertension_condition_severity",
                ThresholdSeverity = 2,
                EscalateMedication = MedicationOrderState.Amlodipine5mg()
            })

            // 3-month follow-up
            .DelayMonths(1)
            .AddEncounter("Hypertension follow-up - 3 month")
            .AddObservation(ObservationState.BloodPressure(
                systolicSeverityAttr: "hypertension_condition_severity",
                diastolicSeverityAttr: "hypertension_condition_severity"))

            .Build();
    }
}
