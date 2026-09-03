// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Minimal result for search/read operations.
/// Contains raw JSON bytes + metadata, no parsing required.
/// Enables zero-copy serialization from data layer to HTTP response.
/// </summary>
public record SearchEntryResult(
    string ResourceType,
    string ResourceId,
    string VersionId,
    DateTimeOffset LastModified,
    ReadOnlyMemory<byte> ResourceBytes)
{
    /// <summary>
    /// Indicates if this resource has been deleted.
    /// </summary>
    public bool IsDeleted { get; init; }

    /// <summary>
    /// Optional tenant identifier for multi-tenant scenarios.
    /// </summary>
    public int? TenantId { get; init; }

    /// <summary>
    /// Optional HTTP request metadata.
    /// </summary>
    public ResourceRequest? Request { get; init; }

    /// <summary>
    /// Indicates how this entry relates to a search (match vs include vs outcome).
    /// Used for setting FHIR Bundle.entry.search.mode in search responses.
    /// Defaults to Match for backward compatibility.
    /// </summary>
    public SearchEntryMode SearchMode { get; init; } = SearchEntryMode.Match;

    /// <summary>
    /// True for a synthetic entry that carries no resource content and exists purely to prove a
    /// further page exists.
    /// </summary>
    /// <remarks>
    /// A data layer that over-fetches one lookahead ("probe") row past the caller's page size, so a
    /// pagination serializer can detect a further page by counting deliveries, has no way to signal
    /// that fact if the probe row itself cannot be turned into a deliverable resource -- a corrupt
    /// stored payload, or a match whose fetch missed because of a concurrent delete. Silently
    /// dropping that row (the same skip-and-continue every other unmappable row already gets) would
    /// make the delivered count fall exactly at the page size, which a counting-based pagination
    /// serializer cannot distinguish from "there is no further page" -- silently truncating a result
    /// set with no error and no next link. Yielding this sentinel in the probe row's place preserves
    /// the count the serializer relies on without fabricating content for a row that was never
    /// actually deliverable. A consumer must treat it as pure signal: never render it, and never
    /// count it toward a page's rendered entries.
    /// </remarks>
    public bool IsPagingProbe { get; init; }
}
