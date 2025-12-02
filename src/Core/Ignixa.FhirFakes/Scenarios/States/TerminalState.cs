// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Marks the end of a scenario execution path.
/// Used to explicitly signal completion, death, or other terminal conditions.
/// </summary>
public sealed class TerminalState : ScenarioState
{
    /// <summary>
    /// Gets or sets the reason for the terminal state (e.g., "Completed", "Death").
    /// </summary>
    public string Reason { get; init; } = "Completed";

    /// <summary>
    /// Marks the scenario as terminated and sets context attributes.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Set flags to indicate scenario termination
        context.SetAttribute("scenario_completed", true);
        context.SetAttribute("terminal_reason", Reason);
    }

    /// <summary>
    /// Creates a terminal state with the "Completed" reason.
    /// </summary>
    public static TerminalState Completed() => new() { Reason = "Completed" };

    /// <summary>
    /// Creates a terminal state with the "Death" reason.
    /// </summary>
    public static TerminalState Death() => new() { Reason = "Death" };

    /// <summary>
    /// Creates a terminal state with a custom reason.
    /// </summary>
    public static TerminalState Custom(string reason) => new() { Reason = reason };
}
