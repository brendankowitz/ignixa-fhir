// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// Contains bundle metadata and a streaming enumerable of entries.
/// Provides immediate access to bundle header information while preserving streaming for entries.
/// </summary>
public class StreamingBundleContext
{
    /// <summary>
    /// Gets the resource type (should be "Bundle" for valid FHIR bundles).
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// Gets the bundle type (transaction, batch, searchset, etc.).
    /// May be null if the bundle type is not specified.
    /// </summary>
    public required string? BundleType { get; init; }

    /// <summary>
    /// Gets the links for pagination (self, next, prev).
    /// Empty list if no links are present in the bundle.
    /// </summary>
    public required IReadOnlyList<BundleLink> Links { get; init; }

    /// <summary>
    /// Gets parsing issues encountered during bundle header parsing.
    /// Includes validation errors, missing fields, and other non-fatal issues.
    /// </summary>
    public required IReadOnlyList<string> ParsingIssues { get; init; }

    /// <summary>
    /// Gets the streaming enumerable of bundle entries.
    /// Entries are yielded as they are parsed from the input stream.
    /// </summary>
    public required IAsyncEnumerable<BundleEntryContext> Entries { get; init; }
}
