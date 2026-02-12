// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Anonymizer.Models;

/// <summary>
/// Represents the result of anonymizing a FHIR resource.
/// </summary>
public sealed record AnonymizationResult
{
    /// <summary>
    /// The anonymized resource as a mutable node for pipeline chaining.
    /// Use this when passing to other processing stages to avoid serialization overhead.
    /// </summary>
    public required ResourceJsonNode Resource { get; init; }

    /// <summary>
    /// The anonymized resource JSON string.
    /// Convenience property for final output. Computed from Resource if needed.
    /// </summary>
    public required string AnonymizedJson { get; init; }

    /// <summary>
    /// Performance metrics from the anonymization process.
    /// </summary>
    public required ProcessingMetrics Metrics { get; init; }

    /// <summary>
    /// Non-fatal warnings generated during processing.
    /// </summary>
    public ImmutableArray<string> Warnings { get; init; } = [];

    /// <summary>
    /// Security labels indicating which anonymization operations were applied.
    /// </summary>
    public required AppliedSecurityLabels AppliedLabels { get; init; }
}

/// <summary>
/// Performance metrics from anonymization processing.
/// </summary>
public sealed record ProcessingMetrics
{
    /// <summary>
    /// Total number of nodes processed.
    /// </summary>
    public required int NodesProcessed { get; init; }

    /// <summary>
    /// Total time taken to process the resource.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Count of each anonymization operation type applied.
    /// </summary>
    public required ImmutableDictionary<string, int> OperationCounts { get; init; }
}

/// <summary>
/// Tracks which anonymization operations were applied to a resource.
/// </summary>
public sealed record AppliedSecurityLabels
{
    public bool IsRedacted { get; init; }
    public bool IsAbstracted { get; init; }
    public bool IsCryptoHashed { get; init; }
    public bool IsEncrypted { get; init; }
    public bool IsPerturbed { get; init; }
    public bool IsSubstituted { get; init; }
    public bool IsGeneralized { get; init; }
}
