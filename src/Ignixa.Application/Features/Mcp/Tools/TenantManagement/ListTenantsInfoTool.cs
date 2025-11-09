// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using ModelContextProtocol.Server;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Mcp.Tools.TenantManagement;

/// <summary>
/// MCP tool for listing available tenants with their configuration info.
/// Used for tenant discovery in multi-tenant FHIR setups where tenantId is required.
/// </summary>
[McpServerToolType]
public class ListTenantsInfoTool
{
    private readonly ITenantConfigurationStore _tenantConfigurationStore;

    public ListTenantsInfoTool(ITenantConfigurationStore tenantConfigurationStore)
    {
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
    }

    [McpServerTool(Name = "list_tenants_info")]
    [Description(@"List all available tenants with their id, name, mode, and description.
Use this to discover which tenants are available before making FHIR requests that require tenantId.
Returns only active, non-system tenants that can be accessed via API.")]
    public async Task<ListTenantsInfoResultDto> ListTenantsInfoAsync(
        CancellationToken cancellationToken = default)
    {
        // Get all active tenants (excludes system partition 0)
        var allTenants = await _tenantConfigurationStore.GetAllTenantsAsync(cancellationToken);
        var accessibleTenants = allTenants
            .Where(t => t.IsActive && !t.IsSystemPartition)
            .OrderBy(t => t.TenantId)
            .ToList();

        // Get system-wide tenant mode
        var mode = _tenantConfigurationStore.Mode;
        var modeDescription = mode switch
        {
            Domain.Models.TenantMode.Isolated => "single-tenant",
            Domain.Models.TenantMode.Distributed => "multi-tenant",
            _ => "unknown"
        };

        // Map to DTOs
        var tenantInfos = accessibleTenants.Select(t => new TenantInfoDto
        {
            Id = t.TenantId,
            Name = t.DisplayName,
            FhirVersion = t.FhirVersion,
            ValidationTier = t.ValidationTier,
            IsActive = t.IsActive,
            Description = $"Tenant {t.TenantId}: {t.DisplayName} (FHIR {t.FhirVersion}, Validation: {t.ValidationTier})"
        }).ToList();

        return new ListTenantsInfoResultDto
        {
            Mode = modeDescription,
            TotalCount = tenantInfos.Count,
            Tenants = tenantInfos
        };
    }
}
