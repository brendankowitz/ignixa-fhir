// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using ModelContextProtocol.Server;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.PackageManagement.Abstractions;

namespace Ignixa.Application.Features.Mcp.Tools.PackageManagement;

/// <summary>
/// MCP tool for uninstalling FHIR packages.
/// Deactivates package resources making them unavailable for validation and operations.
/// Note: Does not delete data, only deactivates (soft delete).
/// </summary>
[McpServerToolType]
public class UninstallPackageTool : TenantAwareMcpTool
{
    private readonly IImplementationGuideProvider _packageProvider;

    public UninstallPackageTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IImplementationGuideProvider packageProvider)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _packageProvider = packageProvider ?? throw new ArgumentNullException(nameof(packageProvider));
    }

    [McpServerTool(Name = "uninstall_fhir_package")]
    [Description(@"Uninstall (deactivate) a FHIR package from the tenant's database.
Makes package resources unavailable for validation and operations.
NOTE: This performs a soft delete - data is not physically removed.
Use list_fhir_packages to see installed packages.
Example: packageId='hl7.fhir.us.core', version='6.1.0'
Returns the number of resources deactivated.")]
    public async Task<UninstallPackageResultDto> UninstallPackageAsync(
        [Description("Package ID (e.g., 'hl7.fhir.us.core')")]
        string packageId,

        [Description("Package version (e.g., '6.1.0')")]
        string version,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Version cannot be empty");
        }

        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Validate tenant access
        var tenantConfig = await ValidateTenantAccessAsync(resolvedTenantId, cancellationToken);

        // Unload package
        var deactivatedCount = await _packageProvider.UnloadPackageAsync(
            tenantConfig.TenantId.ToString(),
            packageId,
            version,
            cancellationToken);

        return new UninstallPackageResultDto
        {
            Success = true,
            TenantId = resolvedTenantId,
            TenantName = tenantConfig.DisplayName,
            PackageId = packageId,
            PackageVersion = version,
            DeactivatedResources = deactivatedCount,
            Message = deactivatedCount > 0
                ? $"Successfully uninstalled {packageId}@{version} ({deactivatedCount} resources deactivated)"
                : $"Package {packageId}@{version} was not found or already uninstalled"
        };
    }
}
