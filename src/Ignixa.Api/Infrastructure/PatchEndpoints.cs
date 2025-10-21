// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Application.Features.ConditionalOperations.ConditionalPatch;
using Ignixa.Application.Features.Patch;
using Ignixa.SourceNodeSerialization;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Registers FHIR PATCH endpoints using FHIRPath Patch (Parameters resource).
/// Supports both direct PATCH by ID and conditional PATCH via query parameters.
/// </summary>
public static class PatchEndpoints
{
    private const string ContentTypeApplicationFhirJson = "application/fhir+json";
    private const string ContentTypeApplicationJson = "application/json";

    /// <summary>
    /// Registers FHIR PATCH endpoints.
    ///
    /// Route Patterns:
    /// 1. Tenant-explicit:
    ///    - PATCH /tenant/{tenantId:int}/{resourceType} - Conditional Patch
    ///    - PATCH /tenant/{tenantId:int}/{resourceType}/{id} - Direct Patch
    /// 2. Tenant-agnostic:
    ///    - PATCH /{resourceType} - Conditional Patch (single-tenant auto-detect)
    ///    - PATCH /{resourceType}/{id} - Direct Patch (single-tenant auto-detect)
    ///
    /// Request Body: Parameters resource (FHIRPath Patch operations)
    /// Content-Type: application/fhir+json
    /// </summary>
    public static IEndpointRouteBuilder MapPatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPatchTenantEndpoints();
        endpoints.MapPatchAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit FHIR PATCH endpoints (/tenant/{tenantId}/...).
    /// Always supported in all multi-tenancy scenarios.
    /// </summary>
    public static IEndpointRouteBuilder MapPatchTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-EXPLICIT ROUTES (always supported)

        // PATCH /tenant/{tenantId:int}/{resourceType} - Conditional Patch
        // IMPORTANT: Must be registered BEFORE PATCH /{resourceType}/{id} to match correctly
        endpoints.MapPatch("/tenant/{tenantId:int}/{resourceType}", HandleConditionalPatchResourceExplicit)
            .WithName("ConditionalPatchResourceExplicit")
            .Accepts<object>(ContentTypeApplicationFhirJson, ContentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, ContentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, ContentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, ContentTypeApplicationFhirJson);

        // PATCH /tenant/{tenantId:int}/{resourceType}/{id} - Direct Patch
        endpoints.MapPatch("/tenant/{tenantId:int}/{resourceType}/{id}", HandlePatchResource)
            .WithName("PatchResource")
            .Accepts<object>(ContentTypeApplicationFhirJson, ContentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, ContentTypeApplicationFhirJson)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR PATCH endpoints (/{resourceType}/...).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// </summary>
    public static IEndpointRouteBuilder MapPatchAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-AGNOSTIC ROUTES (single-tenant auto-detect)

        // PATCH /{resourceType} - Conditional Patch (agnostic)
        // IMPORTANT: Must be registered BEFORE PATCH /{resourceType}/{id} to match correctly
        endpoints.MapPatch("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, CancellationToken ct) =>
            HandleConditionalPatchResource(context, resourceType, mediator, ct))
            .WithName("ConditionalPatchResourceAgnostic")
            .Accepts<object>(ContentTypeApplicationFhirJson, ContentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, ContentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, ContentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, ContentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest);

        // PATCH /{resourceType}/{id} - Direct Patch (agnostic)
        endpoints.MapPatch("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandlePatchResource(context, ExtractTenantId(context), resourceType, id, mediator, logger, ct))
            .WithName("PatchResourceAgnostic")
            .Accepts<object>(ContentTypeApplicationFhirJson, ContentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, ContentTypeApplicationFhirJson)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    /// <summary>
    /// PATCH /tenant/{tenantId:int}/{resourceType}/{id} or PATCH /{resourceType}/{id}
    /// Patches a specific resource by ID using FHIR Parameters patch operations.
    /// </summary>
    private static async Task<IResult> HandlePatchResource(
        HttpContext context,
        int tenantId,
        string resourceType,
        string id,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("PATCH /tenant/{TenantId}/{ResourceType}/{Id}", tenantId, resourceType, id);

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.NotFound(new { error = $"Resource type '{resourceType}' not supported" });
        }

        // Read request body (Parameters resource with patch operations)
        using var memoryStream = new MemoryStream();
        await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        var patchBody = Encoding.UTF8.GetString(memoryStream.ToArray());

        // Execute patch via mediator
        var command = new PatchResourceCommand(
            tenantId,
            resourceType,
            id,
            patchBody);

        var result = await mediator.SendAsync(command, cancellationToken);

        if (result == null)
        {
            logger.LogInformation("Resource {ResourceType}/{Id} not found for patch", resourceType, id);
            return Results.NotFound();
        }

        // Add ETag header
        context.Response.Headers.Append("ETag", $"W/\"{result.VersionId}\"");

        // Return 200 OK with patched resource
        var resourceJson = result.Resource.SerializeToString();
        logger.LogInformation("Patched {ResourceType}/{Id} (version {VersionId})", resourceType, id, result.VersionId);
        return Results.Content(resourceJson, ContentTypeApplicationFhirJson, statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// PATCH /{resourceType} - Conditional Patch (tenant-agnostic)
    /// Delegates to tenant-explicit handler with extracted tenant ID.
    /// </summary>
    private static async Task<IResult> HandleConditionalPatchResource(
        HttpContext context,
        string resourceType,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(context);
        return await HandleConditionalPatchResourceExplicit(context, tenantId, resourceType, mediator, cancellationToken);
    }

    /// <summary>
    /// PATCH /tenant/{tenantId:int}/{resourceType} - Conditional Patch (tenant-explicit)
    /// Patches resource based on query string parameters.
    /// - 0 matches: 404 Not Found (different from conditional update!)
    /// - 1 match: Patch existing resource (200 OK)
    /// - Multiple matches: 412 Precondition Failed
    /// </summary>
    private static async Task<IResult> HandleConditionalPatchResourceExplicit(
        HttpContext context,
        int tenantId,
        string resourceType,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Extract query string (search criteria)
        var queryString = context.Request.QueryString.Value;

        if (string.IsNullOrWhiteSpace(queryString) || queryString == "?")
        {
            return Results.BadRequest(new
            {
                error = "Conditional patch requires search parameters in query string"
            });
        }

        // Remove leading '?'
        var searchCriteria = queryString.TrimStart('?');

        // Read request body (Parameters resource with patch operations)
        using var memoryStream = new MemoryStream();
        await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        var patchBody = Encoding.UTF8.GetString(memoryStream.ToArray());

        // Execute conditional patch
        var command = new ConditionalPatchCommand(
            tenantId,
            resourceType,
            searchCriteria,
            patchBody,
            context.TraceIdentifier);

        var result = await mediator.SendAsync(command, cancellationToken);

        // Return 200 OK with patched resource (ConditionalPatchResult.Resource is ResourceWrapper)
        var resourceJson = result.Resource.Resource.SerializeToString();
        return Results.Content(resourceJson, ContentTypeApplicationFhirJson, statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// Validates resource type against capability statement or schema provider.
    /// For now, returns true for all resource types (will implement proper validation later).
    /// </summary>
    private static bool IsValidResourceType(string resourceType, HttpContext context)
    {
        // TODO: Implement proper validation using IFhirSchemaProvider or ICapabilityStatementService
        // For now, accept all resource types to support dynamic routing
        return true;
    }

    /// <summary>
    /// Extracts tenant ID from HttpContext.Items (populated by TenantResolutionMiddleware).
    /// Throws if tenant ID not found - should never happen if middleware ran successfully.
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
}
