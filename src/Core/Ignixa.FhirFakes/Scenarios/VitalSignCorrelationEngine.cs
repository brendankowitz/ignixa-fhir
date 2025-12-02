// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Provides physiologically realistic correlations between vital signs, lab values, and patient conditions.
/// Implements evidence-based adjustments based on BMI, chronic conditions, and other health factors.
/// </summary>
/// <remarks>
/// This engine ensures that generated test data reflects real-world physiological relationships:
/// - Higher BMI correlates with elevated blood pressure and cholesterol
/// - Diabetes affects glucose levels and A1C
/// - Conditions interact (e.g., diabetes increases hypertension risk)
///
/// Based on clinical evidence:
/// - BMI and hypertension: For every 10 kg increase in body weight, systolic BP increases by 3-5 mmHg (Framingham Heart Study)
/// - Diabetes ranges: Fasting glucose 140-200 mg/dL, A1C 6.5-9.0% (ADA guidelines)
/// - BMI and cholesterol: Obesity increases LDL by 20-40 mg/dL on average
/// </remarks>
public sealed class VitalSignCorrelationEngine
{
    /// <summary>
    /// Adjusts blood pressure based on BMI and other risk factors.
    /// Higher BMI increases blood pressure due to increased cardiac workload and vascular resistance.
    /// </summary>
    /// <param name="baseBP">The baseline blood pressure value (in mmHg).</param>
    /// <param name="context">The scenario context containing patient attributes like BMI.</param>
    /// <returns>Adjusted blood pressure value reflecting BMI impact.</returns>
    /// <remarks>
    /// Adjustment ranges based on BMI categories:
    /// - BMI 40+ (Class III Obesity): +15-25 mmHg
    /// - BMI 35-40 (Class II Obesity): +10-15 mmHg
    /// - BMI 30-35 (Class I Obesity): +5-10 mmHg
    /// - BMI below 30: No adjustment
    ///
    /// These ranges reflect epidemiological data showing strong correlation between
    /// obesity and hypertension risk (relative risk 2.0-2.5 for obese individuals).
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public decimal AdjustBloodPressure(decimal baseBP, ScenarioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bmi = context.GetAttribute<decimal>("bmi", 0m);

        // Apply BMI-based adjustment
        var adjustment = bmi switch
        {
            >= 40m => Random.Shared.Next(15, 26),  // Class III Obesity
            >= 35m => Random.Shared.Next(10, 16),  // Class II Obesity
            >= 30m => Random.Shared.Next(5, 11),   // Class I Obesity
            _ => 0                                  // Normal/Overweight (no adjustment)
        };

        return baseBP + adjustment;
    }

    /// <summary>
    /// Adjusts blood glucose levels based on diabetes status.
    /// Diabetic patients have consistently elevated fasting glucose levels.
    /// </summary>
    /// <param name="baseGlucose">The baseline glucose value (in mg/dL).</param>
    /// <param name="context">The scenario context containing condition history.</param>
    /// <returns>Adjusted glucose value reflecting diabetes status.</returns>
    /// <remarks>
    /// Glucose ranges:
    /// - Normal fasting: 70-100 mg/dL (uses baseGlucose)
    /// - Prediabetes: 100-125 mg/dL
    /// - Diabetes: 140-200 mg/dL (ADA diagnostic criteria: fasting glucose >= 126 mg/dL)
    ///
    /// This method checks for diabetes type 2 (SNOMED code 44054006) in the patient's
    /// condition history and adjusts glucose to diabetic range if present.
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public decimal AdjustGlucose(decimal baseGlucose, ScenarioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Check if patient has diabetes (SNOMED CT code for Type 2 Diabetes)
        var hasDiabetes = context.Conditions.Any(c =>
        {
            // Extract SNOMED code from condition resource
            var codeElement = c.MutableNode["code"]?["coding"]?[0]?["code"];
            return codeElement?.GetValue<string>() == "44054006";
        });

        return hasDiabetes
            ? Random.Shared.Next(140, 201)  // Diabetic range (140-200 mg/dL)
            : baseGlucose;                   // Normal range (use base value)
    }

    /// <summary>
    /// Adjusts cholesterol levels based on BMI.
    /// Higher BMI correlates with elevated LDL cholesterol and total cholesterol.
    /// </summary>
    /// <param name="baseCholesterol">The baseline total cholesterol value (in mg/dL).</param>
    /// <param name="context">The scenario context containing patient attributes like BMI.</param>
    /// <returns>Adjusted cholesterol value reflecting BMI impact.</returns>
    /// <remarks>
    /// Adjustment ranges based on BMI categories:
    /// - BMI 40+ (Class III Obesity): +30-50 mg/dL
    /// - BMI 35-40 (Class II Obesity): +20-40 mg/dL
    /// - BMI 30-35 (Class I Obesity): +10-25 mg/dL
    /// - BMI below 30: No adjustment
    ///
    /// Normal total cholesterol: below 200 mg/dL (desirable)
    /// Borderline high: 200-239 mg/dL
    /// High: 240+ mg/dL
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public decimal AdjustCholesterol(decimal baseCholesterol, ScenarioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bmi = context.GetAttribute<decimal>("bmi", 0m);

        var adjustment = bmi switch
        {
            >= 40m => Random.Shared.Next(30, 51),  // Class III Obesity
            >= 35m => Random.Shared.Next(20, 41),  // Class II Obesity
            >= 30m => Random.Shared.Next(10, 26),  // Class I Obesity
            _ => 0                                  // Normal/Overweight
        };

        return baseCholesterol + adjustment;
    }

    /// <summary>
    /// Adjusts hemoglobin A1C based on diabetes severity.
    /// A1C reflects average blood glucose over the past 2-3 months.
    /// </summary>
    /// <param name="context">The scenario context containing diabetes severity attribute.</param>
    /// <param name="severityAttribute">The attribute name containing severity level (1-5).</param>
    /// <returns>A1C percentage value reflecting diabetes severity.</returns>
    /// <remarks>
    /// A1C ranges:
    /// - Normal: below 5.7%
    /// - Prediabetes: 5.7-6.4%
    /// - Diabetes (well-controlled): 6.5-7.0%
    /// - Diabetes (moderate control): 7.0-8.5%
    /// - Diabetes (poor control): 8.5%+
    ///
    /// Severity levels:
    /// - Severity 1 (controlled): 7.0-7.5%
    /// - Severity 2 (moderate): 7.5-8.5%
    /// - Severity 3 (poor): 8.5-10.0%
    /// - Severity 4 (very poor): 10.0-11.5%
    /// - Severity 5+ (uncontrolled): 11.5%+
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public decimal AdjustHemoglobinA1c(ScenarioContext context, string severityAttribute = "diabetes_condition_severity")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(severityAttribute);

        var severity = context.GetAttribute<int>(severityAttribute, 1);

        return severity switch
        {
            1 => 7.0m + (decimal)(Random.Shared.NextDouble() * 0.5),     // 7.0-7.5%
            2 => 7.5m + (decimal)(Random.Shared.NextDouble() * 1.0),     // 7.5-8.5%
            3 => 8.5m + (decimal)(Random.Shared.NextDouble() * 1.5),     // 8.5-10.0%
            4 => 10.0m + (decimal)(Random.Shared.NextDouble() * 1.5),    // 10.0-11.5%
            _ => 11.5m + (decimal)(Random.Shared.NextDouble() * 2.0)     // 11.5%+
        };
    }

    /// <summary>
    /// Calculates BMI from height and weight and stores it in the context.
    /// BMI = weight (kg) / height (m)^2
    /// </summary>
    /// <param name="context">The scenario context to store BMI.</param>
    /// <param name="heightCm">Height in centimeters.</param>
    /// <param name="weightKg">Weight in kilograms.</param>
    /// <remarks>
    /// BMI categories:
    /// - Underweight: below 18.5
    /// - Normal: 18.5-24.9
    /// - Overweight: 25.0-29.9
    /// - Obese Class I: 30.0-34.9
    /// - Obese Class II: 35.0-39.9
    /// - Obese Class III: 40.0+
    /// </remarks>
    public void CalculateAndStoreBMI(ScenarioContext context, decimal heightCm, decimal weightKg)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (heightCm <= 0)
        {
            throw new ArgumentException("Height must be positive", nameof(heightCm));
        }

        if (weightKg <= 0)
        {
            throw new ArgumentException("Weight must be positive", nameof(weightKg));
        }

        var heightM = heightCm / 100m;
        var bmi = Math.Round(weightKg / (heightM * heightM), 1);

        context.SetAttribute("bmi", bmi);
        context.SetAttribute("height_cm", heightCm);
        context.SetAttribute("weight_kg", weightKg);
    }

    /// <summary>
    /// Checks if a condition code exists in the patient's condition history.
    /// </summary>
    /// <param name="context">The scenario context containing conditions.</param>
    /// <param name="snomedCode">The SNOMED CT code to search for.</param>
    /// <returns>True if the condition is present, false otherwise.</returns>
    public bool HasCondition(ScenarioContext context, string snomedCode)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snomedCode);

        return context.Conditions.Any(c =>
        {
            var codeElement = c.MutableNode["code"]?["coding"]?[0]?["code"];
            return codeElement?.GetValue<string>() == snomedCode;
        });
    }
}
