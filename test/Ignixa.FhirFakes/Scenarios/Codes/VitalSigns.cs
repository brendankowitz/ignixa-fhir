// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Common vital sign observation codes (LOINC).
/// Vital signs are measurements of the body's basic functions.
/// Units: Temperature (°C or °F), Heart Rate (beats/min), Respiratory Rate (breaths/min),
/// Blood Pressure (mmHg), Oxygen Saturation (%), Height (cm or in), Weight (kg or lb), BMI (kg/m²).
/// </summary>
public static class VitalSigns
{
    /// <summary>Body temperature - Typical unit: °C or °F</summary>
    public static FhirCode BodyTemperature { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "8310-5",
        Display: "Body temperature");

    /// <summary>Heart rate - Typical unit: beats/min</summary>
    public static FhirCode HeartRate { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "8867-4",
        Display: "Heart rate");

    /// <summary>Respiratory rate - Typical unit: breaths/min</summary>
    public static FhirCode RespiratoryRate { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "9279-1",
        Display: "Respiratory rate");

    /// <summary>Blood pressure panel - Container for systolic and diastolic readings</summary>
    public static FhirCode BloodPressurePanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "85354-9",
        Display: "Blood pressure panel");

    /// <summary>Systolic blood pressure - Typical unit: mmHg</summary>
    public static FhirCode BloodPressureSystolic { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "8480-6",
        Display: "Systolic blood pressure");

    /// <summary>Diastolic blood pressure - Typical unit: mmHg</summary>
    public static FhirCode BloodPressureDiastolic { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "8462-4",
        Display: "Diastolic blood pressure");

    /// <summary>Oxygen saturation in arterial blood - Typical unit: %</summary>
    public static FhirCode OxygenSaturation { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2708-6",
        Display: "Oxygen saturation in Arterial blood");

    /// <summary>Oxygen saturation in arterial blood by pulse oximetry - Typical unit: %</summary>
    public static FhirCode OxygenSaturationPulseOx { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "59408-5",
        Display: "Oxygen saturation in Arterial blood by Pulse oximetry");

    /// <summary>Body height - Typical unit: cm or in</summary>
    public static FhirCode BodyHeight { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "8302-2",
        Display: "Body height");

    /// <summary>Body weight - Typical unit: kg or lb</summary>
    public static FhirCode BodyWeight { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "29463-7",
        Display: "Body weight");

    /// <summary>Body mass index (BMI) - Typical unit: kg/m²</summary>
    public static FhirCode BMI { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "39156-5",
        Display: "Body mass index (BMI) [Ratio]");

    /// <summary>Head circumference - Typical unit: cm</summary>
    public static FhirCode HeadCircumference { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "9843-4",
        Display: "Head Occipital-frontal circumference");

    /// <summary>Body surface area - Typical unit: m²</summary>
    public static FhirCode BodySurfaceArea { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "8277-6",
        Display: "Body surface area");

    /// <summary>Pain severity (0-10 scale) - Typical unit: {score}</summary>
    public static FhirCode PainSeverity { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "72514-3",
        Display: "Pain severity - 0-10 verbal numeric rating [Score] - Reported");
}
