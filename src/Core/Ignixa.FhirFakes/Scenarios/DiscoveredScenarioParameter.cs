// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Metadata describing one parameter of a <see cref="DiscoveredScenario"/> factory method, as produced
/// by <see cref="ScenarioCatalog"/>.
/// </summary>
public sealed class DiscoveredScenarioParameter
{
    /// <summary>
    /// The parameter name, matching the factory method's parameter name exactly (used as the key for
    /// <see cref="ScenarioCatalog.Invoke"/> parameter overrides).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The parameter's CLR type.
    /// </summary>
    public required Type Type { get; init; }

    /// <summary>
    /// The parameter's own default value, if it has one. Null when <see cref="HasDefaultValue"/> is false.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// True if the factory method parameter declares a default value.
    /// </summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>
    /// Minimum value hint from <see cref="ScenarioParameterAttribute.Min"/>, or null if unset/unannotated.
    /// </summary>
    public double? Min { get; init; }

    /// <summary>
    /// Maximum value hint from <see cref="ScenarioParameterAttribute.Max"/>, or null if unset/unannotated.
    /// </summary>
    public double? Max { get; init; }

    /// <summary>
    /// One-line description from <see cref="ScenarioParameterAttribute.Description"/>, or null if unset.
    /// </summary>
    public string? Description { get; init; }
}
