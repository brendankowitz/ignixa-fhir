// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Metadata.Models;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Represents a segment of a FHIR CapabilityStatement that can be applied independently.
/// Segments have different refresh rates (static, quasi-static, dynamic) and can compute
/// version hashes for cache invalidation.
/// </summary>
public interface ICapabilitySegment
{
    /// <summary>
    /// Unique identifier for this segment (e.g., "static", "interactions", "search-params").
    /// Used for cache invalidation and diagnostics.
    /// </summary>
    string SegmentKey { get; }

    /// <summary>
    /// Execution priority for segment application.
    /// Lower priority values execute first.
    /// Recommended: 10=static, 20=quasi-static, 30=dynamic, 40+=tenant-specific.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Applies this segment's capabilities to the capability statement.
    /// </summary>
    /// <param name="statement">The capability statement being built.</param>
    /// <param name="context">Context for capability generation (FHIR version, tenant, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Computes a version hash for this segment's current state.
    /// The hash changes when the segment's content would change.
    /// Used for cache validation - if all segment hashes match cached entry, cache is still valid.
    /// </summary>
    /// <param name="context">Context for capability generation (FHIR version, tenant, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Version hash string (typically SHA256 or semantic version).</returns>
    ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken);
}
