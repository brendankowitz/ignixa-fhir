// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Base class for all scenario states in the state machine.
/// Each state knows how to execute its logic and produce resources.
/// </summary>
public abstract class ScenarioState
{
    /// <summary>
    /// Gets or sets the name of this state (for debugging/logging).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique identifier for this state.
    /// Used for cross-state references (e.g., DiagnosticReport referencing Observations).
    /// If not specified, the state is not referenceable by other states.
    /// </summary>
    public string? StateId { get; init; }

    /// <summary>
    /// Executes this state's logic against the scenario context.
    /// May generate FHIR resources, modify attributes, or advance time.
    /// </summary>
    /// <param name="context">The scenario context containing patient state and resources.</param>
    /// <param name="faker">The resource faker for generating realistic FHIR resources.</param>
    public abstract void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker);
}
