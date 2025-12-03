// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain;
using Ignixa.Serialization;

namespace Ignixa.Application.Features.Metadata;

/// <summary>
/// Handler for retrieving the server's FHIR CapabilityStatement.
/// Uses CapabilityStatementService with segmented architecture and smart caching.
/// </summary>
public class GetCapabilityStatementHandler
    : IRequestHandler<GetCapabilityStatementQuery, CapabilityStatementJsonNode>
{
    private readonly CapabilityStatementService _service;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly ILogger<GetCapabilityStatementHandler> _logger;

    public GetCapabilityStatementHandler(
        CapabilityStatementService service,
        ITenantConfigurationStore tenantConfigStore,
        ILogger<GetCapabilityStatementHandler> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CapabilityStatementJsonNode> HandleAsync(
        GetCapabilityStatementQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Retrieving capability statement for TenantId={TenantId}",
            request.TenantId?.ToString() ?? "system-wide");

        // Determine FHIR version from tenant or default
        FhirVersion fhirVersion = FhirVersion.R4; // Default

        if (request.TenantId.HasValue)
        {
            var tenantConfig = await _tenantConfigStore.GetTenantConfigurationAsync(
                request.TenantId.Value,
                cancellationToken);

            if (tenantConfig != null)
            {
                fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            }
        }
        else
        {
            // System-wide: Use first active tenant's version, or default to R4
            var allTenants = await _tenantConfigStore.GetAllTenantsAsync(cancellationToken);
            if (allTenants.Count > 0)
            {
                fhirVersion = FhirSpecificationExtensions.FromVersionString(allTenants[0].FhirVersion);
            }
        }

        // Build capability context
        var context = new CapabilityContext(
            FhirVersion: fhirVersion,
            TenantId: request.TenantId);

        // Get capability statement (cached if available)
        var capability = await _service.GetCapabilityStatementAsync(context, cancellationToken);

        _logger.LogDebug(
            "Retrieved CapabilityStatement for tenant {TenantId} with FHIR version {FhirVersion}, {ResourceCount} resources",
            request.TenantId,
            capability.FhirVersion,
            capability.Rest?[0]?.Resource?.Count ?? 0);

        return capability;
    }
}
