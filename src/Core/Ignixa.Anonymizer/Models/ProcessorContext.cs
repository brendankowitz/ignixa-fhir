// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;

namespace Ignixa.Anonymizer.Models;

/// <summary>
/// Context passed to anonymization processors during execution.
/// </summary>
public sealed record ProcessorContext
{
    /// <summary>
    /// The ID of the resource being processed.
    /// Used by processors that need per-resource context (e.g., DateShiftProcessor).
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Optional processor-specific settings from the configuration rule.
    /// </summary>
    public ImmutableDictionary<string, object>? Settings { get; init; }

    /// <summary>
    /// Tracks visited node locations to prevent infinite recursion.
    /// Uses Location strings (e.g., "Patient.name[0].use") since IElement instances
    /// are not stable across calls.
    /// </summary>
    public required HashSet<string> VisitedNodes { get; init; }
}
