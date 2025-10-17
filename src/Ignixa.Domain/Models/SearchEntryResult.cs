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
}
