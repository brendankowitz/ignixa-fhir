// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;

namespace Ignixa.DeId.Models;

/// <summary>
/// Result of a processor's de-identification operation.
/// </summary>
public sealed record ProcessorResult
{
    /// <summary>
    /// Indicates whether the processor modified the resource.
    /// </summary>
    public required bool WasModified { get; init; }

    /// <summary>
    /// The type of de-identification operation performed (e.g., "REDACT", "DATESHIFT").
    /// </summary>
    public required string OperationType { get; init; }

    /// <summary>
    /// FHIRPath locations of nodes that were processed.
    /// </summary>
    public ImmutableArray<string> ProcessedPaths { get; init; } = [];
}
