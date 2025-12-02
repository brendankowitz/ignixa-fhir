// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that performs weighted random branching based on probability distributions.
/// Selects one state from multiple options using cumulative probability calculation.
/// Used to model realistic disease onset rates, condition prevalence, and epidemiological data.
/// </summary>
/// <remarks>
/// Example: Model appendicitis prevalence where 8.6% of patients develop the condition
/// and 91.4% remain healthy. Probabilities should sum to approximately 1.0.
/// </remarks>
public sealed class ProbabilisticBranchState : ScenarioState
{
    /// <summary>
    /// Gets or sets the list of branches with their associated probabilities.
    /// Each tuple contains a probability (0.0-1.0) and the state to execute if selected.
    /// </summary>
    public required IReadOnlyList<(double Probability, ScenarioState State)> Branches { get; init; }

    /// <summary>
    /// Gets or sets the tolerance for probability sum validation (default: 0.01).
    /// Probabilities should sum to approximately 1.0 within this tolerance.
    /// </summary>
    public double ProbabilityTolerance { get; init; } = 0.01;

    /// <summary>
    /// Executes weighted random selection using cumulative probability calculation.
    /// Validates probability sum and selects exactly one branch based on random value.
    /// </summary>
    /// <param name="context">The scenario context containing patient state and resources.</param>
    /// <param name="faker">The resource faker for generating realistic FHIR resources.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no branches are defined or if probability sum is outside tolerance range.
    /// </exception>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (Branches.Count == 0)
        {
            throw new InvalidOperationException("ProbabilisticBranchState requires at least one branch");
        }

        // Validate that probabilities sum to approximately 1.0
        var totalProbability = Branches.Sum(b => b.Probability);
        if (Math.Abs(totalProbability - 1.0) > ProbabilityTolerance)
        {
            throw new InvalidOperationException(
                $"Branch probabilities must sum to approximately 1.0 (within {ProbabilityTolerance} tolerance). " +
                $"Current sum: {totalProbability:F4}. " +
                $"Branches: {string.Join(", ", Branches.Select((b, i) => $"[{i}]: {b.Probability:F3}"))}");
        }

        // Generate random value [0.0, 1.0)
        var random = Random.Shared.NextDouble();
        var cumulative = 0.0;

        // Cumulative probability selection
        foreach (var (probability, state) in Branches)
        {
            cumulative += probability;
            if (random <= cumulative)
            {
                state.Execute(context, faker);
                return;
            }
        }

        // Fallback: execute last branch if floating-point precision issues occur
        Branches[^1].State.Execute(context, faker);
    }

    /// <summary>
    /// Creates a probabilistic branch state with the specified branches.
    /// Validates that probabilities are non-negative and sum to approximately 1.0.
    /// </summary>
    /// <param name="branches">
    /// Variable arguments of tuples containing probability and state.
    /// Each probability must be between 0.0 and 1.0, and the sum should equal 1.0.
    /// </param>
    /// <returns>A configured ProbabilisticBranchState.</returns>
    /// <exception cref="ArgumentException">Thrown if any probability is negative or greater than 1.0.</exception>
    public static ProbabilisticBranchState Create(params (double Probability, ScenarioState State)[] branches)
    {
        // Validate individual probabilities
        foreach (var (probability, _) in branches)
        {
            if (probability < 0.0 || probability > 1.0)
            {
                throw new ArgumentException(
                    $"Each branch probability must be between 0.0 and 1.0. Found: {probability:F4}",
                    nameof(branches));
            }
        }

        return new ProbabilisticBranchState
        {
            Branches = [..branches],
            Name = $"ProbabilisticBranch({branches.Length} options)"
        };
    }

    /// <summary>
    /// Creates a binary probabilistic branch (e.g., disease occurs vs. stays healthy).
    /// </summary>
    /// <param name="probabilityA">Probability of executing the first state (0.0-1.0).</param>
    /// <param name="stateA">State to execute with probability A.</param>
    /// <param name="stateB">State to execute with probability (1.0 - A).</param>
    /// <returns>A configured ProbabilisticBranchState with two branches.</returns>
    public static ProbabilisticBranchState Binary(double probabilityA, ScenarioState stateA, ScenarioState stateB)
    {
        if (probabilityA < 0.0 || probabilityA > 1.0)
        {
            throw new ArgumentException(
                $"Probability must be between 0.0 and 1.0. Found: {probabilityA:F4}",
                nameof(probabilityA));
        }

        return new ProbabilisticBranchState
        {
            Branches =
            [
                (probabilityA, stateA),
                (1.0 - probabilityA, stateB)
            ],
            Name = $"BinaryBranch({probabilityA:P1})"
        };
    }
}
