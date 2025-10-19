// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Serialization;
using Ignixa.Domain;

namespace Ignixa.Api.Features.Metadata.Api;

/// <summary>
/// Controller for FHIR metadata endpoints (CapabilityStatement).
/// Supports both tenant-agnostic (/metadata) and tenant-explicit (/tenant/{tenantId}/metadata) routes.
/// </summary>
[ApiController]
public class MetadataController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<MetadataController> _logger;

    public MetadataController(
        IMediator mediator,
        ILogger<MetadataController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// GET /metadata
    /// Returns the FHIR server's capability statement (tenant-agnostic).
    /// In multi-tenant scenarios, returns system-wide capabilities.
    /// In single-tenant scenarios, returns the single tenant's capabilities.
    /// </summary>
    [HttpGet("metadata")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMetadata(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GET /metadata (tenant-agnostic)");

        // Check if TenantId was resolved by TenantResolutionMiddleware (single-tenant auto-detect)
        int? tenantId = null;
        if (HttpContext.Items.TryGetValue("TenantId", out var tenantIdObj) &&
            tenantIdObj is int resolvedTenantId)
        {
            tenantId = resolvedTenantId;
            _logger.LogDebug("Tenant auto-detected: {TenantId}", tenantId);
        }

        var query = new GetCapabilityStatementQuery(tenantId);
        var capabilityStatement = await _mediator.SendAsync(query, cancellationToken);

        // Extract FHIR version from CapabilityStatement and use version-aware serialization
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(capabilityStatement.FhirVersion ?? "4.0");
        var serializerOptions = CapabilityStatementSerializerOptions.Create(fhirVersion);

        return new JsonResult(capabilityStatement, serializerOptions)
        {
            ContentType = "application/fhir+json",
        };
    }

    /// <summary>
    /// GET /tenant/{tenantId}/metadata
    /// Returns the FHIR server's capability statement for a specific tenant.
    /// </summary>
    [HttpGet("tenant/{tenantId:int}/metadata")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantMetadata(
        int tenantId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("GET /tenant/{TenantId}/metadata", tenantId);

        // TenantResolutionMiddleware already validated the tenant exists and is active
        // The tenantId is stored in HttpContext.Items

        var query = new GetCapabilityStatementQuery(tenantId);
        var capabilityStatement = await _mediator.SendAsync(query, cancellationToken);

        // Extract FHIR version from CapabilityStatement and use version-aware serialization
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(capabilityStatement.FhirVersion ?? "4.0");
        var serializerOptions = CapabilityStatementSerializerOptions.Create(fhirVersion);

        return new JsonResult(capabilityStatement, serializerOptions)
        {
            ContentType = "application/fhir+json",
        };
    }
}
