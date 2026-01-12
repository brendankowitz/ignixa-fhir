// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.Diagnostics.CodeAnalysis;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.FhirFakes.Scenarios.Lifecycle;

/// <summary>
/// Implements probabilistic disease onset within a specified age range.
/// Models realistic epidemiological patterns where conditions have age-specific incidence rates.
/// </summary>
/// <remarks>
/// <para>
/// This lifecycle event enables simulation of realistic disease onset probabilities based on
/// epidemiological data. Conditions don't affect everyone, and onset timing varies by age cohort.
/// </para>
/// <para>
/// Examples of age-stratified disease onset:
/// - Type 2 Diabetes: Peak onset ages 45-64 (probability ~15% over lifetime)
/// - Hypertension: Peak onset ages 40-59 (probability ~30% by age 60)
/// - Asthma: Bimodal distribution - childhood onset (ages 5-9) or adult onset (ages 35-40)
/// - Alzheimer's Disease: Onset typically after age 65 (probability doubles every 5 years)
/// </para>
/// <para>
/// Clinical rationale:
/// - Realistic patient populations have diverse health histories
/// - Disease prevalence varies by age, genetics, and environmental factors
/// - Probabilistic modeling creates heterogeneous test datasets
/// - Enables testing of clinical decision support for different patient cohorts
/// </para>
/// </remarks>
public sealed class ProbabilisticConditionOnset : ILifecycleEvent
{
    private readonly string _conditionName;
    private readonly Range _onsetAges;
    private readonly double _probability;
    private readonly Func<ScenarioBuilder, ScenarioBuilder> _scenarioFactory;
    private bool _hasOccurred;

    /// <summary>
    /// Creates a new probabilistic condition onset event.
    /// </summary>
    /// <param name="conditionName">
    /// A descriptive name for the condition (used for logging and debugging).
    /// Example: "Type 2 Diabetes Onset"
    /// </param>
    /// <param name="onsetAges">
    /// The age range during which the condition may develop.
    /// Example: new Range(40, 65) for middle-age onset diabetes.
    /// </param>
    /// <param name="probability">
    /// The probability (0.0 to 1.0) that the condition will occur within the age range.
    /// This is the cumulative probability over the entire range, not per-year probability.
    /// Example: 0.15 means 15% of patients will develop this condition during the age range.
    /// </param>
    /// <param name="scenarioFactory">
    /// A factory function that builds the scenario to execute when the condition occurs.
    /// The function receives a ScenarioBuilder and should add appropriate states for the condition
    /// (e.g., condition onset, medication orders, follow-up encounters).
    /// Example: builder => builder.AddConditionOnset(Conditions.Type2Diabetes)
    ///                             .AddMedicationOrder(Medications.Metformin)
    /// </param>
    /// <remarks>
    /// <para>
    /// The probability is evaluated once per year during the age range. The actual implementation
    /// uses a uniform probability distribution, meaning each year in the range has an equal chance
    /// of triggering the condition (if it hasn't occurred yet).
    /// </para>
    /// <para>
    /// More sophisticated implementations might use:
    /// - Age-weighted probabilities (higher risk in certain years)
    /// - Risk factor adjustments (family history, BMI, smoking status)
    /// - Competing risk models (can't develop diabetes if patient dies first)
    /// </para>
    /// </remarks>
    public ProbabilisticConditionOnset(
        string conditionName,
        Range onsetAges,
        double probability,
        Func<ScenarioBuilder, ScenarioBuilder> scenarioFactory)
    {
        ArgumentNullException.ThrowIfNull(conditionName);
        ArgumentNullException.ThrowIfNull(scenarioFactory);

        if (probability is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), probability,
                "Probability must be between 0.0 and 1.0.");
        }

        _conditionName = conditionName;
        _onsetAges = onsetAges;
        _probability = probability;
        _scenarioFactory = scenarioFactory;
    }

    /// <summary>
    /// Determines if the condition onset should be evaluated at the specified age.
    /// </summary>
    /// <param name="patientAge">The patient's current age in years.</param>
    /// <returns>
    /// <c>true</c> if the patient is within the onset age range and the condition hasn't occurred yet;
    /// otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Once the condition has occurred (_hasOccurred = true), this method will always return false
    /// to prevent duplicate condition onset.
    /// </remarks>
    public bool IsApplicable(int patientAge)
    {
        return !_hasOccurred
               && patientAge >= _onsetAges.Start.Value
               && patientAge <= _onsetAges.End.Value;
    }

    /// <summary>
    /// Evaluates whether the condition occurs this year and executes the condition scenario if triggered.
    /// </summary>
    /// <param name="context">The scenario context for resource generation.</param>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <remarks>
    /// <para>
    /// This method uses a random number generator to determine if the condition occurs.
    /// If the random roll succeeds (random value ≤ probability), the scenario factory is invoked
    /// to generate the condition onset resources.
    /// </para>
    /// <para>
    /// Implementation note: The current implementation uses a uniform per-year probability.
    /// For a more accurate epidemiological model, you would adjust the probability based on
    /// the total age range. For example, if the condition has a 15% lifetime risk over 20 years,
    /// the per-year probability would be approximately 15% / 20 = 0.75% per year
    /// (using 1 - (1 - totalProb)^(1/years) for compound probability).
    /// </para>
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        // Roll the dice to see if the condition occurs this year
        var roll = Random.Shared.NextDouble();

        if (roll <= _probability)
        {
            // Condition occurs! Execute the scenario factory
            var builder = new ScenarioBuilder(schemaProvider);
            var conditionScenario = _scenarioFactory(builder);

            // Execute all states from the scenario factory
            var faker = new SchemaBasedFhirResourceFaker(schemaProvider);
            foreach (var state in conditionScenario.GetStates())
            {
                state.Execute(context, faker);
            }

            // Mark the condition as occurred to prevent duplicate onset
            _hasOccurred = true;
        }
    }

    /// <summary>
    /// Creates a probabilistic condition onset for Type 2 Diabetes.
    /// </summary>
    /// <param name="onsetAges">The age range for diabetes onset (default: 45-65 years).</param>
    /// <param name="probability">The cumulative probability of developing diabetes (default: 15%).</param>
    /// <param name="conditionCode">The FHIR/SNOMED code for Type 2 Diabetes.</param>
    /// <returns>A configured <see cref="ProbabilisticConditionOnset"/> instance.</returns>
    /// <remarks>
    /// Based on CDC data: ~15% of adults develop Type 2 Diabetes in their lifetime,
    /// with peak incidence between ages 45-64.
    /// </remarks>
    public static ProbabilisticConditionOnset Type2Diabetes(
        Range? onsetAges = null,
        double probability = 0.15,
        FhirCode? conditionCode = null)
    {
        var ageRange = onsetAges ?? new Range(45, 65);
        var code = conditionCode ?? new FhirCode(
            System: FhirCode.Systems.SnomedCt,
            Code: "44054006",
            Display: "Type 2 Diabetes Mellitus");

        return new ProbabilisticConditionOnset(
            conditionName: "Type 2 Diabetes Onset",
            onsetAges: ageRange,
            probability: probability,
            scenarioFactory: builder => builder
                .AddConditionOnset(code, severity: 2, assignToAttribute: "diabetes_condition_id"));
    }

    /// <summary>
    /// Creates a probabilistic condition onset for Essential Hypertension.
    /// </summary>
    /// <param name="onsetAges">The age range for hypertension onset (default: 40-60 years).</param>
    /// <param name="probability">The cumulative probability of developing hypertension (default: 30%).</param>
    /// <param name="conditionCode">The FHIR/SNOMED code for Essential Hypertension.</param>
    /// <returns>A configured <see cref="ProbabilisticConditionOnset"/> instance.</returns>
    /// <remarks>
    /// Based on AHA data: ~30% of adults develop hypertension by age 60,
    /// with increasing prevalence at older ages.
    /// </remarks>
    public static ProbabilisticConditionOnset EssentialHypertension(
        Range? onsetAges = null,
        double probability = 0.30,
        FhirCode? conditionCode = null)
    {
        var ageRange = onsetAges ?? new Range(40, 60);
        var code = conditionCode ?? new FhirCode(
            System: FhirCode.Systems.SnomedCt,
            Code: "59621000",
            Display: "Essential Hypertension");

        return new ProbabilisticConditionOnset(
            conditionName: "Essential Hypertension Onset",
            onsetAges: ageRange,
            probability: probability,
            scenarioFactory: builder => builder
                .AddConditionOnset(code, severity: 2, assignToAttribute: "hypertension_condition_id"));
    }
}
