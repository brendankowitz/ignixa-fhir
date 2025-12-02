// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating enhanced wellness visit scenarios with probabilistic screening outcomes.
/// Demonstrates Layer 2 features: probabilistic branching, reusable fragments, and vital sign correlations.
/// </summary>
public static class EnhancedWellnessVisitScenario
{
    /// <summary>
    /// Generates an enhanced annual wellness visit scenario with probabilistic screening outcomes.
    ///
    /// Demonstrates:
    /// - **Reusable fragments**: CommonScenarios.RecordVitalSigns(), BasicMetabolicPanel(), LipidPanel()
    /// - **Probabilistic branching**: 15% chance of abnormal lipid panel requiring statin therapy
    /// - **Vital sign correlations**: BMI-adjusted blood pressure using VitalSignCorrelationEngine
    ///
    /// Timeline:
    /// 1. Patient presents for annual wellness visit
    /// 2. Vital signs recorded using reusable CommonScenarios.RecordVitalSigns() fragment
    /// 3. Basic Metabolic Panel ordered using CommonScenarios.BasicMetabolicPanel() fragment
    /// 4. Lipid Panel ordered using CommonScenarios.LipidPanel() fragment
    /// 5. Probabilistic branch: 15% chance of elevated cholesterol requiring statin prescription
    /// 6. 3-month follow-up if statin prescribed
    ///
    /// Generated Resources:
    /// - 1 Encounter (ambulatory wellness visit)
    /// - 5 Vital Sign Observations (height, weight, BMI, BP systolic, BP diastolic) - from CommonScenarios
    /// - 1 DiagnosticReport (BMP) with 8 lab observations - from CommonScenarios
    /// - 1 DiagnosticReport (Lipid Panel) with 4 lab observations - from CommonScenarios
    /// - 1 MedicationRequest (Atorvastatin 20mg) if cholesterol elevated (15% probability)
    /// - 1 Follow-up Encounter if medication prescribed (15% probability)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 55).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <param name="includeProbabilisticOutcomes">
    /// Whether to include probabilistic screening outcomes (default: true).
    /// Set to false for deterministic test scenarios.
    /// </param>
    /// <returns>A complete scenario context with wellness visit resources and probabilistic outcomes.</returns>
    /// <remarks>
    /// This scenario demonstrates realistic clinical workflows where screening tests may reveal
    /// conditions requiring treatment. The 15% probability reflects approximate prevalence of
    /// elevated cholesterol in the general adult population requiring pharmacotherapy.
    ///
    /// The scenario uses:
    /// - **CommonScenarios.RecordVitalSigns()**: Reusable fragment for standard vital signs
    /// - **CommonScenarios.BasicMetabolicPanel()**: Reusable fragment for metabolic labs
    /// - **CommonScenarios.LipidPanel()**: Reusable fragment for lipid screening
    /// - **ProbabilisticBranchState**: Models 15% prevalence of high cholesterol
    /// </remarks>
    public static ScenarioContext GetEnhancedWellnessVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 55,
        string gender = "male",
        bool includeProbabilisticOutcomes = true)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Enhanced Annual Wellness Visit with Probabilistic Screening Outcomes")
            .WithDescription("Annual wellness visit demonstrating reusable scenario fragments, probabilistic branching for screening outcomes, and realistic clinical workflows.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Wellness encounter
            .AddWellnessVisit("Annual wellness examination")

            // REUSABLE FRAGMENT: Record standard vital signs
            // Demonstrates CommonScenarios.RecordVitalSigns() composition
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Record Standard Vital Signs")

            // REUSABLE FRAGMENT: Order Basic Metabolic Panel
            // Demonstrates CommonScenarios.BasicMetabolicPanel() composition
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Order Basic Metabolic Panel")

            // REUSABLE FRAGMENT: Order Lipid Panel for cardiovascular risk assessment
            // Demonstrates CommonScenarios.LipidPanel() composition
            .AddSubScenario(CommonScenarios.LipidPanel(), "Order Lipid Panel");

        // PROBABILISTIC BRANCHING: Model realistic screening outcomes
        // 15% chance of elevated cholesterol requiring statin therapy (clinical prevalence)
        if (includeProbabilisticOutcomes)
        {
            builder.AddProbabilisticBranch(
                0.15,  // 15% probability of abnormal lipid panel

                // TRUE PATH: Elevated cholesterol - prescribe statin and schedule follow-up
                new ConditionOnsetState
                {
                    Name = "Hyperlipidemia_Onset",
                    Code = FhirCode.Conditions.Hyperlipidemia,
                    Severity = 2,
                    AssignToAttribute = "hyperlipidemia_condition"
                }
                .ThenAddMedicationOrder(MedicationOrderState.Atorvastatin20mg())
                .ThenDelay(TimeSpan.FromDays(90))  // 3-month follow-up
                .ThenAddEncounter("Lipid panel follow-up - statin monitoring")
                .ThenAddSubScenario(CommonScenarios.LipidPanel()),

                // FALSE PATH: Normal lipid panel - no action needed (85% probability)
                new DelayState { Name = "Normal_Screening", Exact = TimeSpan.Zero }
            );
        }

        return builder.Build();
    }

    /// <summary>
    /// Generates a wellness visit scenario with multiple probabilistic screening outcomes.
    /// Demonstrates complex branching with multiple independent probabilities.
    ///
    /// Screening probabilities (evidence-based):
    /// - Prediabetes (A1C 5.7-6.4%): 38% prevalence in US adults (CDC, 2021)
    /// - Vitamin D deficiency (&lt;20 ng/mL): 42% prevalence in US adults (NHANES)
    /// - Elevated blood pressure (130/80+): 47% prevalence in US adults (AHA, 2021)
    ///
    /// Timeline:
    /// 1. Annual wellness visit with comprehensive labs
    /// 2. Probabilistic branch: 38% chance of prediabetes diagnosis
    /// 3. Probabilistic branch: 42% chance of vitamin D deficiency
    /// 4. Probabilistic branch: 47% chance of elevated blood pressure
    /// 5. Follow-up visits scheduled based on findings
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 60).</param>
    /// <param name="gender">Patient gender (default: "female").</param>
    /// <returns>A complete scenario context with multiple probabilistic screening outcomes.</returns>
    public static ScenarioContext GetComprehensiveScreeningVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 60,
        string gender = "female")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Comprehensive Wellness Screening with Multiple Probabilistic Outcomes")
            .WithDescription("Annual wellness visit with evidence-based screening outcome probabilities for prediabetes, vitamin D deficiency, and hypertension.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Wellness encounter
            .AddWellnessVisit("Annual preventive health screening")

            // REUSABLE FRAGMENTS: Standard screening
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Vital Signs")
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Metabolic Labs")
            .AddSubScenario(CommonScenarios.LipidPanel(), "Lipid Screening")

            // Additional screening: Hemoglobin A1C for diabetes screening
            .AddObservation(ObservationState.HemoglobinA1c())

            // PROBABILISTIC OUTCOME 1: Prediabetes (38% prevalence)
            .AddProbabilisticBranch(
                0.38,  // 38% probability based on CDC prevalence data
                new ConditionOnsetState
                {
                    Name = "Prediabetes_Diagnosis",
                    Code = FhirCode.Conditions.Prediabetes,
                    Severity = 1,
                    AssignToAttribute = "prediabetes_condition"
                }
                .ThenDelay(TimeSpan.FromDays(180))  // 6-month follow-up
                .ThenAddEncounter("Prediabetes follow-up")
                .ThenAddObservation(ObservationState.HemoglobinA1c()),
                new DelayState { Name = "Normal_A1C", Exact = TimeSpan.Zero }
            )

            // PROBABILISTIC OUTCOME 2: Vitamin D deficiency (42% prevalence)
            .AddProbabilisticBranch(
                0.42,  // 42% probability based on NHANES data
                new ConditionOnsetState
                {
                    Name = "VitaminD_Deficiency",
                    Code = FhirCode.Conditions.VitaminDDeficiency,
                    Severity = 1,
                    AssignToAttribute = "vitamin_d_condition"
                }
                .ThenAddMedicationOrder(MedicationOrderState.VitaminD50000IU()),
                new DelayState { Name = "Normal_VitaminD", Exact = TimeSpan.Zero }
            )

            // PROBABILISTIC OUTCOME 3: Elevated blood pressure (47% prevalence)
            .AddProbabilisticBranch(
                0.47,  // 47% probability based on AHA hypertension statistics
                new ConditionOnsetState
                {
                    Name = "Hypertension_Stage1",
                    Code = FhirCode.Conditions.HypertensionEssential,
                    Severity = 1,
                    AssignToAttribute = "hypertension_condition"
                }
                .ThenDelay(TimeSpan.FromDays(90))  // 3-month follow-up for BP monitoring
                .ThenAddEncounter("Blood pressure follow-up")
                .ThenAddSubScenario(CommonScenarios.CardiovascularVitals()),
                new DelayState { Name = "Normal_BP", Exact = TimeSpan.Zero }
            )

            .Build();
    }
}

/// <summary>
/// Extension methods for chaining states in probabilistic branches.
/// Enables fluent composition: stateA.ThenAddEncounter().ThenAddObservation()
/// </summary>
internal static class ScenarioStateChainExtensions
{
    /// <summary>
    /// Chains a medication order after the current state.
    /// </summary>
    public static CompositeState ThenAddMedicationOrder(this ScenarioState state, MedicationOrderState medication)
    {
        return new CompositeState { States = [state, medication] };
    }

    /// <summary>
    /// Chains a delay after the current state.
    /// </summary>
    public static CompositeState ThenDelay(this ScenarioState state, TimeSpan duration)
    {
        return new CompositeState { States = [state, DelayState.ExactDuration(duration)] };
    }

    /// <summary>
    /// Chains a delay after a composite state.
    /// </summary>
    public static CompositeState ThenDelay(this CompositeState composite, TimeSpan duration)
    {
        composite.States.Add(DelayState.ExactDuration(duration));
        return composite;
    }

    /// <summary>
    /// Chains an encounter after the current state.
    /// </summary>
    public static CompositeState ThenAddEncounter(this CompositeState composite, string reason)
    {
        composite.States.Add(new EncounterState
        {
            Name = "Follow_Up_Encounter",
            EncounterClass = "ambulatory",
            Reason = reason
        });
        return composite;
    }

    /// <summary>
    /// Chains a sub-scenario after the current state.
    /// </summary>
    public static CompositeState ThenAddSubScenario(this CompositeState composite, Func<ScenarioBuilder, ScenarioBuilder> subScenario)
    {
        composite.States.Add(new CallSubScenarioState
        {
            Name = "Chained_SubScenario",
            SubScenario = subScenario
        });
        return composite;
    }

    /// <summary>
    /// Chains an observation after the current state.
    /// </summary>
    public static CompositeState ThenAddObservation(this CompositeState composite, ObservationState observation)
    {
        composite.States.Add(observation);
        return composite;
    }
}

/// <summary>
/// Composite state that executes multiple states in sequence.
/// Used for chaining states in probabilistic branches.
/// </summary>
internal sealed class CompositeState : ScenarioState
{
    public List<ScenarioState> States { get; init; } = [];

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        foreach (var state in States)
        {
            state.Execute(context, faker);
        }
    }
}
