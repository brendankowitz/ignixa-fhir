// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Lifecycle;

/// <summary>
/// Lifecycle event that probabilistically triggers a condition onset within a specified age range.
/// Models real-world disease epidemiology by using probability distributions based on age.
/// </summary>
/// <remarks>
/// <para>
/// This event type is used to simulate diseases that have known onset patterns in the population.
/// For example:
/// <list type="bullet">
///   <item><description>Asthma: 26.3% onset ages 1-17, 42.3% ages 18-44</description></item>
///   <item><description>Type 2 Diabetes: Increases with age, peaks 35-65</description></item>
///   <item><description>Hypertension: 29.6% baseline, increases with age</description></item>
/// </list>
/// </para>
/// <para>
/// Once a condition has occurred, this event will not trigger again (one-time occurrence).
/// The probability is checked each year within the onset age range using a random number generator.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// new ProbabilisticConditionOnset(
///     "Pediatric Asthma",
///     onsetAges: 1..17,
///     probability: 0.263,
///     scenarioFactory: sp => new ScenarioBuilder(sp)
///         .AddConditionOnset(FhirCode.Conditions.Asthma)
///         .AddMedicationOrder(FhirCode.Medications.Albuterol))
/// </code>
/// </para>
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class ProbabilisticConditionOnset : ILifecycleEvent
{
    private readonly string _conditionName;
    private readonly Range _onsetAges;
    private readonly double _probability;
    private readonly Func<IFhirSchemaProvider, ScenarioBuilder> _scenarioFactory;
    private bool _hasOccurred;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProbabilisticConditionOnset"/> class.
    /// </summary>
    /// <param name="conditionName">
    /// The name of the condition for logging and tracking purposes.
    /// </param>
    /// <param name="onsetAges">
    /// The age range (inclusive) during which the condition may onset.
    /// Use C# range syntax: <c>1..17</c> for ages 1 through 17.
    /// </param>
    /// <param name="probability">
    /// The annual probability (0.0 to 1.0) of the condition occurring at each applicable age.
    /// This is checked each year within the onset range until the condition occurs.
    /// </param>
    /// <param name="scenarioFactory">
    /// A factory function that creates a ScenarioBuilder to generate the clinical resources
    /// when the condition onsets. The factory receives the IFhirSchemaProvider.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="conditionName"/> or <paramref name="scenarioFactory"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="probability"/> is less than 0.0 or greater than 1.0.
    /// </exception>
    public ProbabilisticConditionOnset(
        string conditionName,
        Range onsetAges,
        double probability,
        Func<IFhirSchemaProvider, ScenarioBuilder> scenarioFactory)
    {
        ArgumentNullException.ThrowIfNull(conditionName);
        ArgumentNullException.ThrowIfNull(scenarioFactory);

        if (probability < 0.0 || probability > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probability),
                probability,
                "Probability must be between 0.0 and 1.0.");
        }

        _conditionName = conditionName;
        _onsetAges = onsetAges;
        _probability = probability;
        _scenarioFactory = scenarioFactory;
    }

    /// <inheritdoc />
    public string Name => $"ProbabilisticCondition_{_conditionName}";

    /// <summary>
    /// Gets whether this condition has already occurred.
    /// Once a condition onsets, it will not trigger again.
    /// </summary>
    public bool HasOccurred => _hasOccurred;

    /// <summary>
    /// Gets the configured probability for this condition.
    /// </summary>
    public double Probability => _probability;

    /// <summary>
    /// Gets the onset age range for this condition.
    /// </summary>
    public Range OnsetAges => _onsetAges;

    /// <inheritdoc />
    /// <remarks>
    /// Returns <c>true</c> if:
    /// <list type="bullet">
    ///   <item><description>The condition has not yet occurred (<see cref="HasOccurred"/> is false)</description></item>
    ///   <item><description>The patient's age is within the onset age range (inclusive)</description></item>
    /// </list>
    /// </remarks>
    public bool IsApplicable(int patientAge)
    {
        if (_hasOccurred)
        {
            return false;
        }

        // Check if age is within the range (inclusive)
        var startAge = _onsetAges.Start.Value;
        var endAge = _onsetAges.End.Value;

        return patientAge >= startAge && patientAge <= endAge;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Executes a random probability check. If the random value is less than or equal to
    /// the configured probability, the condition onsets and the scenario factory is invoked
    /// to generate clinical resources (conditions, medications, observations, etc.).
    /// </para>
    /// <para>
    /// Once triggered, <see cref="HasOccurred"/> is set to <c>true</c> and subsequent
    /// calls to <see cref="IsApplicable"/> will return <c>false</c>.
    /// </para>
    /// </remarks>
    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        // Probability check
        var roll = Random.Shared.NextDouble();
        if (roll > _probability)
        {
            // Condition did not occur this year
            return;
        }

        // Condition occurs!
        _hasOccurred = true;

        // Store the onset information in context attributes
        context.SetAttribute($"{_conditionName}_onset_age", context.CurrentAge);
        context.SetAttribute($"{_conditionName}_onset_date", context.CurrentTime);

        // Build and execute the condition scenario
        var scenarioBuilder = _scenarioFactory(schemaProvider);
        var states = scenarioBuilder.GetStates();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        foreach (var state in states)
        {
            state.Execute(context, faker);
        }
    }

    /// <summary>
    /// Resets the occurrence state, allowing the condition to potentially occur again.
    /// This is primarily useful for testing scenarios.
    /// </summary>
    public void Reset()
    {
        _hasOccurred = false;
    }
}
