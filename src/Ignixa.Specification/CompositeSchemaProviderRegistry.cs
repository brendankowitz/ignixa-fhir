// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Domain;
using Ignixa.Serialization;
using Microsoft.Extensions.Logging;

namespace Ignixa.Specification;

/// <summary>
/// Registry for managing composite schema provider instances and their cache invalidation.
/// Thread-safe singleton that tracks all active composite providers by tenant and FHIR version.
/// </summary>
public sealed class CompositeSchemaProviderRegistry : ICompositeSchemaProviderRegistry, IDisposable
{
    private readonly ConcurrentDictionary<(int tenantId, FhirSpecification version), CompositeStructureDefinitionSummaryProvider> _providers;
    private readonly DebounceInvalidationStrategy _debounceStrategy;
    private readonly ILogger<CompositeSchemaProviderRegistry> _logger;

    /// <summary>
    /// Gets the current debounce delay setting (for testing/monitoring).
    /// </summary>
    public TimeSpan DebounceDelay => _debounceStrategy.DebounceDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeSchemaProviderRegistry"/> class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="debounceDelay">Debounce delay for cache invalidation (default: 1 second)</param>
    public CompositeSchemaProviderRegistry(
        ILogger<CompositeSchemaProviderRegistry> logger,
        TimeSpan? debounceDelay = null)
    {
        _logger = logger;
        _providers = new ConcurrentDictionary<(int, FhirSpecification), CompositeStructureDefinitionSummaryProvider>();
        _debounceStrategy = new DebounceInvalidationStrategy(debounceDelay, _logger);
    }

    /// <summary>
    /// Registers a composite provider instance for a tenant.
    /// Called during validation setup to track active providers.
    /// </summary>
    public void RegisterProvider(int tenantId, IFhirSchemaProvider provider)
    {
        if (provider is not CompositeStructureDefinitionSummaryProvider compositeProvider)
        {
            _logger.LogWarning("Attempted to register non-composite provider for tenant {TenantId}", tenantId);
            return;
        }

        // Get version from provider
        var version = compositeProvider.Version;
        var key = (tenantId, version);

        _providers.AddOrUpdate(key, compositeProvider, (_, existing) =>
        {
            _logger.LogDebug("Updating composite provider for tenant {TenantId} version {Version}",
                tenantId, version);
            return compositeProvider;
        });

        _logger.LogDebug("Registered composite provider for tenant {TenantId} version {Version}",
            tenantId, compositeProvider.Version);
    }

    /// <summary>
    /// Invalidates cache for all instances of a loaded package with debounce protection.
    /// Multiple requests within debounce window are coalesced.
    /// Called when a new package is loaded to refresh validation.
    /// </summary>
    public Task InvalidateCacheForPackageAsync(string packageId, int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Requesting cache invalidation for package {PackageId} tenant {TenantId}",
            packageId, tenantId);

        // Use debounce strategy instead of directly invalidating
        _debounceStrategy.RequestInvalidation(
            tenantId,
            async () => await ExecuteInvalidationAsync(tenantId, cancellationToken),
            cancellationToken);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Invalidates all caches for a tenant with debounce protection.
    /// Multiple requests within debounce window are coalesced.
    /// Used when package loading is completed (all packages refreshed).
    /// </summary>
    public Task InvalidateCachesForTenantAsync(int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Requesting full cache invalidation for tenant {TenantId}", tenantId);

        // Use debounce strategy for full tenant invalidation
        _debounceStrategy.RequestInvalidation(
            tenantId,
            async () => await ExecuteInvalidationAsync(tenantId, cancellationToken),
            cancellationToken);

        return Task.CompletedTask;
    }

    private Task ExecuteInvalidationAsync(int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing cache invalidation for tenant {TenantId}", tenantId);

        var providersForTenant = _providers
            .Where(kvp => kvp.Key.tenantId == tenantId)
            .Select(kvp => kvp.Value)
            .ToList();

        foreach (var provider in providersForTenant)
        {
            provider.ClearCache();
            _logger.LogDebug("Cleared cache for {Version} provider (tenant {TenantId})",
                provider.Version, tenantId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the debounce strategy and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        _debounceStrategy?.Dispose();
        GC.SuppressFinalize(this);
    }
}
