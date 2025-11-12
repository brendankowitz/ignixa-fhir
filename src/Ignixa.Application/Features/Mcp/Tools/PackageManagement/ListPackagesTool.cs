// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using Ignixa.Domain.Abstractions;
using Ignixa.PackageManagement.Abstractions;

namespace Ignixa.Application.Features.Mcp.Tools.PackageManagement;

/// <summary>
/// MCP tool for listing installed FHIR packages.
/// Shows all packages currently loaded in the tenant's database.
/// </summary>
[McpServerToolType]
public class ListPackagesTool : TenantAwareMcpTool
{
    private readonly IImplementationGuideProvider _packageProvider;

    public ListPackagesTool(
        IHttpContextAccessor httpContextAccessor,
        ITenantConfigurationStore tenantStore,
        IImplementationGuideProvider packageProvider)
        : base(httpContextAccessor, tenantStore)
    {
        _packageProvider = packageProvider ?? throw new ArgumentNullException(nameof(packageProvider));
    }

    [McpServerTool(Name = "list_fhir_packages")]
    [Description(@"List all FHIR packages currently installed/loaded in the tenant's database.
Shows package ID and version for each loaded package.
Use this to see what packages are already available before installing new ones.
Example response: [{ packageId: 'hl7.fhir.us.core', version: '6.1.0' }]")]
    public async Task<ListPackagesResultDto> ListPackagesAsync(
        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Validate tenant access
        var tenantConfig = await ValidateTenantAccessAsync(resolvedTenantId, cancellationToken);

        // List loaded packages
        var packages = await _packageProvider.ListLoadedPackagesAsync(
            tenantConfig.TenantId.ToString(),
            cancellationToken);

        var packageDtos = packages
            .Select(p => new PackageInfoDto
            {
                PackageId = p.PackageId,
                Version = p.Version
            })
            .OrderBy(p => p.PackageId)
            .ToList();

        return new ListPackagesResultDto
        {
            TenantId = resolvedTenantId,
            TenantName = tenantConfig.DisplayName,
            TotalCount = packageDtos.Count,
            Packages = packageDtos
        };
    }
}

/// <summary>
/// Result of listing packages.
/// </summary>
public record ListPackagesResultDto
{
    /// <summary>
    /// Tenant ID that was queried.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Tenant name.
    /// </summary>
    public required string TenantName { get; init; }

    /// <summary>
    /// Total number of packages loaded.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// List of loaded packages.
    /// </summary>
    public required IReadOnlyList<PackageInfoDto> Packages { get; init; }
}

/// <summary>
/// Information about a loaded package.
/// </summary>
public record PackageInfoDto
{
    /// <summary>
    /// Package ID (e.g., "hl7.fhir.us.core").
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Package version (e.g., "6.1.0").
    /// </summary>
    public required string Version { get; init; }
}
