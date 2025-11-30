// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Metadata.Models;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Represents a cached CapabilityStatement with version tracking.
/// The VersionHash is a composite of all segment hashes - if it matches current segments,
/// the cached statement is still valid.
/// </summary>
/// <param name="Statement">The cached capability statement.</param>
/// <param name="VersionHash">Composite hash of all segment versions at time of caching.</param>
/// <param name="CachedAt">When this entry was cached.</param>
public record CapabilityCacheEntry(
    CapabilityStatementJsonNode Statement,
    string VersionHash,
    DateTimeOffset CachedAt);
