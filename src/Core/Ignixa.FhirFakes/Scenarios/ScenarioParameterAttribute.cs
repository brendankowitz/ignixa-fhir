// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Annotates a scenario factory method parameter with UI hints (numeric bounds, description) consumed
/// by <see cref="ScenarioCatalog"/> and surfaced to downstream UIs (e.g. slider min/max). Optional.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ScenarioParameterAttribute : Attribute
{
    /// <summary>
    /// Minimum value hint for numeric parameters. <see cref="double.NaN"/> (the default) means "unset";
    /// attributes cannot take a nullable <see cref="double"/>, so <see cref="ScenarioCatalog"/> converts
    /// NaN to <see langword="null"/> when building <see cref="DiscoveredScenarioParameter"/> metadata.
    /// </summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>
    /// Maximum value hint for numeric parameters. See <see cref="Min"/> for the NaN-as-unset convention.
    /// </summary>
    public double Max { get; init; } = double.NaN;

    /// <summary>
    /// One-line description of what the parameter controls.
    /// </summary>
    public string? Description { get; init; }
}
