// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Api.Extensions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.Features.ConditionalOperations.ConditionalPatch;
using Ignixa.Application.Features.Patch;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Registers FHIR PATCH endpoints using FHIRPath Patch (Parameters resource).
/// Supports both direct PATCH by ID and conditional PATCH via query parameters.
/// </summary>
public static class PatchEndpoints
{

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
    /// All routes validate resource type against tenant's FHIR version via ResourceTypeValidationFilter.
    /// </summary>
    public static IEndpointRouteBuilder MapPatchTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-EXPLICIT ROUTES (always supported)
        // Create a route group with the filter applied to all endpoints
        var tenantGroup = endpoints
            .MapGroup("/tenant/{tenantId:int}")
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        // PATCH /{resourceType} - Conditional Patch
        // IMPORTANT: Must be registered BEFORE PATCH /{resourceType}/{id} to match correctly
        tenantGroup.MapPatch("/{resourceType}", (HttpContext context, int tenantId, string resourceType,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager, CancellationToken ct) =>
            HandleConditionalPatchResourceExplicit(context, tenantId, resourceType, mediator, memoryStreamManager, ct))
            .WithName("ConditionalPatchResourceExplicit")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, KnownContentTypes.ApplicationFhirJson);

        // PATCH /{resourceType}/{id} - Direct Patch
        tenantGroup.MapPatch("/{resourceType}/{id}", (HttpContext context, int tenantId, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager, [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandlePatchResource(context, tenantId, resourceType, id, mediator, memoryStreamManager, logger, ct))
            .WithName("PatchResource")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR PATCH endpoints (/{resourceType}/...).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// All routes validate resource type against tenant's FHIR version via ResourceTypeValidationFilter.
    /// </summary>
    public static IEndpointRouteBuilder MapPatchAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-AGNOSTIC ROUTES (single-tenant auto-detect)
        // Create a route group with the filter applied to all endpoints
        var agnosticGroup = endpoints
            .MapGroup(string.Empty)
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        // PATCH /{resourceType} - Conditional Patch (agnostic)
        // IMPORTANT: Must be registered BEFORE PATCH /{resourceType}/{id} to match correctly
        agnosticGroup.MapPatch("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager, CancellationToken ct) =>
            HandleConditionalPatchResource(context, resourceType, mediator, memoryStreamManager, ct))
            .WithName("ConditionalPatchResourceAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest);

        // PATCH /{resourceType}/{id} - Direct Patch (agnostic)
        agnosticGroup.MapPatch("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager, [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandlePatchResource(context, context.GetTenantId(), resourceType, id, mediator, memoryStreamManager, logger, ct))
            .WithName("PatchResourceAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
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
        RecyclableMemoryStreamManager memoryStreamManager,
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

        // Parse request body to ResourceJsonNode (Parameters resource with patch operations)
        ResourceJsonNode patchDocument;
        await using (var memoryStream = memoryStreamManager.GetStream("patch-request"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            patchDocument = await JsonSourceNodeFactory.Parse(memoryStream);
        }

        // Execute patch via mediator
        var command = new PatchResourceCommand(
            tenantId,
            resourceType,
            id,
            patchDocument);

        var result = await mediator.SendAsync(command, cancellationToken);

        if (result == null)
        {
            logger.LogInformation("Resource {ResourceType}/{Id} not found for patch", resourceType, id);
            return Results.NotFound();
        }

        // Return 200 OK with patched resource
        var resourceJson = result.Resource.SerializeToString();
        var resourceBytes = Encoding.UTF8.GetBytes(resourceJson);
        logger.LogInformation("Patched {ResourceType}/{Id} (version {VersionId})", resourceType, id, result.VersionId);
        return FhirResults.Ok(resourceBytes)
            .WithETag(result.VersionId)
            .WithLastModified(result.LastModified);
    }

    /// <summary>
    /// PATCH /{resourceType} - Conditional Patch (tenant-agnostic)
    /// Delegates to tenant-explicit handler with extracted tenant ID.
    /// </summary>
    private static async Task<IResult> HandleConditionalPatchResource(
        HttpContext context,
        string resourceType,
        IMediator mediator,
        RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        return await HandleConditionalPatchResourceExplicit(context, tenantId, resourceType, mediator, memoryStreamManager, cancellationToken);
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
        RecyclableMemoryStreamManager memoryStreamManager,
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

        // Parse request body to ResourceJsonNode (Parameters resource with patch operations)
        ResourceJsonNode patchDocument;
        await using (var memoryStream = memoryStreamManager.GetStream("conditional-patch-request"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            patchDocument = await JsonSourceNodeFactory.Parse(memoryStream);
        }

        // Execute conditional patch
        var command = new ConditionalPatchCommand(
            tenantId,
            resourceType,
            searchCriteria,
            patchDocument,
            context.TraceIdentifier);

        var result = await mediator.SendAsync(command, cancellationToken);

        // Return 200 OK with patched resource (ConditionalPatchResult.Resource is ResourceWrapper)
        var resourceJson = result.Resource.Resource.SerializeToString();
        var resourceBytes = Encoding.UTF8.GetBytes(resourceJson);
        return FhirResults.Ok(resourceBytes)
            .WithETag(result.Resource.VersionId)
            .WithLastModified(result.Resource.LastModified);
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

}
