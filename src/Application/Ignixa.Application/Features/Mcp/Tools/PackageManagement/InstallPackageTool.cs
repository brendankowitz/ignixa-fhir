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
/// MCP tool for installing FHIR packages from the NPM registry.
/// Downloads, extracts, and imports package resources into the tenant's database.
/// </summary>
[McpServerToolType]
public class InstallPackageTool : TenantAwareMcpTool
{
    private readonly IImplementationGuideProvider _packageProvider;
    private readonly INpmPackageSearchService _searchService;

    public InstallPackageTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IImplementationGuideProvider packageProvider,
        INpmPackageSearchService searchService)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _packageProvider = packageProvider ?? throw new ArgumentNullException(nameof(packageProvider));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    [McpServerTool(Name = "install_fhir_package")]
    [Description(@"Install a FHIR package from the NPM registry into the tenant's database.
Downloads the package, extracts conformance resources, and imports them.
Use search_fhir_packages first to find the exact package ID and version.
Example: packageId='hl7.fhir.us.core', version='6.1.0'
Returns statistics about imported resources (counts, types, duration).
NOTE: This operation may take 30-60 seconds for large packages.")]
    public async Task<InstallPackageResultDto> InstallPackageAsync(
        [Description("Package ID (e.g., 'hl7.fhir.us.core')")]
        string packageId,

        [Description("Package version (e.g., '6.1.0'). Use 'latest' or omit to install the latest version.")]
        string? version = null,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be empty");
        }

        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Validate tenant access
        var tenantConfig = await ValidateTenantAccessAsync(resolvedTenantId, cancellationToken);

        // Resolve version if "latest" or null
        string resolvedVersion;
        if (string.IsNullOrWhiteSpace(version) || version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            var details = await _searchService.GetPackageDetailsAsync(packageId, cancellationToken);
            if (details == null)
            {
                throw new InvalidOperationException($"Package '{packageId}' not found in NPM registry");
            }

            resolvedVersion = details.LatestVersion
                ?? throw new InvalidOperationException($"No latest version available for package '{packageId}'");
        }
        else
        {
            resolvedVersion = version;
        }

        // Load package
        var startTime = DateTime.UtcNow;
        var result = await _packageProvider.LoadPackageAsync(
            tenantConfig.TenantId.ToString(),
            packageId,
            resolvedVersion,
            cancellationToken);

        var duration = DateTime.UtcNow - startTime;

        // Map to DTO
        var resourcesByType = result.ResourcesByType
            .OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return new InstallPackageResultDto
        {
            Success = true,
            TenantId = resolvedTenantId,
            TenantName = tenantConfig.DisplayName,
            PackageId = result.PackageId,
            PackageVersion = result.PackageVersion,
            TotalResources = result.TotalResources,
            ImportedResources = result.ImportedResources,
            UpdatedResources = result.UpdatedResources,
            DurationSeconds = (int)duration.TotalSeconds,
            ResourcesByType = resourcesByType,
            Message = $"Successfully installed {result.PackageId}@{result.PackageVersion} " +
                      $"({result.ImportedResources} new, {result.UpdatedResources} updated)"
        };
    }
}
