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
    /// Free-text grouping label (e.g. "Chronic", "Emergency", "Pediatric"). Null if uncategorized.
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
}
