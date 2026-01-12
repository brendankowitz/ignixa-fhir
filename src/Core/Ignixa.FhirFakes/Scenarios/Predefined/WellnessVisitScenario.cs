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
/// Provides extension methods for generating annual wellness visit scenarios.
/// </summary>
public static class WellnessVisitScenario
{
    /// <summary>
    /// Generates an annual wellness visit scenario with comprehensive vital signs and lab work.
    ///
    /// Timeline:
    /// 1. Patient presents for annual wellness visit
    /// 2. Vital signs recorded (height, weight, BMI, blood pressure, heart rate, respiratory rate, temperature)
    /// 3. Basic Metabolic Panel (BMP) lab work ordered and completed
    /// 4. Lipid Panel ordered for patients age 30+ (optional)
    ///
    /// Generated Resources:
    /// - 1 Encounter (ambulatory wellness visit)
    /// - 7 Vital Sign Observations (height, weight, BMI, BP systolic, BP diastolic, heart rate, respiratory rate, temperature)
    /// - 1 DiagnosticReport (BMP) with 8 lab observations
    /// - 1 DiagnosticReport (Lipid Panel) with 4 lab observations (if includeLipidPanel is true or age >= 30)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 45).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <param name="includeLipidPanel">Whether to include lipid panel (default: true, automatic for age >= 30).</param>
    /// <returns>A complete scenario context with wellness visit resources.</returns>
    public static ScenarioContext GetWellnessVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 45,
        string gender = "male",
        bool includeLipidPanel = true)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Annual Wellness Visit")
            .WithDescription("Annual wellness visit with comprehensive vital signs, Basic Metabolic Panel, and optional Lipid Panel for preventive health screening.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Wellness encounter
            .AddWellnessVisit("Annual wellness examination")

            // Vital signs - Height
            .AddObservation(ObservationState.BodyHeight())

            // Vital signs - Weight
            .AddObservation(ObservationState.BodyWeight())

            // Vital signs - BMI
            .AddObservation(ObservationState.BodyMassIndex())

            // Vital signs - Blood Pressure (panel with systolic and diastolic components)
            .AddObservation(ObservationState.BloodPressure())

            // Vital signs - Heart Rate
            .AddObservation(ObservationState.HeartRate())

            // Vital signs - Respiratory Rate
            .AddObservation(ObservationState.RespiratoryRate())

            // Vital signs - Body Temperature
            .AddObservation(ObservationState.BodyTemperature())

            // Basic Metabolic Panel (8 tests)
            .AddDiagnosticReport(DiagnosticReportState.BasicMetabolicPanel());

        // Conditional lipid panel (automatic for age >= 30 or if explicitly requested)
        if (includeLipidPanel || age >= 30)
        {
            builder.AddLipidPanel();
        }

        return builder.Build();
    }
}
