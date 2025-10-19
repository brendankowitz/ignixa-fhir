// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Context for capability statement generation.
/// Represents the scope for which capabilities are being requested (FHIR version, tenant, etc.).
/// Used as cache key discriminator and passed to segments for context-aware generation.
/// </summary>
/// <param name="FhirVersion">FHIR specification version (R4, R4B, R5, STU3).</param>
/// <param name="TenantId">Optional tenant identifier for multi-tenant scenarios. Null = system-wide or single-tenant.</param>
/// <param name="IncludeExperimental">Whether to include experimental features in capability statement.</param>
public record CapabilityContext(
    FhirSpecification FhirVersion,
    int? TenantId = null,
    bool IncludeExperimental = false)
{
    /// <summary>
    /// Builds a cache key from this context.
    /// Format: "capability:{fhirVersion}:{tenantId}"
    /// </summary>
    public string ToCacheKey()
    {
        var tenantPart = TenantId.HasValue ? TenantId.Value.ToString() : "default";
        return $"capability:{FhirVersion}:{tenantPart}";
    }
}
