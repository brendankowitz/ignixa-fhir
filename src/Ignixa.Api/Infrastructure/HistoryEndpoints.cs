// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.History;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Registers FHIR _history endpoints for version history retrieval.
/// Supports instance-level, type-level, and system-level history operations.
/// </summary>
public static class HistoryEndpoints
{
    private const string ContentTypeApplicationFhirJson = "application/fhir+json";

    /// <summary>
    /// Registers FHIR _history endpoints.
    ///
    /// Route Patterns:
    /// 1. Instance-level: GET /{resourceType}/{id}/_history (all versions of a resource)
    /// 2. Type-level: GET /{resourceType}/_history (all versions of all resources of a type)
    /// 3. System-level: GET /_history (all versions across all resource types)
    ///
    /// Supports both tenant-explicit and tenant-agnostic routes (same as FhirEndpoints).
    /// </summary>
    public static IEndpointRouteBuilder MapFhirHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFhirHistoryTenantEndpoints();
        endpoints.MapFhirHistoryAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit FHIR _history endpoints (/tenant/{tenantId}/.../_history).
    /// Always supported in all multi-tenancy scenarios.
    /// </summary>
    public static IEndpointRouteBuilder MapFhirHistoryTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-EXPLICIT ROUTES (always supported)

        // GET /tenant/{tenantId:int}/{resourceType}/{id}/_history - Instance-level history
        endpoints.MapGet("/tenant/{tenantId:int}/{resourceType}/{id}/_history", HandleGetResourceHistory)
            .WithName("GetResourceHistory")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // GET /tenant/{tenantId:int}/{resourceType}/_history - Type-level history
        endpoints.MapGet("/tenant/{tenantId:int}/{resourceType}/_history", HandleGetTypeHistory)
            .WithName("GetTypeHistory")
            .Produces(StatusCodes.Status200OK);

        // GET /tenant/{tenantId:int}/_history - System-level history
        endpoints.MapGet("/tenant/{tenantId:int}/_history", HandleGetSystemHistory)
            .WithName("GetSystemHistory")
            .Produces(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR _history endpoints (/.../_history).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// </summary>
    public static IEndpointRouteBuilder MapFhirHistoryAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-AGNOSTIC ROUTES (FHIR-compliant, single-tenant auto-detection)

        // GET /{resourceType}/{id}/_history - Instance-level history (agnostic)
        endpoints.MapGet("/{resourceType}/{id}/_history", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, CancellationToken ct) =>
            HandleGetResourceHistory(context, ExtractTenantId(context), resourceType, id, mediator, ct))
            .WithName("GetResourceHistoryAgnostic")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        // GET /{resourceType}/_history - Type-level history (agnostic)
        endpoints.MapGet("/{resourceType}/_history", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, CancellationToken ct) =>
            HandleGetTypeHistory(context, ExtractTenantId(context), resourceType, mediator, ct))
            .WithName("GetTypeHistoryAgnostic")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        // GET /_history - System-level history (agnostic)
        endpoints.MapGet("/_history", (HttpContext context,
            [FromServices] IMediator mediator, CancellationToken ct) =>
            HandleGetSystemHistory(context, ExtractTenantId(context), mediator, ct))
            .WithName("GetSystemHistoryAgnostic")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    /// <summary>
    /// Extracts tenant ID from HttpContext.Items (populated by TenantResolutionMiddleware).
    /// </summary>
    private static int ExtractTenantId(HttpContext context)
    {
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            throw new InvalidOperationException(
                "TenantId not found in HttpContext.Items. TenantResolutionMiddleware may not have run.");
        }
        return tenantId;
    }

    /// <summary>
    /// GET /tenant/{tenantId:int}/{resourceType}/{id}/_history
    /// Instance-level history: Returns all versions of a specific resource.
    /// Uses streaming serialization for efficient memory usage.
    /// </summary>
    private static async Task<IResult> HandleGetResourceHistory(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromRoute] string id,
        [FromServices] IMediator mediator,
        CancellationToken ct)
    {
        // Parse query parameters
        var parameters = HistoryQueryParametersParser.Parse(context.Request.Query);

        // Build URLs for pagination
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var requestPath = $"/tenant/{tenantId}/{resourceType}/{id}/_history";

        // Create query
        var query = new GetResourceHistoryQuery(
            resourceType,
            id,
            tenantId,
            parameters,
            baseUrl,
            requestPath);

        // Execute query (returns streaming result)
        var result = await mediator.SendAsync(query, ct);

        // Set response content type
        context.Response.ContentType = ContentTypeApplicationFhirJson + "; charset=utf-8";

        // Stream history bundle directly to response
        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream: context.Response.Body,
            bundleType: "history",
            total: result.TotalCount,
            entries: result.Entries,
            links: result.Links,
            pretty: false,
            cancellationToken: ct);

        // Response already written to stream
        return Results.Empty;
    }

    /// <summary>
    /// GET /tenant/{tenantId:int}/{resourceType}/_history
    /// Type-level history: Returns all versions of all resources of a given type.
    /// Uses streaming serialization for efficient memory usage.
    /// </summary>
    private static async Task<IResult> HandleGetTypeHistory(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        CancellationToken ct)
    {
        // Parse query parameters
        var parameters = HistoryQueryParametersParser.Parse(context.Request.Query);

        // Build URLs for pagination
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var requestPath = $"/tenant/{tenantId}/{resourceType}/_history";

        // Create query
        var query = new GetTypeHistoryQuery(
            resourceType,
            tenantId,
            parameters,
            baseUrl,
            requestPath);

        // Execute query (returns streaming result)
        var result = await mediator.SendAsync(query, ct);

        // Set response content type
        context.Response.ContentType = ContentTypeApplicationFhirJson + "; charset=utf-8";

        // Stream history bundle directly to response
        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream: context.Response.Body,
            bundleType: "history",
            total: result.TotalCount,
            entries: result.Entries,
            links: result.Links,
            pretty: false,
            cancellationToken: ct);

        // Response already written to stream
        return Results.Empty;
    }

    /// <summary>
    /// GET /tenant/{tenantId:int}/_history
    /// System-level history: Returns all versions across all resource types.
    /// Uses streaming serialization for efficient memory usage.
    /// </summary>
    private static async Task<IResult> HandleGetSystemHistory(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromServices] IMediator mediator,
        CancellationToken ct)
    {
        // Parse query parameters
        var parameters = HistoryQueryParametersParser.Parse(context.Request.Query);

        // Build URLs for pagination
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var requestPath = $"/tenant/{tenantId}/_history";

        // Create query
        var query = new GetSystemHistoryQuery(
            tenantId,
            parameters,
            baseUrl,
            requestPath);

        // Execute query (returns streaming result)
        var result = await mediator.SendAsync(query, ct);

        // Set response content type
        context.Response.ContentType = ContentTypeApplicationFhirJson + "; charset=utf-8";

        // Stream history bundle directly to response
        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream: context.Response.Body,
            bundleType: "history",
            total: result.TotalCount,
            entries: result.Entries,
            links: result.Links,
            pretty: false,
            cancellationToken: ct);

        // Response already written to stream
        return Results.Empty;
    }
}
