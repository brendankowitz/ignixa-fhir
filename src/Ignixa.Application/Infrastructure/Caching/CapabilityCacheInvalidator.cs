// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain;
using Ignixa.Serialization;

namespace Ignixa.Application.Infrastructure.Caching;

/// <summary>
/// Implementation of capability cache invalidation service.
/// Wraps CapabilityStatementService to provide semantic invalidation methods.
/// </summary>
public class CapabilityCacheInvalidator : ICapabilityCacheInvalidator
{
    private readonly CapabilityStatementService _capabilityService;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly ILogger<CapabilityCacheInvalidator> _logger;

    public CapabilityCacheInvalidator(
        CapabilityStatementService capabilityService,
        ITenantConfigurationStore tenantConfigStore,
        ILogger<CapabilityCacheInvalidator> logger)
    {
        _capabilityService = capabilityService ?? throw new ArgumentNullException(nameof(capabilityService));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask InvalidateForProfileChangesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Invalidating capability caches due to profile changes (IG load/unload)");

        // Invalidate all tenants since profiles may affect multiple tenants
        await InvalidateAllTenantsAsync(cancellationToken);

        _logger.LogInformation("Capability cache invalidation completed for profile changes");
    }

    public async ValueTask InvalidateForSearchParameterChangesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Invalidating capability caches due to custom search parameter registration");

        // Invalidate all tenants since search parameters may affect multiple tenants
        await InvalidateAllTenantsAsync(cancellationToken);

        _logger.LogInformation("Capability cache invalidation completed for search parameter changes");
    }

    public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Invalidating ALL capability caches (administrative operation)");

        await _capabilityService.ClearCacheAsync(cancellationToken);

        _logger.LogInformation("All capability caches cleared");
    }

    public async ValueTask InvalidateForTenantAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Invalidating capability cache for tenant {TenantId}", tenantId);

        var tenantConfig = await _tenantConfigStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
        if (tenantConfig == null)
        {
            _logger.LogWarning("Cannot invalidate cache for tenant {TenantId} - tenant not found", tenantId);
            return;
        }

        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var context = new CapabilityContext(FhirVersion: fhirVersion, TenantId: tenantId);

        await _capabilityService.InvalidateCacheAsync(context, cancellationToken);

        _logger.LogInformation("Capability cache invalidated for tenant {TenantId}", tenantId);
    }

    private async ValueTask InvalidateAllTenantsAsync(CancellationToken cancellationToken)
    {
        var tenants = await _tenantConfigStore.GetAllTenantsAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
            var context = new CapabilityContext(FhirVersion: fhirVersion, TenantId: tenant.TenantId);

            await _capabilityService.InvalidateCacheAsync(context, cancellationToken);
        }

        _logger.LogDebug("Invalidated capability caches for {Count} tenants", tenants.Count);
    }
}
