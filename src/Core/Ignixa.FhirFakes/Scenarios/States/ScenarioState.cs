// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

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

    /// <summary>
    /// Removes all choice element variants (e.g., effective[x]) from a JSON node.
    /// Call this before setting a choice element to avoid "multiple type variants" validation errors.
    /// </summary>
    /// <param name="node">The JSON object to modify.</param>
    /// <param name="baseName">The base name of the choice element (e.g., "effective", "performed", "value").</param>
    /// <example>
    /// // Before setting effectiveDateTime, remove all effective[x] variants
    /// RemoveChoiceConflicts(node, "effective");
    /// node["effectiveDateTime"] = DateTime.UtcNow.ToString("o");
    /// </example>
    protected static void RemoveChoiceConflicts(JsonObject node, string baseName)
    {
        var keysToRemove = node
            .Where(kvp => kvp.Key.StartsWith(baseName, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            node.Remove(key);
        }
    }
}
