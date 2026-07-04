// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Metadata describing a discovered predefined scenario, produced by <see cref="ScenarioCatalog"/>.
/// </summary>
public sealed class DiscoveredScenario
{
    /// <summary>
    /// The scenario id (e.g. "DiabeticPatient"), derived from the factory method name with a leading
    /// "Get" stripped. Matched case-insensitively by <see cref="ScenarioCatalog.Find"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Free-text grouping label from <see cref="ScenarioAttribute.Category"/>, or null if unannotated.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Human-readable title, either from <see cref="ScenarioAttribute.Title"/> or a humanized <see cref="Id"/>.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// One-line description from <see cref="ScenarioAttribute.Description"/>, or null if unannotated.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Metadata for each factory method parameter after the leading <c>IFhirSchemaProvider</c> parameter.
    /// </summary>
    public required IReadOnlyList<DiscoveredScenarioParameter> Parameters { get; init; }

    /// <summary>
    /// Clinical specialty from <see cref="ScenarioAttribute.Domain"/>, or null if undeclared.
    /// </summary>
    public ClinicalDomain? Domain { get; init; }

    /// <summary>
    /// The underlying factory method. Internal so callers cannot bypass <see cref="ScenarioCatalog.Invoke"/>
    /// and its parameter-fallback / exception-wrapping behavior via raw reflection. Visible to
    /// <c>Ignixa.FhirFakes.Tests</c> via <c>InternalsVisibleTo</c> so tests can construct synthetic
    /// scenarios pointing at test-local methods. Not <c>required</c> (a required member cannot be less
    /// visible than its public containing type, per CS9032) — always set via the object initializer by
    /// <c>ScenarioCatalog.Discover()</c> and by tests; the <c>= null!</c> default only silences the
    /// nullable-reference-type warning since the compiler can no longer enforce it's set.
    /// </summary>
    internal MethodInfo Method { get; init; } = null!;
}
