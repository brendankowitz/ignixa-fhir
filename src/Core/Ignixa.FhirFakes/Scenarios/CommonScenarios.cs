// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Provides reusable scenario fragments for common clinical patterns.
/// These sub-scenarios can be composed into larger scenarios using CallSubScenarioState.
/// </summary>
/// <remarks>
/// Pattern based on Synthea's "CallSubmodule" feature, which enables scenario composition.
/// Common fragments are extracted from frequently repeated patterns across clinical scenarios.
/// </remarks>
public static class CommonScenarios
{
    /// <summary>
    /// Records a standard set of vital signs measurements.
    /// Includes: Height, Weight, BMI, and Blood Pressure (systolic/diastolic).
    /// </summary>
    /// <returns>A function that configures a ScenarioBuilder with vital sign observations.</returns>
    /// <remarks>
    /// This fragment is reused across wellness visits, emergency encounters, and chronic disease management.
    /// Typical values:
    /// - Height: 150-190 cm
    /// - Weight: 50-100 kg
    /// - BMI: 18-30 kg/m²
    /// - Systolic BP: 110-130 mmHg
    /// - Diastolic BP: 70-85 mmHg
    /// </remarks>
    public static Func<ScenarioBuilder, ScenarioBuilder> RecordVitalSigns()
    {
        return builder => builder
            .AddObservation(VitalSigns.BodyHeight, minValue: 150m, maxValue: 190m, unit: "cm", unitCode: "cm")
            .AddObservation(VitalSigns.BodyWeight, minValue: 50m, maxValue: 100m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, minValue: 18m, maxValue: 30m, unit: "kg/m2", unitCode: "kg/m2")
            .AddObservation(VitalSigns.BloodPressureSystolic, minValue: 110m, maxValue: 130m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, minValue: 70m, maxValue: 85m, unit: "mmHg", unitCode: "mm[Hg]");
    }

    /// <summary>
    /// Records vital signs whose values are appropriate for the patient's age, using
    /// <see cref="GrowthReference"/> medians for height, weight, and BMI (so a toddler is not
    /// recorded at an adult height) and age-banded blood pressure.
    /// </summary>
    /// <param name="ageYears">The patient's age in years at the time of the visit.</param>
    /// <param name="sex">The patient's sex ("male"/"female"); unknown averages the two references.</param>
    /// <returns>A function that configures a ScenarioBuilder with age-appropriate observations.</returns>
    public static Func<ScenarioBuilder, ScenarioBuilder> RecordAgeAppropriateVitalSigns(int ageYears, string? sex = null)
    {
        var height = GrowthReference.MedianHeightCm(ageYears, sex);
        var weight = GrowthReference.MedianWeightKg(ageYears, sex);
        var bmi = GrowthReference.MedianBmi(ageYears, sex);

        // Children run lower blood pressures than adults; band roughly at age 13.
        var (sysLow, sysHigh, diaLow, diaHigh) = ageYears < 13
            ? (90m, 110m, 55m, 70m)
            : (110m, 130m, 70m, 85m);

        return builder => builder
            .AddObservation(VitalSigns.BodyHeight, minValue: height * 0.97m, maxValue: height * 1.03m, unit: "cm", unitCode: "cm")
            .AddObservation(VitalSigns.BodyWeight, minValue: weight * 0.90m, maxValue: weight * 1.10m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, minValue: bmi * 0.92m, maxValue: bmi * 1.08m, unit: "kg/m2", unitCode: "kg/m2")
            .AddObservation(VitalSigns.BloodPressureSystolic, minValue: sysLow, maxValue: sysHigh, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, minValue: diaLow, maxValue: diaHigh, unit: "mmHg", unitCode: "mm[Hg]");
    }

    /// <summary>
    /// Orders a Comprehensive Metabolic Panel (CMP) diagnostic report.
    /// This is a standard lab panel that includes electrolytes, kidney function, liver function, and glucose.
    /// </summary>
    /// <returns>A function that configures a ScenarioBuilder with a CMP diagnostic report.</returns>
    /// <remarks>
    /// The CMP is one of the most commonly ordered lab panels and includes measurements for:
    /// - Glucose, Calcium, Sodium, Potassium, CO2, Chloride
    /// - BUN (Blood Urea Nitrogen), Creatinine
    /// - Albumin, Total protein, Alkaline phosphatase, ALT, AST, Bilirubin
    ///
    /// This is frequently ordered during:
    /// - Annual wellness visits
    /// - Chronic disease monitoring (diabetes, kidney disease, liver disease)
    /// - Pre-operative assessments
    /// - Medication monitoring (e.g., metformin, statins)
    /// </remarks>
    public static Func<ScenarioBuilder, ScenarioBuilder> BasicMetabolicPanel()
    {
        return builder => builder.AddComprehensiveMetabolicPanel();
    }

    /// <summary>
    /// Records cardiovascular vital signs for cardiac monitoring.
    /// Includes: Heart Rate, Blood Pressure, and Oxygen Saturation.
    /// </summary>
    /// <returns>A function that configures a ScenarioBuilder with cardiovascular observations.</returns>
    /// <remarks>
    /// Used in:
    /// - Cardiac disease monitoring
    /// - Post-operative care
    /// - Emergency department assessments
    /// - Respiratory disease monitoring
    ///
    /// Typical values:
    /// - Heart Rate: 60-100 beats/min
    /// - Systolic BP: 110-130 mmHg
    /// - Diastolic BP: 70-85 mmHg
    /// - O2 Saturation: 95-100%
    /// </remarks>
    public static Func<ScenarioBuilder, ScenarioBuilder> CardiovascularVitals()
    {
        return builder => builder
            .AddObservation(VitalSigns.HeartRate, minValue: 60m, maxValue: 100m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.BloodPressureSystolic, minValue: 110m, maxValue: 130m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, minValue: 70m, maxValue: 85m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.OxygenSaturationPulseOx, minValue: 95m, maxValue: 100m, unit: "%", unitCode: "%");
    }

    /// <summary>
    /// Orders a lipid panel to assess cardiovascular risk.
    /// Includes: Total Cholesterol, LDL, HDL, and Triglycerides.
    /// </summary>
    /// <returns>A function that configures a ScenarioBuilder with a lipid panel diagnostic report.</returns>
    /// <remarks>
    /// Lipid panels are recommended:
    /// - Every 5 years for adults 20+ (screening)
    /// - Annually for patients with cardiovascular disease
    /// - Annually for patients on statin therapy
    /// - Before starting cholesterol-lowering medications
    ///
    /// Used to assess risk for:
    /// - Coronary artery disease
    /// - Stroke
    /// - Peripheral artery disease
    /// </remarks>
    public static Func<ScenarioBuilder, ScenarioBuilder> LipidPanel()
    {
        return builder => builder.AddLipidPanel();
    }

    /// <summary>
    /// Orders a Complete Blood Count (CBC) to assess overall health and detect infections, anemia, and blood disorders.
    /// </summary>
    /// <returns>A function that configures a ScenarioBuilder with a CBC diagnostic report.</returns>
    /// <remarks>
    /// CBC is one of the most common lab tests and includes:
    /// - White Blood Cell (WBC) count
    /// - Red Blood Cell (RBC) count
    /// - Hemoglobin and Hematocrit
    /// - Platelet count
    /// - Differential (neutrophils, lymphocytes, monocytes, eosinophils, basophils)
    ///
    /// Ordered for:
    /// - Routine health screening
    /// - Diagnosing infections
    /// - Monitoring anemia
    /// - Pre-operative assessments
    /// - Chemotherapy monitoring
    /// </remarks>
    public static Func<ScenarioBuilder, ScenarioBuilder> CompleteBloodCount()
    {
        return builder => builder.AddCompleteBloodCount();
    }

    /// <summary>
    /// Records temperature and respiratory vital signs for infection monitoring.
    /// Includes: Body Temperature, Respiratory Rate, Heart Rate, and Oxygen Saturation.
    /// </summary>
    /// <returns>A function that configures a ScenarioBuilder with infection monitoring observations.</returns>
    /// <remarks>
    /// Used to monitor for signs of infection or systemic illness:
    /// - Fever (elevated temperature)
    /// - Tachypnea (elevated respiratory rate)
    /// - Tachycardia (elevated heart rate)
    /// - Hypoxia (low oxygen saturation)
    ///
    /// Common in:
    /// - Suspected infections (pneumonia, UTI, sepsis)
    /// - Post-operative monitoring
    /// - Emergency department triage
    /// </remarks>
    public static Func<ScenarioBuilder, ScenarioBuilder> InfectionMonitoringVitals()
    {
        return builder => builder
            .AddObservation(VitalSigns.BodyTemperature, minValue: 36.5m, maxValue: 37.5m, unit: "°C", unitCode: "Cel")
            .AddObservation(VitalSigns.RespiratoryRate, minValue: 12m, maxValue: 20m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.HeartRate, minValue: 60m, maxValue: 100m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.OxygenSaturationPulseOx, minValue: 95m, maxValue: 100m, unit: "%", unitCode: "%");
    }
}
