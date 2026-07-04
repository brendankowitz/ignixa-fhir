// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Annotates a predefined scenario factory method with catalog metadata (category, title, description)
/// consumed by <see cref="ScenarioCatalog"/> and surfaced to downstream UIs. Optional: unannotated
/// methods still work, falling back to a humanized id for <see cref="Title"/> and null for the rest.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ScenarioAttribute : Attribute
{
    /// <summary>
    /// Explicit scenario id. When set, overrides the method-name-derived id so a factory method can
    /// be renamed without breaking the published id consumers store.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Free-text grouping label (e.g. "Chronic", "Emergency", "Pediatric"). Null if uncategorized.
    /// This is a free-text presentation label for grouping in UIs; for the machine-usable clinical
    /// taxonomy use <see cref="Domain"/>.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Human-readable title. If not set, <see cref="ScenarioCatalog"/> derives one from the scenario id
    /// by inserting spaces before internal capital letters (e.g. "DiabeticPatient" -> "Diabetic Patient").
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// One-line description of what the scenario generates.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Clinical specialty this scenario belongs to. <see cref="ClinicalDomain.Unspecified"/> (the
    /// default) means "not declared" and surfaces as null on <see cref="DiscoveredScenario.Domain"/>.
    /// </summary>
    public ClinicalDomain Domain { get; init; } = ClinicalDomain.Unspecified;
}
