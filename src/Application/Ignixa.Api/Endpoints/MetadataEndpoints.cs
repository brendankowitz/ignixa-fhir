// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Api.Http;
using Ignixa.Application.Features.Metadata;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Serialization;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for FHIR metadata (CapabilityStatement).
/// Supports both tenant-agnostic (/metadata) and tenant-explicit (/tenant/{tenantId}/metadata) routes.
/// </summary>
public static class MetadataEndpoints
{
    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMetadataTenantEndpoints();
        endpoints.MapMetadataAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit FHIR metadata endpoints (/tenant/{tenantId}/metadata).
    /// Always supported in all multi-tenancy scenarios.
    /// </summary>
    public static IEndpointRouteBuilder MapMetadataTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-explicit route: GET /tenant/{tenantId}/metadata
        endpoints.MapGet("/tenant/{tenantId:int}/metadata", HandleGetTenantMetadata)
            .WithName("GetTenantMetadata")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR metadata endpoints (/metadata).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// </summary>
    public static IEndpointRouteBuilder MapMetadataAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-agnostic route: GET /metadata
        endpoints.MapGet("/metadata", HandleGetMetadata)
            .WithName("GetMetadata")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson);

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
        [FromServices] ITenantConfigurationStore configStore,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("GET /metadata (tenant-agnostic)");

        // Validate Accept header for content negotiation
        ValidateAcceptHeader(context);

        // Resolve tenant ID: For /metadata endpoint, the TenantResolutionMiddleware doesn't
        // auto-detect because it's not classified as a resource endpoint. We need to manually
        // perform the same auto-detection logic here.
        int? tenantId = null;

        // First check if middleware already resolved it (though unlikely for /metadata)
        if (context.Items.TryGetValue("TenantId", out var tenantIdObj) &&
            tenantIdObj is int resolvedTenantId)
        {
            tenantId = resolvedTenantId;
            logger.LogDebug("Tenant auto-detected by middleware: {TenantId}", tenantId);
        }

        var query = new GetCapabilityStatementQuery(tenantId);
        var capabilityStatement = await mediator.SendAsync(query, cancellationToken);

        return Results.Content(capabilityStatement.SerializeToString(), KnownContentTypes.ApplicationFhirJson);
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

        // Validate Accept header for content negotiation
        ValidateAcceptHeader(context);

        // TenantResolutionMiddleware already validated the tenant exists and is active
        // The tenantId is stored in HttpContext.Items

        var query = new GetCapabilityStatementQuery(tenantId);
        var capabilityStatement = await mediator.SendAsync(query, cancellationToken);

        return Results.Content(capabilityStatement.SerializeToString(), KnownContentTypes.ApplicationFhirJson);
    }

    /// <summary>
    /// Validates the Accept header for FHIR-compliant content negotiation.
    /// Throws NotAcceptableException if the Accept header specifies unsupported media types.
    /// </summary>
    private static void ValidateAcceptHeader(HttpContext context)
    {
        const string fhirJsonMediaType = KnownContentTypes.ApplicationFhirJson;
        const string jsonMediaType = KnownContentTypes.ApplicationJson;

        var acceptHeader = context.Request.Headers.Accept.ToString();

        if (string.IsNullOrWhiteSpace(acceptHeader))
        {
            // Accept header is optional; if not provided, default to KnownContentTypes.ApplicationFhirJson
            return;
        }

        // Parse Accept header values (may contain multiple values separated by comma)
        var acceptedTypes = acceptHeader.Split(',')
            .Select(t => t.Trim())
            .Select(t => t.Split(';')[0].Trim()) // Remove quality factors and parameters
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        // Check if any accepted type is supported
        bool hasAcceptableType = acceptedTypes.Any(t =>
            t.Equals(fhirJsonMediaType, StringComparison.OrdinalIgnoreCase) ||
            t.Equals(jsonMediaType, StringComparison.OrdinalIgnoreCase) ||
            t.Equals("*/*", StringComparison.OrdinalIgnoreCase));

        if (!hasAcceptableType)
        {
            // No acceptable media type found
            throw new NotAcceptableException(
                $"The server cannot produce content matching the requested Accept header: '{acceptHeader}'. " +
                $"Supported media types: KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson");
        }
    }
}
