// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Mcp.Dtos;

/// <summary>
/// DTO representing the result of listing available tenants.
/// Used by list_tenants_info MCP tool to help users discover available tenants.
/// </summary>
public class ListTenantsInfoResultDto
{
    /// <summary>
    /// System-wide tenant mode: "single-tenant" (Isolated) or "multi-tenant" (Distributed).
    /// In single-tenant mode, tenantId parameter can be omitted (auto-detected).
    /// In multi-tenant mode, tenantId parameter is required for FHIR operations.
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Total number of accessible tenants (excludes system partition 0).
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Collection of available tenant configurations.
    /// Only includes active, non-system tenants that can be accessed via API.
    /// </summary>
    public IReadOnlyList<TenantInfoDto> Tenants { get; init; } = Array.Empty<TenantInfoDto>();
}
