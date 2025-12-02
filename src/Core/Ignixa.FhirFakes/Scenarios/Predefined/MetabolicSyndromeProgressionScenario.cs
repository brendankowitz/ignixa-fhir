// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating metabolic syndrome progression scenarios.
/// Demonstrates Layer 2 features: vital sign correlations, probabilistic disease cascades, and reusable fragments.
/// </summary>
/// <remarks>
/// Metabolic syndrome is a cluster of conditions that occur together, increasing risk of:
/// - Heart disease
/// - Stroke
/// - Type 2 diabetes
///
/// Progression pathway (evidence-based):
/// 1. Obesity (BMI 30+) - baseline condition
/// 2. Hypertension develops (65% probability in obese individuals - Framingham Heart Study)
/// 3. Type 2 diabetes develops (40% probability with obesity + hypertension - ARIC Study)
/// 4. Hyperlipidemia develops (58% probability in metabolic syndrome - NCEP ATP III)
///
/// This scenario demonstrates:
/// - **VitalSignCorrelationEngine**: BMI-adjusted blood pressure and cholesterol
/// - **Probabilistic disease cascade**: Obesity → Hypertension → Diabetes → Hyperlipidemia
/// - **Reusable fragments**: CommonScenarios for vital signs and lab panels
/// </remarks>
public static class MetabolicSyndromeProgressionScenario
{
    /// <summary>
    /// Generates a metabolic syndrome progression scenario demonstrating the obesity → hypertension → diabetes cascade.
    ///
    /// Demonstrates:
    /// - **Vital sign correlations**: VitalSignCorrelationEngine adjusts BP based on BMI
    /// - **Probabilistic branching**: 65% chance of hypertension in obese patients
    /// - **Disease cascade**: Hypertension increases diabetes risk to 40%
    /// - **Reusable fragments**: CommonScenarios.RecordVitalSigns(), BasicMetabolicPanel()
    ///
    /// Timeline:
    /// Year 0: Initial wellness visit - patient is obese (BMI 35)
    /// Year 1: Blood pressure elevated (probabilistic: 65% chance) - BMI-correlated values
    /// Year 2: Type 2 diabetes develops (probabilistic: 40% chance if hypertensive)
    /// Year 3: Hyperlipidemia develops (probabilistic: 58% chance with metabolic syndrome)
    /// Year 4: Follow-up with medication adjustments
    ///
    /// Generated Resources:
    /// - 5 Encounters (annual wellness visits + follow-ups)
    /// - 20+ Observations (vital signs with BMI-correlated blood pressure)
    /// - 1-4 Conditions (obesity, hypertension, diabetes, hyperlipidemia - probabilistic)
    /// - 1-3 MedicationRequests (antihypertensive, metformin, statin - as needed)
    /// - 5 DiagnosticReports (metabolic panels, lipid panels, A1C tests)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 48).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <param name="startingBMI">Starting BMI value (default: 35.0 - Class II Obesity).</param>
    /// <returns>A complete scenario context with metabolic syndrome progression.</returns>
    public static ScenarioContext GetMetabolicSyndromeProgression(
        this IFhirSchemaProvider schemaProvider,
        int age = 48,
        string gender = "male",
        decimal startingBMI = 35.0m)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var correlationEngine = new VitalSignCorrelationEngine();

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Metabolic Syndrome Progression with Vital Sign Correlations")
            .WithDescription("Multi-year patient journey demonstrating obesity → hypertension → diabetes cascade with BMI-correlated vital signs and probabilistic disease progression.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // === YEAR 0: Initial Visit - Obesity Diagnosis ===
            .AddWellnessVisit("Annual wellness visit")

            // Record height (fixed for BMI calculation)
            .AddObservation(VitalSigns.BodyHeight, value: 175m, unit: "cm", unitCode: "cm")

            // Calculate weight from desired BMI: weight = BMI * (height_m)^2
            // For BMI 35 and height 175cm (1.75m): weight = 35 * (1.75)^2 = 107.2 kg
            .AddObservation(VitalSigns.BodyWeight, value: 107.2m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, value: startingBMI, unit: "kg/m2", unitCode: "kg/m2")

            // Store BMI in context for correlation engine
            .SetAttribute("bmi", startingBMI)

            // VITAL SIGN CORRELATION: Adjust blood pressure based on BMI
            // BMI 35 (Class II Obesity) increases BP by 10-15 mmHg
            .AddState(new CorrelatedBloodPressureState
            {
                Name = "BMI_Correlated_BP_Year0",
                BaselineSystolic = 120m,
                BaselineDiastolic = 80m
            })

            // Basic labs
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Baseline Metabolic Panel")
            .AddSubScenario(CommonScenarios.LipidPanel(), "Baseline Lipid Panel")

            // Diagnose obesity
            .AddConditionOnset(FhirCode.Conditions.Obesity, severity: 2, assignToAttribute: "obesity_condition")

            // === YEAR 1: Hypertension Development (65% probability) ===
            .DelayMonths(12)
            .AddWellnessVisit("Annual follow-up - weight management")

            // Vital signs with BMI correlation (still obese)
            .AddObservation(VitalSigns.BodyWeight, value: 109m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, value: 35.6m, unit: "kg/m2", unitCode: "kg/m2")
            .SetAttribute("bmi", 35.6m)

            // PROBABILISTIC BRANCH: 65% develop hypertension (evidence-based)
            .AddProbabilisticBranch(
                0.65,  // 65% probability in obese individuals (Framingham Heart Study)

                // TRUE PATH: Hypertension develops
                new CompositeState
                {
                    Name = "Hypertension_Development",
                    States =
                    [
                        new CorrelatedBloodPressureState
                        {
                            Name = "Elevated_BP_With_BMI",
                            BaselineSystolic = 135m,  // Already elevated
                            BaselineDiastolic = 88m
                        },
                        new ConditionOnsetState
                        {
                            Name = "Hypertension_Diagnosis",
                            Code = FhirCode.Conditions.HypertensionEssential,
                            Severity = 2,
                            AssignToAttribute = "hypertension_condition"
                        },
                        MedicationOrderState.Lisinopril10mg()  // Start ACE inhibitor
                    ]
                },

                // FALSE PATH: Blood pressure remains elevated but not hypertensive yet
                new CorrelatedBloodPressureState
                {
                    Name = "Prehypertension_BP",
                    BaselineSystolic = 128m,
                    BaselineDiastolic = 82m
                }
            )

            // === YEAR 2: Type 2 Diabetes Development (40% probability if hypertensive) ===
            .DelayMonths(12)
            .AddEncounter("Routine follow-up")

            // Vital signs (BMI increasing)
            .AddObservation(VitalSigns.BodyWeight, value: 112m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, value: 36.6m, unit: "kg/m2", unitCode: "kg/m2")
            .SetAttribute("bmi", 36.6m)

            // BP with BMI correlation
            .AddState(new CorrelatedBloodPressureState
            {
                Name = "BMI_Correlated_BP_Year2",
                BaselineSystolic = 132m,
                BaselineDiastolic = 86m
            })

            // A1C screening
            .AddObservation(ObservationState.HemoglobinA1c())

            // PROBABILISTIC BRANCH: 40% develop diabetes (ARIC Study - obesity + hypertension)
            .AddProbabilisticBranch(
                0.40,  // 40% probability with obesity + hypertension

                // TRUE PATH: Type 2 Diabetes develops
                new CompositeState
                {
                    Name = "Diabetes_Development",
                    States =
                    [
                        new ConditionOnsetState
                        {
                            Name = "Diabetes_Diagnosis",
                            Code = FhirCode.Conditions.DiabetesType2,
                            Severity = 2,
                            AssignToAttribute = "diabetes_condition"
                        },
                        // VITAL SIGN CORRELATION: Elevated glucose due to diabetes
                        new CorrelatedBloodGlucoseState
                        {
                            Name = "Diabetic_Glucose_Range",
                            BaselineGlucose = 95m  // Will be adjusted to 140-200 mg/dL
                        },
                        MedicationOrderState.Metformin500mg()  // Start metformin
                    ]
                },

                // FALSE PATH: Prediabetes only
                new ConditionOnsetState
                {
                    Name = "Prediabetes_Only",
                    Code = FhirCode.Conditions.Prediabetes,
                    Severity = 1,
                    AssignToAttribute = "prediabetes_condition"
                }
            )

            // === YEAR 3: Hyperlipidemia Development (58% probability in metabolic syndrome) ===
            .DelayMonths(12)
            .AddEncounter("Annual comprehensive metabolic screening")

            // Vital signs
            .AddObservation(VitalSigns.BodyWeight, value: 113m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, value: 36.9m, unit: "kg/m2", unitCode: "kg/m2")
            .SetAttribute("bmi", 36.9m)

            // BP monitoring
            .AddState(new CorrelatedBloodPressureState
            {
                Name = "BMI_Correlated_BP_Year3",
                BaselineSystolic = 138m,
                BaselineDiastolic = 88m
            })

            // Comprehensive labs
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Annual Metabolic Panel")
            .AddSubScenario(CommonScenarios.LipidPanel(), "Annual Lipid Panel")
            .AddObservation(ObservationState.HemoglobinA1c())

            // PROBABILISTIC BRANCH: 58% develop hyperlipidemia (NCEP ATP III)
            .AddProbabilisticBranch(
                0.58,  // 58% probability in metabolic syndrome

                // TRUE PATH: Hyperlipidemia develops
                new CompositeState
                {
                    Name = "Hyperlipidemia_Development",
                    States =
                    [
                        new ConditionOnsetState
                        {
                            Name = "Hyperlipidemia_Diagnosis",
                            Code = FhirCode.Conditions.Hyperlipidemia,
                            Severity = 2,
                            AssignToAttribute = "hyperlipidemia_condition"
                        },
                        // VITAL SIGN CORRELATION: Elevated cholesterol due to BMI
                        new CorrelatedCholesterolState
                        {
                            Name = "BMI_Elevated_Cholesterol",
                            BaselineTotalCholesterol = 180m  // Will be adjusted to 190-205 mg/dL
                        },
                        MedicationOrderState.Atorvastatin20mg()  // Start statin
                    ]
                },

                // FALSE PATH: Borderline cholesterol, no medication yet
                new DelayState { Name = "Borderline_Lipids", Exact = TimeSpan.Zero }
            )

            // === YEAR 4: Follow-up with Full Metabolic Syndrome ===
            .DelayMonths(12)
            .AddEncounter("Metabolic syndrome management follow-up")

            // Vital signs
            .AddObservation(VitalSigns.BodyWeight, value: 111m, unit: "kg", unitCode: "kg")  // Slight weight loss
            .AddObservation(VitalSigns.BMI, value: 36.2m, unit: "kg/m2", unitCode: "kg/m2")
            .SetAttribute("bmi", 36.2m)

            // BP monitoring (improved with treatment)
            .AddState(new CorrelatedBloodPressureState
            {
                Name = "Treated_BP_Year4",
                BaselineSystolic = 128m,  // Improved with medication
                BaselineDiastolic = 82m
            })

            // Comprehensive follow-up labs
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Follow-up Metabolic Panel")
            .AddSubScenario(CommonScenarios.LipidPanel(), "Follow-up Lipid Panel")
            .AddObservation(ObservationState.HemoglobinA1c());

        return builder.Build();
    }

    /// <summary>
    /// Generates a simplified metabolic syndrome scenario demonstrating BMI-correlated vital signs only.
    /// Useful for testing VitalSignCorrelationEngine without complex probabilistic branching.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="bmiCategory">BMI category: "normal" (22), "overweight" (28), "obese1" (32), "obese2" (37), "obese3" (42).</param>
    /// <returns>A scenario context demonstrating BMI-correlated blood pressure and cholesterol.</returns>
    public static ScenarioContext GetBMICorrelationDemo(
        this IFhirSchemaProvider schemaProvider,
        string bmiCategory = "obese2")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var (bmi, weight, description) = bmiCategory.ToUpperInvariant() switch
        {
            "NORMAL" => (22.0m, 67.4m, "Normal BMI (22.0) - baseline blood pressure"),
            "OVERWEIGHT" => (28.0m, 85.8m, "Overweight BMI (28.0) - minimal BP adjustment"),
            "OBESE1" => (32.0m, 98.0m, "Class I Obesity BMI (32.0) - moderate BP elevation (+5-10 mmHg)"),
            "OBESE2" => (37.0m, 113.3m, "Class II Obesity BMI (37.0) - significant BP elevation (+10-15 mmHg)"),
            "OBESE3" => (42.0m, 128.6m, "Class III Obesity BMI (42.0) - severe BP elevation (+15-25 mmHg)"),
            _ => throw new ArgumentException($"Invalid BMI category: {bmiCategory}. Use: normal, overweight, obese1, obese2, obese3.")
        };

        return new ScenarioBuilder(schemaProvider)
            .WithName($"BMI Correlation Demonstration - {bmiCategory.ToUpperInvariant()}")
            .WithDescription($"Demonstrates VitalSignCorrelationEngine adjusting blood pressure and cholesterol based on BMI. {description}")

            .WithPatient(age: 50, gender: "male")
            .AddEncounter("Wellness visit for BMI correlation demo")

            // Height fixed at 175cm for consistent BMI calculation
            .AddObservation(VitalSigns.BodyHeight, value: 175m, unit: "cm", unitCode: "cm")
            .AddObservation(VitalSigns.BodyWeight, value: weight, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, value: bmi, unit: "kg/m2", unitCode: "kg/m2")

            // Store BMI for correlation engine
            .SetAttribute("bmi", bmi)

            // VITAL SIGN CORRELATION: Blood pressure adjusted by BMI
            .AddState(new CorrelatedBloodPressureState
            {
                Name = "BMI_Correlated_BP",
                BaselineSystolic = 120m,  // Normal baseline
                BaselineDiastolic = 80m
            })

            // VITAL SIGN CORRELATION: Cholesterol adjusted by BMI
            .AddState(new CorrelatedCholesterolState
            {
                Name = "BMI_Correlated_Cholesterol",
                BaselineTotalCholesterol = 180m  // Normal baseline
            })

            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Metabolic Panel")
            .AddSubScenario(CommonScenarios.LipidPanel(), "Lipid Panel")

            .Build();
    }
}

/// <summary>
/// Custom state that generates BMI-correlated blood pressure observations.
/// Demonstrates integration of VitalSignCorrelationEngine with state machine pattern.
/// </summary>
internal sealed class CorrelatedBloodPressureState : ScenarioState
{
    /// <summary>
    /// Gets or sets the baseline systolic pressure before BMI adjustment.
    /// </summary>
    public required decimal BaselineSystolic { get; init; }

    /// <summary>
    /// Gets or sets the baseline diastolic pressure before BMI adjustment.
    /// </summary>
    public required decimal BaselineDiastolic { get; init; }

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        var correlationEngine = new VitalSignCorrelationEngine();

        // Adjust blood pressure based on BMI stored in context
        var adjustedSystolic = correlationEngine.AdjustBloodPressure(BaselineSystolic, context);
        var adjustedDiastolic = correlationEngine.AdjustBloodPressure(BaselineDiastolic, context);

        // Create blood pressure observation with adjusted values
        var bpState = ObservationState.BloodPressure(
            systolic: adjustedSystolic,
            diastolic: adjustedDiastolic);

        bpState.Execute(context, faker);
    }
}

/// <summary>
/// Custom state that generates diabetes-correlated blood glucose observations.
/// Demonstrates integration of VitalSignCorrelationEngine for glucose adjustment.
/// </summary>
internal sealed class CorrelatedBloodGlucoseState : ScenarioState
{
    /// <summary>
    /// Gets or sets the baseline glucose before diabetes adjustment.
    /// </summary>
    public required decimal BaselineGlucose { get; init; }

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        var correlationEngine = new VitalSignCorrelationEngine();

        // Adjust glucose based on diabetes status in context
        var adjustedGlucose = correlationEngine.AdjustGlucose(BaselineGlucose, context);

        // Create glucose observation with adjusted value
        var glucoseState = ObservationState.BloodGlucose(value: adjustedGlucose);
        glucoseState.Execute(context, faker);
    }
}

/// <summary>
/// Custom state that generates BMI-correlated cholesterol observations.
/// Demonstrates integration of VitalSignCorrelationEngine for cholesterol adjustment.
/// </summary>
internal sealed class CorrelatedCholesterolState : ScenarioState
{
    /// <summary>
    /// Gets or sets the baseline total cholesterol before BMI adjustment.
    /// </summary>
    public required decimal BaselineTotalCholesterol { get; init; }

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        var correlationEngine = new VitalSignCorrelationEngine();

        // Adjust cholesterol based on BMI stored in context
        var adjustedCholesterol = correlationEngine.AdjustCholesterol(BaselineTotalCholesterol, context);

        // Store adjusted value in context for reference
        context.SetAttribute("adjusted_cholesterol", adjustedCholesterol);

        // Note: Actual DiagnosticReport with cholesterol would be created separately
        // This is a simplified demo showing the correlation calculation
    }
}
