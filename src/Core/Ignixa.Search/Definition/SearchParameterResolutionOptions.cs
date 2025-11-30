// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Definition;

/// <summary>
/// Configuration options for resolving conflicts when multiple IGs define SearchParameters with the same code.
/// Supports both explicit priority-based resolution and semantic version fallback.
/// </summary>
public class SearchParameterResolutionOptions
{
    /// <summary>
    /// Explicit priority order for IG packages (highest priority first).
    /// When multiple IGs define the same code, the IG with the highest priority wins.
    /// Example: ["hl7.fhir.us.core", "hl7.fhir.au.base", "hl7.fhir.hl7v2"]
    /// If null or empty, falls back to semantic version comparison.
    /// </summary>
    public IReadOnlyList<string>? PackagePriorityOrder { get; init; }

    /// <summary>
    /// Whether to use semantic versioning for conflict resolution when no explicit priority is configured.
    /// Default: true (newer versions win).
    /// </summary>
    public bool UseSemanticVersioning { get; set; } = true;

    /// <summary>
    /// Whether to log warnings when conflicts are detected and resolved.
    /// Default: true (important for debugging and auditing).
    /// </summary>
    public bool LogConflicts { get; set; } = true;

    /// <summary>
    /// Whether to eagerly load all package search parameters on startup.
    /// When true: All package SearchParameters are loaded at initialization time (better runtime performance, predictable startup).
    /// When false: Package SearchParameters are loaded lazily per resource type (faster startup, slower first search per resource).
    /// Default: true (eager loading enabled for production).
    /// </summary>
    public bool EagerLoadPackageSearchParameters { get; set; } = true;

    /// <summary>
    /// Whether to fail startup if eager loading of package search parameters fails.
    /// Only applies when EagerLoadPackageSearchParameters is true.
    /// Default: false (log error and continue with base parameters).
    /// </summary>
    public bool FailStartupOnEagerLoadError { get; set; }

    /// <summary>
    /// Gets the priority rank for a package ID (lower number = higher priority).
    /// Returns int.MaxValue if package is not in the priority list.
    /// </summary>
    public int GetPriorityRank(string packageId)
    {
        if (PackagePriorityOrder == null || PackagePriorityOrder.Count == 0)
        {
            return int.MaxValue;
        }

        for (int i = 0; i < PackagePriorityOrder.Count; i++)
        {
            if (string.Equals(PackagePriorityOrder[i], packageId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
