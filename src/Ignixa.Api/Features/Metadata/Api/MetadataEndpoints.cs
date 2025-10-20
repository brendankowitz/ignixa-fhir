// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Serialization;
using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;

namespace Ignixa.Api.Features.Metadata.Api;

/// <summary>
/// Minimal API endpoints for FHIR metadata (CapabilityStatement).
/// Supports both tenant-agnostic (/metadata) and tenant-explicit (/tenant/{tenantId}/metadata) routes.
/// </summary>
public static class MetadataEndpoints
{
    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-agnostic route: GET /metadata
        endpoints.MapGet("/metadata", HandleGetMetadata)
            .WithName("GetMetadata")
            .Produces(StatusCodes.Status200OK, contentType: "application/fhir+json");

        // Tenant-explicit route: GET /tenant/{tenantId}/metadata
        endpoints.MapGet("/tenant/{tenantId:int}/metadata", HandleGetTenantMetadata)
            .WithName("GetTenantMetadata")
            .Produces(StatusCodes.Status200OK, contentType: "application/fhir+json")
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// GET /metadata
    /// Returns the FHIR server's capability statement (tenant-agnostic).
    /// In multi-tenant scenarios, returns system-wide capabilities.
    /// In single-tenant scenarios, returns the single tenant's capabilities.
    /// </summary>
    private static async Task<IResult> HandleGetMetadata(
        HttpContext context,
        [FromServices] IMediator mediator,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("GET /metadata (tenant-agnostic)");

        // Check if TenantId was resolved by TenantResolutionMiddleware (single-tenant auto-detect)
        int? tenantId = null;
        if (context.Items.TryGetValue("TenantId", out var tenantIdObj) &&
            tenantIdObj is int resolvedTenantId)
        {
            tenantId = resolvedTenantId;
            logger.LogDebug("Tenant auto-detected: {TenantId}", tenantId);
        }

        var query = new GetCapabilityStatementQuery(tenantId);
        var capabilityStatement = await mediator.SendAsync(query, cancellationToken);

        // Extract FHIR version from CapabilityStatement and use version-aware serialization
        var fhirVersionString = capabilityStatement.FhirVersion?.ToVersionString() ?? "4.0";
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(fhirVersionString);
        var serializerOptions = CapabilityStatementSerializerOptions.Create(fhirVersion);

        return Results.Json(capabilityStatement, serializerOptions, "application/fhir+json");
    }

    /// <summary>
    /// GET /tenant/{tenantId}/metadata
    /// Returns the FHIR server's capability statement for a specific tenant.
    /// </summary>
    private static async Task<IResult> HandleGetTenantMetadata(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("GET /tenant/{TenantId}/metadata", tenantId);

        // TenantResolutionMiddleware already validated the tenant exists and is active
        // The tenantId is stored in HttpContext.Items

        var query = new GetCapabilityStatementQuery(tenantId);
        var capabilityStatement = await mediator.SendAsync(query, cancellationToken);

        // Extract FHIR version from CapabilityStatement and use version-aware serialization
        var fhirVersionString = capabilityStatement.FhirVersion?.ToVersionString() ?? "4.0";
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(fhirVersionString);
        var serializerOptions = CapabilityStatementSerializerOptions.Create(fhirVersion);

        return Results.Json(capabilityStatement, serializerOptions, "application/fhir+json");
    }
}
