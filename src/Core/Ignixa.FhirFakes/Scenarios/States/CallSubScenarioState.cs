// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that executes a reusable scenario fragment (sub-scenario).
/// Enables composition of scenarios by calling predefined scenario builders.
/// This allows creation of common clinical patterns that can be reused across multiple scenarios.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// .AddState(new CallSubScenarioState
/// {
///     Name = "Record Vital Signs",
///     SubScenario = CommonScenarios.RecordVitalSigns()
/// })
/// </code>
/// </remarks>
public sealed class CallSubScenarioState : ScenarioState
{
    /// <summary>
    /// Gets or sets the sub-scenario builder function.
    /// This function takes a ScenarioBuilder and returns a configured builder with additional states.
    /// </summary>
    public required Func<ScenarioBuilder, ScenarioBuilder> SubScenario { get; init; }

    /// <summary>
    /// Executes the sub-scenario by creating a new builder and executing all its states.
    /// The sub-scenario shares the same context and faker as the parent scenario.
    /// </summary>
    /// <param name="context">The scenario context containing patient state and resources.</param>
    /// <param name="faker">The resource faker for generating realistic FHIR resources.</param>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);
        ArgumentNullException.ThrowIfNull(SubScenario);

        // Create a temporary builder to collect states from the sub-scenario
        var tempBuilder = new ScenarioBuilder(faker.SchemaProvider);

        // Apply the sub-scenario function to build up states
        var configuredBuilder = SubScenario(tempBuilder);

        // Execute all states from the sub-scenario against the current context
        var states = configuredBuilder.GetStates();
        foreach (var state in states)
        {
            state.Execute(context, faker);
        }
    }
}
