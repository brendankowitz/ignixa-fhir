// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Mcp.Dtos;

/// <summary>
/// DTO representing information about a single tenant.
/// Used in list_tenants_info MCP tool responses.
/// </summary>
public class TenantInfoDto
{
    /// <summary>
    /// Unique tenant identifier (1, 2, 3, ...).
    /// Note: TenantId 0 is reserved for system operations and not included in this list.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Display name for the tenant (e.g., "Mayo Clinic", "Default Tenant").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// FHIR version for this tenant (e.g., "4.0" for R4, "5.0" for R5).
    /// </summary>
    public required string FhirVersion { get; init; }

    /// <summary>
    /// Validation tier for this tenant (None, Fast, Spec, Profile).
    /// Indicates the level of FHIR validation applied to resources.
    /// </summary>
    public required string ValidationTier { get; init; }

    /// <summary>
    /// Whether this tenant is active and accepting requests.
    /// Only active tenants are returned by list_tenants_info.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Human-readable description of the tenant including key configuration details.
    /// </summary>
    public required string Description { get; init; }
}
