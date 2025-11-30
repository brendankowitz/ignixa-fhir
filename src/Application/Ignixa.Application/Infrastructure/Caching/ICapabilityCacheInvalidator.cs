// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Metadata.Segments;

namespace Ignixa.Application.Infrastructure.Caching;

/// <summary>
/// Service for invalidating capability statement caches when server capabilities change.
/// Used when profiles are loaded (Phase 11) or custom search parameters are registered (Phase 12).
/// </summary>
public interface ICapabilityCacheInvalidator
{
    /// <summary>
    /// Invalidates capability caches when Implementation Guides are loaded/unloaded.
    /// Called by $load-ig operation (Phase 11).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask InvalidateForProfileChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates capability caches when custom search parameters are registered.
    /// Called by POST /SearchParameter operation (Phase 12).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask InvalidateForSearchParameterChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all capability caches across all tenants and versions.
    /// Use for administrative operations or when segment configuration changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates capability cache for a specific tenant.
    /// </summary>
    /// <param name="tenantId">Tenant ID to invalidate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask InvalidateForTenantAsync(int tenantId, CancellationToken cancellationToken = default);
}
