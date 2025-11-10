// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Domain;

namespace Ignixa.Specification;

/// <summary>
/// Registry for managing composite schema provider instances and their cache invalidation.
/// Enables coordination between package loading and validation schema caching.
/// </summary>
public interface ICompositeSchemaProviderRegistry
{
    /// <summary>
    /// Registers a composite provider instance for a tenant.
    /// Called during validation setup to track active providers.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="provider">Schema provider instance</param>
    void RegisterProvider(int tenantId, IFhirSchemaProvider provider);

    /// <summary>
    /// Invalidates cache for all instances of a loaded package with debounce protection.
    /// Multiple requests within debounce window are coalesced.
    /// Called when a new package is loaded to refresh validation.
    /// </summary>
    /// <param name="packageId">NPM package identifier</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InvalidateCacheForPackageAsync(string packageId, int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates all caches for a tenant with debounce protection.
    /// Multiple requests within debounce window are coalesced.
    /// Used when package loading is completed (all packages refreshed).
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InvalidateCachesForTenantAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current debounce delay setting (for testing/monitoring).
    /// </summary>
    TimeSpan DebounceDelay { get; }
}
