// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure;
using Ignixa.Abstractions;
using Ignixa.Api.Extensions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Api.Infrastructure;
using Ignixa.Application.Features;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.ConditionalOperations;
using Ignixa.Application.Features.ConditionalOperations.ConditionalDelete;
using Ignixa.Application.Features.ConditionalOperations.ConditionalRead;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Utilities;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IO;
using DeferredWriteCoordinator = Ignixa.Application.Features.Bundle.DeferredWriteCoordinator;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Registers FHIR RESTful endpoints for all resource types.
/// No controllers, no switch statements - pure endpoint routing.
/// </summary>
public static class FhirEndpoints
{
    /// <summary>
    /// Registers FHIR RESTful endpoints for all resource types.
    ///
    /// Route Patterns:
    /// 1. Tenant-explicit: /tenant/{tenantId:int}/{resourceType}/{id?} - Always supported
    /// 2. Tenant-agnostic: /{resourceType}/{id?} - Auto-enabled for single-tenant scenarios
    ///
    /// Single-Tenant Mode (1 active tenant):
    ///   - Both /tenant/1/Patient/123 AND /Patient/123 work
    ///   - Middleware auto-detects and applies the single tenant
    ///
    /// Multi-Tenant Mode (2+ active tenants):
    ///   - Only /tenant/{id}/Patient/123 works
    ///   - Agnostic routes blocked by middleware (400 Bad Request)
    ///
    /// Distributed Mode (future):
    ///   - Agnostic routes work with transparent sharding
    /// </summary>
    public static IEndpointRouteBuilder MapFhirEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFhirTenantEndpoints();
        endpoints.MapFhirAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit FHIR endpoints (/tenant/{tenantId}/...).
    /// Always supported in all multi-tenancy scenarios.
    /// All routes within this group validate resource type against tenant's FHIR version via ResourceTypeValidationFilter.
    /// </summary>
    public static IEndpointRouteBuilder MapFhirTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-EXPLICIT ROUTES (always supported, all scenarios)
        // Create a route group with the filter applied to all endpoints
        var tenantGroup = endpoints
            .MapGroup("/tenant/{tenantId:int}")
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>()
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        // GET /{resourceType}/{id} - Read resource
        tenantGroup.MapGet("/{resourceType}/{id}", HandleGetResource)
            .WithName("GetResource")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status404NotFound);

        // PUT /{resourceType} - Conditional Update (no ID in URL, uses query string)
        // IMPORTANT: Must be registered BEFORE PUT /{resourceType}/{id} to match correctly
        tenantGroup.MapPut("/{resourceType}", (HttpContext context, int tenantId, string resourceType,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
            [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleConditionalUpdateResourceExplicit(context, tenantId, resourceType, mediator, memoryStreamManager, loggerFactory, ct))
            .WithName("ConditionalUpdateResourceExplicit")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status201Created, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, KnownContentTypes.ApplicationFhirJson);

        // PUT /{resourceType}/{id} - Create or update resource
        tenantGroup.MapPut("/{resourceType}/{id}", HandlePutResource)
            .WithName("PutResource")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created);

        // DELETE /{resourceType} - Conditional Delete (no ID in URL, uses query string)
        // IMPORTANT: Must be registered BEFORE DELETE /{resourceType}/{id} to match correctly
        tenantGroup.MapDelete("/{resourceType}", HandleConditionalDeleteResourceExplicit)
            .WithName("ConditionalDeleteResourceExplicit")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, KnownContentTypes.ApplicationFhirJson);

        // DELETE /{resourceType}/{id} - Delete resource
        tenantGroup.MapDelete("/{resourceType}/{id}", HandleDeleteResource)
            .WithName("DeleteResource")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        // GET /{resourceType} - Search resources
        tenantGroup.MapGet("/{resourceType}", (HttpContext context, int tenantId, string resourceType,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] IFhirVersionContext versionContext, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleSearchResource(context, tenantId, resourceType, mediator, queryParser, searchOptionsBuilderFactory, versionContext, fhirContextAccessor, loggerFactory, ct))
            .WithName("SearchResource")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        // POST /{resourceType}/_search - Search with form-urlencoded
        tenantGroup.MapPost("/{resourceType}/_search", (HttpContext context, int tenantId, string resourceType,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] IFhirVersionContext versionContext, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePostSearchResource(context, tenantId, resourceType, mediator, queryParser, searchOptionsBuilderFactory, versionContext, fhirContextAccessor, loggerFactory, ct))
            .WithName("PostSearchResource")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        // POST /{resourceType} - Create resource (server assigns ID)
        tenantGroup.MapPost("/{resourceType}", HandlePostResource)
            .WithName("PostResource")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status201Created);

        // POST / - Transaction/Batch bundle
        tenantGroup.MapPost("/", HandleBundle)
            .WithName("Bundle")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status501NotImplemented);

        // GET / - Base-level search (system-wide search across all resource types)
        tenantGroup.MapGet("/", (HttpContext context, int tenantId,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] IFhirVersionContext versionContext, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleBaseSearchResource(context, tenantId, mediator, queryParser, searchOptionsBuilderFactory, versionContext, fhirContextAccessor, loggerFactory, ct))
            .WithName("BaseSearchResource")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        // POST /_search - Base-level search with form-urlencoded (system-wide search)
        tenantGroup.MapPost("/_search", (HttpContext context, int tenantId,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] IFhirVersionContext versionContext, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePostBaseSearchResource(context, tenantId, mediator, queryParser, searchOptionsBuilderFactory, versionContext, fhirContextAccessor, loggerFactory, ct))
            .WithName("PostBaseSearchResource")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR endpoints (/{resourceType}/...).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// All routes within this group validate resource type against tenant's FHIR version via ResourceTypeValidationFilter.
    /// </summary>
    public static IEndpointRouteBuilder MapFhirAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-AGNOSTIC ROUTES (FHIR-compliant, single-tenant auto-detection)
        // Middleware validates: single tenant = auto-apply, multi-tenant = 400 Bad Request
        // These routes delegate to the same handlers - middleware provides tenant context
        // Create a route group with the filter applied to all endpoints
        var agnosticGroup = endpoints
            .MapGroup(string.Empty)
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>()
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        // GET /{resourceType}/{id} - Read resource (agnostic)
        agnosticGroup.MapGet("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleGetResource(context, fhirContextAccessor.RequestContext!.TenantId, resourceType, id, mediator, loggerFactory, ct))
            .WithName("GetResourceAgnostic")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        // PUT /{resourceType} - Conditional Update (agnostic, no ID in URL, uses query string)
        // IMPORTANT: Must be registered BEFORE PUT /{resourceType}/{id} to match correctly
        agnosticGroup.MapPut("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
            [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleConditionalUpdateResource(context, resourceType, mediator, memoryStreamManager, fhirContextAccessor, loggerFactory, ct))
            .WithName("ConditionalUpdateResourceAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status201Created, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest);

        // PUT /{resourceType}/{id} - Create or update resource (agnostic)
        agnosticGroup.MapPut("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
            [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePutResource(context, fhirContextAccessor.RequestContext!.TenantId, resourceType, id, mediator, memoryStreamManager, fhirContextAccessor, loggerFactory, ct))
            .WithName("PutResourceAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        // DELETE /{resourceType} - Conditional Delete (agnostic, no ID in URL, uses query string)
        // IMPORTANT: Must be registered BEFORE DELETE /{resourceType}/{id} to match correctly
        agnosticGroup.MapDelete("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, CancellationToken ct) =>
            HandleConditionalDeleteResource(context, resourceType, mediator, fhirContextAccessor, ct))
            .WithName("ConditionalDeleteResourceAgnostic")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest);

        // DELETE /{resourceType}/{id} - Delete resource (agnostic)
        agnosticGroup.MapDelete("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleDeleteResource(context, fhirContextAccessor.RequestContext!.TenantId, resourceType, id, mediator, loggerFactory, ct))
            .WithName("DeleteResourceAgnostic")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        // GET /{resourceType} - Search resources (agnostic)
        agnosticGroup.MapGet("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] IFhirVersionContext versionContext, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleSearchResource(context, fhirContextAccessor.RequestContext!.TenantId, resourceType, mediator, queryParser, searchOptionsBuilderFactory, versionContext, fhirContextAccessor, loggerFactory, ct))
            .WithName("SearchResourceAgnostic")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        // POST /{resourceType}/_search - Search with form-urlencoded (agnostic)
        agnosticGroup.MapPost("/{resourceType}/_search", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] IFhirVersionContext versionContext, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePostSearchResource(context, fhirContextAccessor.RequestContext!.TenantId, resourceType, mediator, queryParser, searchOptionsBuilderFactory, versionContext, fhirContextAccessor, loggerFactory, ct))
            .WithName("PostSearchResourceAgnostic")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        // POST /{resourceType} - Create resource with server-assigned ID (agnostic)
        agnosticGroup.MapPost("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
            [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePostResource(context, fhirContextAccessor.RequestContext!.TenantId, resourceType, mediator, memoryStreamManager, fhirContextAccessor, loggerFactory, ct))
            .WithName("PostResourceAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        // POST / - Transaction/Batch bundle (agnostic)
        agnosticGroup.MapPost("/", (HttpContext context, [FromServices] BundleProcessor bundleProcessor,
            [FromServices] StreamingBundleParser streamingParser, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleBundle(context, fhirContextAccessor.RequestContext!.TenantId, bundleProcessor, streamingParser, loggerFactory, ct))
            .WithName("BundleAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status501NotImplemented);

        // GET / - Base-level search (agnostic, system-wide search across all resource types)
        agnosticGroup.MapGet("/", (HttpContext context,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] IFhirVersionContext versionContext, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandleBaseSearchResource(context, fhirContextAccessor.RequestContext!.TenantId, mediator, queryParser, searchOptionsBuilderFactory, versionContext, fhirContextAccessor, loggerFactory, ct))
            .WithName("BaseSearchResourceAgnostic")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        // POST /_search - Base-level search with form-urlencoded (agnostic, system-wide search)
        agnosticGroup.MapPost("/_search", (HttpContext context,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] IFhirVersionContext versionContext, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePostBaseSearchResource(context, fhirContextAccessor.RequestContext!.TenantId, mediator, queryParser, searchOptionsBuilderFactory, versionContext, fhirContextAccessor, loggerFactory, ct))
            .WithName("PostBaseSearchResourceAgnostic")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    /// <summary>
    /// GET /tenant/{tenantId:int}/{resourceType}/{id}
    /// Supports conditional read via If-None-Match and If-Modified-Since headers.
    /// </summary>
    private static async Task<IResult> HandleGetResource(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromRoute] string id,
        [FromServices] IMediator mediator,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("GET /tenant/{TenantId}/{ResourceType}/{Id}", tenantId, resourceType, id);
        
        // Check for conditional read headers
        var ifNoneMatch = context.Request.Headers["If-None-Match"].FirstOrDefault();
        var ifModifiedSince = context.Request.Headers["If-Modified-Since"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(ifNoneMatch) || !string.IsNullOrWhiteSpace(ifModifiedSince))
        {
            // Conditional read operation
            var parsedETag = ConditionalHeaderParser.ParseIfNoneMatch(ifNoneMatch);
            var parsedDate = ConditionalHeaderParser.ParseIfModifiedSince(ifModifiedSince);

            var conditionalQuery = new ConditionalReadQuery(
                tenantId,
                resourceType,
                id,
                parsedETag,
                parsedDate);

            var conditionalResult = await mediator.SendAsync(conditionalQuery, ct);

            if (conditionalResult.Resource == null)
            {
                // Resource not found
                logger.LogInformation("Resource {ResourceType}/{Id} not found in tenant {TenantId}", resourceType, id, tenantId);
                return Results.NotFound();
            }

            if (conditionalResult.NotModified)
            {
                // 304 Not Modified: Include ETag and Last-Modified headers but no body
                logger.LogInformation("Resource {ResourceType}/{Id} not modified, returning 304", resourceType, id);
                return FhirResults.NotModified()
                    .WithETag(conditionalResult.Resource.VersionId)
                    .WithLastModified(conditionalResult.Resource.LastModified);
            }

            // Resource modified: Return resource with headers
            logger.LogInformation("Resource {ResourceType}/{Id} modified, returning resource", resourceType, id);
            return FhirResults.Ok(conditionalResult.Resource.ResourceBytes, context)
                .WithETag(conditionalResult.Resource.VersionId)
                .WithLastModified(conditionalResult.Resource.LastModified);
        }

        // Normal GET (no conditional headers)
        var query = new GetResourceQuery(resourceType, id);
        SearchEntryResult? result = await mediator.SendAsync(query, ct);

        if (result == null)
        {
            logger.LogInformation("Resource {ResourceType}/{Id} not found in tenant {TenantId}", resourceType, id, tenantId);
            return Results.NotFound();
        }

        // Check if resource is deleted (FHIR R4 spec: return 410 Gone)
        if (result.IsDeleted)
        {
            logger.LogInformation(
                "Resource {ResourceType}/{Id} has been deleted (version {VersionId})",
                resourceType,
                id,
                result.VersionId);

            // Return 410 Gone per FHIR R4 specification (Section 3.1.0.1.2)
            // 410 Gone = resource existed but has been deleted
            // 404 Not Found = resource never existed
            return Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Resource Deleted",
                detail: $"{resourceType}/{id} has been deleted (last version: {result.VersionId})");
        }

        // Return raw JSON bytes with FHIR headers (zero-copy serialization)
        return FhirResults.Ok(result.ResourceBytes, context)
            .WithETag(result.VersionId)
            .WithLastModified(result.LastModified);
    }

    /// <summary>
    /// PUT /tenant/{tenantId:int}/{resourceType}/{id}
    /// </summary>
    private static async Task<IResult> HandlePutResource(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromRoute] string id,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("PUT /tenant/{TenantId}/{ResourceType}/{Id}", tenantId, resourceType, id);

        // Read request body
        ResourceJsonNode jsonNode;
        await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("request-body"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;
            jsonNode = await JsonSourceNodeFactory.ParseAsync(memoryStream, ct);
        }

        // Validate resource type matches
        if (!string.Equals(jsonNode.ResourceType, resourceType, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Resource type mismatch: expected '{ExpectedType}', got '{ActualType}'",
                resourceType,
                jsonNode.ResourceType);
            throw new BadRequestException($"Resource type must be '{resourceType}', got '{jsonNode.ResourceType}'");
        }

        // Validate resource ID matches (FHIR Spec: For PUT, the resource ID in the body must match the ID in the URL)
        var bodyId = jsonNode.Id;
        if (string.IsNullOrWhiteSpace(bodyId))
        {
            logger.LogWarning(
                "Resource ID missing in body for PUT request to {ResourceType}/{Id}",
                resourceType,
                id);
            throw new BadRequestException($"Resource ID must be present in the body for PUT requests");
        }

        if (!string.Equals(bodyId, id, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Resource ID mismatch: URL has '{UrlId}', body has '{BodyId}'",
                id,
                bodyId);
            throw new BadRequestException($"Resource ID in body ('{bodyId}') must match the ID in the URL ('{id}')");
        }

        // Extract deferred write coordinator from IFhirRequestContext (works for both regular and bundle requests)
        var coordinator = fhirContextAccessor.RequestContext?.DeferredWriteCoordinator;

        // Extract validation depth preference from Prefer header, or from HttpContext.Items if in bundle
        var validationOverride = PreferHeaderParser.TryParseValidationLevel(context.Request.Headers, logger);
        if (!validationOverride.HasValue &&
            context.Items.TryGetValue("ValidationDepthOverride", out var contextOverride) &&
            contextOverride is ValidationDepth bundleValidationDepth)
        {
            validationOverride = bundleValidationDepth;
            logger.LogDebug("Using bundle validation depth override: {ValidationDepth}", validationOverride.Value);
        }

        // Extract return preference from Prefer header (RFC 7240)
        var returnPreference = PreferHeaderParser.TryParseReturnPreference(context.Request.Headers, logger);

        // Extract and validate If-Match header for optimistic concurrency control (ETag validation)
        string? parsedIfMatch = null;
        var ifMatchHeader = context.Request.Headers["If-Match"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(ifMatchHeader))
        {
            // Parse the If-Match header value (e.g., W/"5" or "5")
            parsedIfMatch = ConditionalHeaderParser.ParseIfNoneMatch(ifMatchHeader);

            // Validate ETag format: must be numeric and >= 1
            if (parsedIfMatch != null && (!int.TryParse(parsedIfMatch, out var versionId) || versionId < 1))
            {
                logger.LogWarning("Invalid ETag in If-Match header: {IfMatch} - must be numeric and >= 1", ifMatchHeader);
                throw new BadRequestException($"Invalid ETag value: {ifMatchHeader}. ETag must be a positive integer.");
            }
        }

        // Extract X-Provenance header if present (only for standalone operations)
        var provenanceResource = await ProvenanceHeaderHelper.TryParseForStandaloneOperationAsync(
            context.Request.Headers,
            coordinator,
            memoryStreamManager,
            logger,
            ct);

        // Extract X-TTL header if present (TTL expiration timestamp)
        var expiresAt = TtlHeaderHelper.TryParseTtlHeader(context.Request.Headers, logger);

        // Send generic command with optional coordinator and validation override
        var command = new CreateOrUpdateResourceCommand(resourceType, id, jsonNode, System.Net.Http.HttpMethod.Put, coordinator, parsedIfMatch, validationOverride, provenanceResource, expiresAt);
        UpdateResult result = await mediator.SendAsync(command, ct);

        // Add ETag, Last-Modified, and Preference-Applied headers
        context.Response.Headers.Append("ETag", $"W/\"{result.Key.VersionId}\"");
        context.Response.Headers.Append("Last-Modified", result.LastModified.ToString("R"));
        if (validationOverride.HasValue)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(validationOverride.Value));
        }

        // Determine if created or updated
        bool isCreated = result.Key.VersionId == "1";

        // Determine actual return preference: default to representation (FHIR spec), unless minimal explicitly requested
        var actualReturnPreference = returnPreference == ReturnPreference.Minimal
            ? ReturnPreference.Minimal
            : ReturnPreference.Representation;

        // Add Preference-Applied header for return preference
        if (returnPreference != ReturnPreference.Unspecified)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(actualReturnPreference));
        }

        var location = $"{context.Request.Scheme}://{context.Request.Host}/tenant/{tenantId}/{resourceType}/{result.Key.Id}";

        if (isCreated)
        {
            logger.LogInformation("Created {ResourceType}/{Id} (version {Version}) in tenant {TenantId}", resourceType, result.Key.Id, result.Key.VersionId, tenantId);

            if (actualReturnPreference == ReturnPreference.Representation)
            {
                // Return full resource representation with Location and ETag headers
                return FhirResults.Created(location, result.ResourceBytes, context)
                    .WithETag(result.Key.VersionId!)
                    .WithLastModified(result.LastModified);
            }

            // Prefer: return=minimal - return headers only, no body (FHIR spec compliant)
            return FhirResults.Created(location)
                .WithETag(result.Key.VersionId!)
                .WithLastModified(result.LastModified);
        }

        logger.LogInformation("Updated {ResourceType}/{Id} (version {Version}) in tenant {TenantId}", resourceType, result.Key.Id, result.Key.VersionId, tenantId);

        if (actualReturnPreference == ReturnPreference.Representation)
        {
            // Return full resource representation with ETag headers
            return FhirResults.Ok(result.ResourceBytes, context)
                .WithETag(result.Key.VersionId!)
                .WithLastModified(result.LastModified);
        }

        // Prefer: return=minimal - return headers only, no body (FHIR spec compliant)
        return new FhirResult(StatusCodes.Status200OK)
            .WithETag(result.Key.VersionId!)
            .WithLastModified(result.LastModified);
    }

    /// <summary>
    /// GET /tenant/{tenantId:int}/{resourceType} - Search
    /// </summary>
    private static async Task<IResult> HandleSearchResource(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] IQueryParameterParser queryParser,
        [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        [FromServices] IFhirVersionContext versionContext,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("GET /tenant/{TenantId}/{ResourceType}?{QueryString}", tenantId, resourceType, context.Request.QueryString);

        // Get tenant configuration from FHIR request context (works for both regular and bundle entry requests)
        var fhirContext = fhirContextAccessor.RequestContext;
        if (fhirContext?.TenantConfiguration == null)
        {
            logger.LogError("TenantConfiguration not found in IFhirRequestContext for resourceType '{ResourceType}'", resourceType);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var tenantConfig = fhirContext.TenantConfiguration;

        // Get version-specific search options builder and schema provider
        // CRITICAL: Pass tenantId to use tenant-specific search parameters (e.g., US Core)
        // This ensures query parsing uses the same parameters as indexing
        var fhirSpec = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var searchOptionsBuilder = searchOptionsBuilderFactory.Create(fhirSpec, tenantId);
        var schemaProvider = versionContext.GetSchemaProvider(fhirSpec, tenantId);

        // Parse query parameters
        var queryParameters = queryParser.Parse(context.Request.Query);

        // Build SearchOptions
        var searchOptions = searchOptionsBuilder.Build(resourceType, queryParameters, schemaProvider);

        // Check for unsupported parameters with handling=strict
        var strictResult = CheckStrictHandling(context, searchOptions, resourceType, logger);
        if (strictResult is not null)
        {
            return strictResult;
        }

        // Send search query
        var searchQuery = new SearchResourcesQuery(resourceType, searchOptions);
        SearchResourcesResult result = await mediator.SendAsync(searchQuery, ct);

        // Build base URL for link generation
        string baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";

        // Set response headers
        context.Response.ContentType = "application/fhir+json; charset=utf-8";

        // Check for _pretty parameter
        bool pretty = context.Request.Query.GetPrettyParameter();

        // Stream Bundle response with count-as-render pagination
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            searchOptions: result.SearchOptions!,
            baseUrl: baseUrl,
            queryString: context.Request.QueryString.Value ?? string.Empty,
            schemaProvider: schemaProvider,
            pretty: pretty,
            cancellationToken: ct);

        return Results.Empty;
    }

    /// <summary>
    /// POST /tenant/{tenantId:int}/{resourceType}/_search - Search with form-urlencoded body
    /// FHIR Spec Requirement: All pagination links SHALL be expressed as HTTP GET requests.
    /// Even though the search request uses POST with form parameters, pagination links must convert
    /// to GET requests with query parameters in the URL.
    /// </summary>
    private static async Task<IResult> HandlePostSearchResource(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] IQueryParameterParser queryParser,
        [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        [FromServices] IFhirVersionContext versionContext,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("POST /tenant/{TenantId}/{ResourceType}/_search", tenantId, resourceType);

        // Read form data from request body
        var form = await context.Request.ReadFormAsync(ct);

        // Convert form data to query parameters
        var queryParameters = form
            .SelectMany(kvp => kvp.Value.Select(v => new QueryParameter(kvp.Key, v ?? string.Empty)))
            .ToList();

        // FHIR Spec: Query string parameters can be combined with form body parameters
        // Query string parameters take precedence over form body parameters for duplicates
        var urlParameters = queryParser.Parse(context.Request.Query);
        var urlParamKeys = urlParameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove form parameters that would be overridden by URL parameters
        queryParameters.RemoveAll(p => urlParamKeys.Contains(p.Name));
        queryParameters.AddRange(urlParameters);

        logger.LogDebug("Converted {Count} total parameters (form + URL) for search", queryParameters.Count);

        // Get tenant configuration from FHIR request context (works for both regular and bundle entry requests)
        var fhirContext = fhirContextAccessor.RequestContext;
        if (fhirContext?.TenantConfiguration == null)
        {
            logger.LogError("TenantConfiguration not found in IFhirRequestContext for resourceType '{ResourceType}'", resourceType);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var tenantConfig = fhirContext.TenantConfiguration;
        // CRITICAL: Pass tenantId to use tenant-specific search parameters (e.g., US Core)
        var fhirSpec = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var searchOptionsBuilder = searchOptionsBuilderFactory.Create(fhirSpec, tenantId);
        var schemaProvider = versionContext.GetSchemaProvider(fhirSpec, tenantId);
        var searchOptions = searchOptionsBuilder.Build(resourceType, queryParameters, schemaProvider);

        // Check for unsupported parameters with handling=strict
        var strictResult = CheckStrictHandling(context, searchOptions, resourceType, logger);
        if (strictResult is not null)
        {
            return strictResult;
        }

        // Send search query
        var searchQuery = new SearchResourcesQuery(resourceType, searchOptions);
        SearchResourcesResult result = await mediator.SendAsync(searchQuery, ct);

        // Build base URL for link generation
        // FHIR Spec: Pagination links must be GET requests, so use the GET search endpoint (without /_search)
        // Detect if this is an agnostic route (single-tenant mode) or tenant-explicit route
        // For agnostic: POST /Patient/_search -> GET /Patient?...
        // For tenant-explicit: POST /tenant/1/Patient/_search -> GET /tenant/1/Patient?...
        bool isAgnosticRoute = !context.Request.Path.Value!.StartsWith("/tenant/", StringComparison.OrdinalIgnoreCase);
        string baseUrl = isAgnosticRoute
            ? $"{context.Request.Scheme}://{context.Request.Host}/{resourceType}"
            : $"{context.Request.Scheme}://{context.Request.Host}/tenant/{tenantId}/{resourceType}";

        // FHIR Spec: All pagination links SHALL preserve search parameters as query parameters
        // Convert form parameters back to query string for inclusion in pagination links
        string queryString = BuildQueryStringFromParameters(queryParameters);

        // Set response headers
        context.Response.ContentType = "application/fhir+json; charset=utf-8";

        // Check for _pretty parameter (from query or form data)
        bool pretty = context.Request.Query.GetPrettyParameter();

        // Stream Bundle response with count-as-render pagination
        // Pagination links will be GET requests with search parameters preserved
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            searchOptions: result.SearchOptions!,
            baseUrl: baseUrl,
            queryString: queryString, // POST _search: convert to GET with query parameters
            schemaProvider: schemaProvider,
            pretty: pretty,
            cancellationToken: ct);

        return Results.Empty;
    }

    /// <summary>
    /// POST /tenant/{tenantId:int}/{resourceType} - Create (server assigns ID) or Conditional Create
    /// </summary>
    private static async Task<IResult> HandlePostResource(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("POST /tenant/{TenantId}/{ResourceType}", tenantId, resourceType);

        // Check for If-None-Exist header (conditional create)
        // Note: Empty or whitespace-only header values are treated as no header (normal create)
        // This is valid FHIR behavior - servers MAY choose to process empty If-None-Exist as normal create
        if (context.Request.Headers.TryGetValue("If-None-Exist", out var ifNoneExist)
            && !string.IsNullOrWhiteSpace(ifNoneExist.ToString()))
        {
            logger.LogInformation(
                "Conditional create detected for {ResourceType} with search criteria: {SearchCriteria}",
                resourceType,
                ifNoneExist.ToString());

            // Read request body
            ResourceJsonNode conditionalJsonNode;
            await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("request-body"))
            {
                await context.Request.Body.CopyToAsync(memoryStream, ct);
                memoryStream.Position = 0;
                conditionalJsonNode = await JsonSourceNodeFactory.ParseAsync(memoryStream, ct);
            }

            // Extract X-Provenance header if present (only for standalone operations)
            var conditionalProvenanceResource = await ProvenanceHeaderHelper.TryParseForStandaloneOperationAsync(
                context.Request.Headers,
                null, // No coordinator for conditional create (standalone only)
                memoryStreamManager,
                logger,
                ct);

            // Extract X-TTL header if present (TTL expiration timestamp)
            var conditionalExpiresAt = TtlHeaderHelper.TryParseTtlHeader(context.Request.Headers, logger);

            // Validate Prefer header strictly (before executing command)
            var (returnPreferenceConditional, isPreferValid, preferErrorMessage) = PreferHeaderParser.ParseReturnPreferenceStrict(context.Request.Headers, logger);
            if (!isPreferValid)
            {
                throw new BadRequestException(preferErrorMessage ?? "Invalid Prefer header");
            }

            // Execute conditional create
            var command = new Application.Features.ConditionalOperations.ConditionalCreate.ConditionalCreateCommand(
                TenantId: tenantId,
                ResourceType: resourceType,
                IfNoneExist: ifNoneExist.ToString(),
                JsonNode: conditionalJsonNode,
                ProvenanceResource: conditionalProvenanceResource,
                RequestId: context.TraceIdentifier,
                ExpiresAt: conditionalExpiresAt);

            var result = await mediator.SendAsync(command, ct);

            var resourceBytes = result.Resource.Resource.SerializeToBytes();

            // Determine actual return preference: default to representation (FHIR spec)
            var actualReturnPreferenceConditional = returnPreferenceConditional == ReturnPreference.Unspecified
                ? ReturnPreference.Representation
                : returnPreferenceConditional;

            // Add Preference-Applied header for return preference
            if (returnPreferenceConditional != ReturnPreference.Unspecified)
            {
                context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(actualReturnPreferenceConditional));
            }

            if (result.WasCreated)
            {
                // 201 Created - return Location header with version history
                // In single-tenant mode (agnostic routes), use /{resourceType}/{id}/_history/{versionId}
                // In multi-tenant mode, use /tenant/{tenantId}/{resourceType}/{id}/_history/{versionId}
                var isAgnosticRouteConditional = context.Items.ContainsKey("IsAgnosticRoute") && (bool)context.Items["IsAgnosticRoute"]!;
                var relativePathConditional = isAgnosticRouteConditional
                    ? $"/{resourceType}/{result.Resource.ResourceId}/_history/{result.Resource.VersionId}"
                    : $"/tenant/{tenantId}/{resourceType}/{result.Resource.ResourceId}/_history/{result.Resource.VersionId}";
                var location = $"{context.Request.Scheme}://{context.Request.Host}{relativePathConditional}";

                logger.LogInformation(
                    "Conditional create: Created new {ResourceType}/{Id} (version {VersionId})",
                    result.Resource.ResourceType,
                    result.Resource.ResourceId,
                    result.Resource.VersionId);

                if (actualReturnPreferenceConditional == ReturnPreference.Minimal)
                {
                    // return=minimal - return headers only, no body (FHIR spec compliant)
                    return FhirResults.Created(location)
                        .WithETag(result.Resource.VersionId)
                        .WithLastModified(result.Resource.LastModified);
                }
                else if (actualReturnPreferenceConditional == ReturnPreference.OperationOutcome)
                {
                    // return=OperationOutcome - return OperationOutcome with success message
                    var outcome = CreateSuccessOperationOutcome($"Successfully created {resourceType}/{result.Resource.ResourceId}");
                    return FhirResults.Created(location, outcome, context)
                        .WithETag(result.Resource.VersionId)
                        .WithLastModified(result.Resource.LastModified);
                }
                else
                {
                    // return=representation - return full resource
                    return FhirResults.Created(location, resourceBytes)
                        .WithETag(result.Resource.VersionId)
                        .WithLastModified(result.Resource.LastModified);
                }
            }
            else
            {
                // 200 OK - existing resource returned
                // FHIR spec: Also include Location header for existing resource (for idempotency)
                logger.LogInformation(
                    "Conditional create: Returned existing {ResourceType}/{Id} (version {VersionId})",
                    result.Resource.ResourceType,
                    result.Resource.ResourceId,
                    result.Resource.VersionId);

                var isAgnosticRouteExisting = context.Items.ContainsKey("IsAgnosticRoute") && (bool)context.Items["IsAgnosticRoute"]!;
                var relativePathExisting = isAgnosticRouteExisting
                    ? $"/{resourceType}/{result.Resource.ResourceId}/_history/{result.Resource.VersionId}"
                    : $"/tenant/{tenantId}/{resourceType}/{result.Resource.ResourceId}/_history/{result.Resource.VersionId}";
                var locationExisting = $"{context.Request.Scheme}://{context.Request.Host}{relativePathExisting}";

                if (actualReturnPreferenceConditional == ReturnPreference.Minimal)
                {
                    // return=minimal - return truly empty body with headers only (no JSON content)
                    // FHIR spec: return=minimal means no resource body, only headers
                    return new FhirResult(StatusCodes.Status200OK)
                        .WithLocation(locationExisting)
                        .WithETag(result.Resource.VersionId)
                        .WithLastModified(result.Resource.LastModified);
                }
                else if (actualReturnPreferenceConditional == ReturnPreference.OperationOutcome)
                {
                    // return=OperationOutcome - return OperationOutcome with ETag and Last-Modified headers
                    var outcome = CreateSuccessOperationOutcome($"No action taken, {resourceType}/{result.Resource.ResourceId} already exists");

                    // Serialize to bytes for FhirResult
                    var outcomeBytes = outcome.SerializeToBytes();
                    return FhirResults.Ok(outcomeBytes.ToArray())
                        .WithLocation(locationExisting)
                        .WithETag(result.Resource.VersionId)
                        .WithLastModified(result.Resource.LastModified);
                }
                else
                {
                    // return=representation - return full resource
                    return FhirResults.Ok(resourceBytes)
                        .WithLocation(locationExisting)
                        .WithETag(result.Resource.VersionId)
                        .WithLastModified(result.Resource.LastModified);
                }
            }
        }

        // Standard create (no If-None-Exist header)

        // Check if we're in a bundle context with a pre-assigned ID (for urn:uuid references)
        string id;
        var bundleAssignedId = fhirContextAccessor.RequestContext?.BundleAssignedResourceId;
        if (!string.IsNullOrWhiteSpace(bundleAssignedId))
        {
            id = bundleAssignedId;
            logger.LogInformation("Using bundle-assigned ID {Id} for new {ResourceType} in tenant {TenantId}", id, resourceType, tenantId);
        }
        else
        {
            // Generate ID
            id = Guid.NewGuid().ToString("N");
            logger.LogInformation("Generated ID {Id} for new {ResourceType} in tenant {TenantId}", id, resourceType, tenantId);
        }

        // Read request body
        ResourceJsonNode jsonNode;
        await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("request-body"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;
            jsonNode = await JsonSourceNodeFactory.ParseAsync(memoryStream, ct);
        }

        // Validate resource type matches
        if (!string.Equals(jsonNode.ResourceType, resourceType, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Resource type mismatch: expected '{ExpectedType}', got '{ActualType}'",
                resourceType,
                jsonNode.ResourceType);
            throw new BadRequestException($"Resource type must be '{resourceType}', got '{jsonNode.ResourceType}'");
        }

        // Extract deferred write coordinator from IFhirRequestContext (works for both regular and bundle requests)
        var coordinator = fhirContextAccessor.RequestContext?.DeferredWriteCoordinator;

        // Extract validation depth preference from Prefer header, or from HttpContext.Items if in bundle
        var validationOverride = PreferHeaderParser.TryParseValidationLevel(context.Request.Headers, logger);
        if (!validationOverride.HasValue &&
            context.Items.TryGetValue("ValidationDepthOverride", out var contextOverride) &&
            contextOverride is ValidationDepth bundleValidationDepth)
        {
            validationOverride = bundleValidationDepth;
            logger.LogDebug("Using bundle validation depth override: {ValidationDepth}", validationOverride.Value);
        }

        // Extract return preference from Prefer header (RFC 7240)
        var returnPreference = PreferHeaderParser.TryParseReturnPreference(context.Request.Headers, logger);

        // Extract X-Provenance header if present (only for standalone operations)
        var provenanceResource = await ProvenanceHeaderHelper.TryParseForStandaloneOperationAsync(
            context.Request.Headers,
            coordinator,
            memoryStreamManager,
            logger,
            ct);

        // Extract X-TTL header if present (TTL expiration timestamp)
        var expiresAt = TtlHeaderHelper.TryParseTtlHeader(context.Request.Headers, logger);

        // Send generic command with HTTP POST method
        var createCommand = new CreateOrUpdateResourceCommand(resourceType, id, jsonNode, System.Net.Http.HttpMethod.Post, coordinator, null, validationOverride, provenanceResource, expiresAt);
        UpdateResult createResult = await mediator.SendAsync(createCommand, ct);

        // Add ETag, Last-Modified, and Preference-Applied headers
        context.Response.Headers.Append("ETag", $"W/\"{createResult.Key.VersionId}\"");
        context.Response.Headers.Append("Last-Modified", createResult.LastModified.ToString("R"));
        if (validationOverride.HasValue)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(validationOverride.Value));
        }

        // Determine actual return preference: default to representation (FHIR spec), unless minimal explicitly requested
        var actualReturnPreference = returnPreference == ReturnPreference.Minimal
            ? ReturnPreference.Minimal
            : ReturnPreference.Representation;

        // Add Preference-Applied header for return preference
        if (returnPreference != ReturnPreference.Unspecified)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(actualReturnPreference));
        }

        // POST always creates (returns 201 Created)
        // In single-tenant mode (agnostic routes), use /{resourceType}/{id}/_history/{versionId}
        // In multi-tenant mode, use /tenant/{tenantId}/{resourceType}/{id}/_history/{versionId}
        var isAgnosticRoute = context.Items.ContainsKey("IsAgnosticRoute") && (bool)context.Items["IsAgnosticRoute"]!;
        var relativePath = isAgnosticRoute
            ? $"/{resourceType}/{createResult.Key.Id}/_history/{createResult.Key.VersionId}"
            : $"/tenant/{tenantId}/{resourceType}/{createResult.Key.Id}/_history/{createResult.Key.VersionId}";
        string createLocation = $"{context.Request.Scheme}://{context.Request.Host}{relativePath}";
        logger.LogInformation(
            "Created {ResourceType}/{Id} (version {VersionId}) in tenant {TenantId}",
            resourceType,
            createResult.Key.Id,
            createResult.Key.VersionId,
            tenantId);

        if (actualReturnPreference == ReturnPreference.Representation)
        {
            // Return full resource representation
            return FhirResults.Created(createLocation, createResult.ResourceBytes, context)
                .WithETag(createResult.Key.VersionId!)
                .WithLastModified(createResult.LastModified);
        }

        // Prefer: return=minimal - return headers only, no body (FHIR spec compliant)
        return FhirResults.Created(createLocation)
            .WithETag(createResult.Key.VersionId!)
            .WithLastModified(createResult.LastModified);
    }

    /// <summary>
    /// DELETE /tenant/{tenantId:int}/{resourceType}/{id}
    /// </summary>
    private static async Task<IResult> HandleDeleteResource(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromRoute] string resourceType,
        [FromRoute] string id,
        [FromServices] IMediator mediator,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("DELETE /tenant/{TenantId}/{ResourceType}/{Id}", tenantId, resourceType, id);

        // Send delete command
        var command = new DeleteResourceCommand(resourceType, id);
        bool deleted = await mediator.SendAsync(command, ct);

        if (!deleted)
        {
            logger.LogInformation("Resource {ResourceType}/{Id} not found for deletion", resourceType, id);
            return Results.NotFound();
        }

        logger.LogInformation("Deleted {ResourceType}/{Id}", resourceType, id);
        return Results.NoContent();
    }

    /// <summary>
    /// POST /tenant/{tenantId:int} - Transaction/Batch bundle
    /// Always uses streaming parser, buffers when needed for urn:uuid resolution.
    /// Phase 2: Supports true end-to-end streaming for batch bundles without urn:uuid.
    /// </summary>
    private static async Task<IResult> HandleBundle(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromServices] BundleProcessor bundleProcessor,
        [FromServices] StreamingBundleParser streamingParser,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("POST /tenant/{TenantId} (Bundle)", tenantId);

        // Extract validation preference from Prefer header (applies to all entries in bundle)
        var validationOverride = PreferHeaderParser.TryParseValidationLevel(context.Request.Headers, logger);
        if (validationOverride.HasValue)
        {
            context.Items["ValidationDepthOverride"] = validationOverride;
            logger.LogInformation("Bundle validation preference set to {ValidationDepth}", validationOverride.Value);
        }

        // ALWAYS parse with streaming parser - returns metadata + streaming entries
        var bundleContext = await streamingParser.ParseStreamAsync(context.Request.Body, ct);

        // Validate resource type
        if (bundleContext.ResourceType != "Bundle")
        {
            logger.LogWarning("Expected Bundle resource, got '{ResourceType}'", bundleContext.ResourceType);
            return Results.BadRequest(new { error = $"Expected Bundle resource, got '{bundleContext.ResourceType}'" });
        }

        // Log parsing issues
        foreach (var issue in bundleContext.ParsingIssues)
        {
            logger.LogWarning("Bundle parsing issue: {Issue}", issue);
        }

        // Determine bundle type (default to Batch if not specified)
        var bundleTypeString = bundleContext.BundleType?.ToUpperInvariant();
        var bundleType = bundleTypeString switch
        {
            "TRANSACTION" => BundleType.Transaction,
            "BATCH" => BundleType.Batch,
            _ => BundleType.Batch // Default to batch
        };

        logger.LogDebug("Bundle type: {BundleType}", bundleType);

        var options = new BundleProcessingOptions
        {
            MaxParallelism = 10,
            ChannelCapacity = 100,
            Type = bundleType
        };

        // Phase 2: Dual-mode routing
        if (options.Type == BundleType.Transaction)
        {
            logger.LogInformation("Using buffered processing (Transaction: {IsTransaction})",
                options.Type == BundleType.Transaction);

            BundleJsonNode responseBundle = await bundleProcessor.ProcessAsync(
                bundleContext.Entries, options, ct);

            // Serialize response bundle with System.Text.Json
            string responseJson;
            try
            {
                responseJson = responseBundle.SerializeToString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to serialize response bundle");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            logger.LogInformation("Successfully processed bundle (buffered mode)");
            if (validationOverride.HasValue)
            {
                context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(validationOverride.Value));
            }
                
            return Results.Content(responseJson, KnownContentTypes.ApplicationFhirJson);
        }
        else
        {
            // STREAMING MODE: Batch bundle with no urn:uuid references
            // True end-to-end streaming - responses written as they complete
            logger.LogInformation("Using streaming processing (Batch bundle, no urn:uuid references)");

            try
            {
                // Get streaming context
                var streamingContext = await bundleProcessor.ProcessBatchStreamingAsync(
                    bundleContext.Entries, options, ct);

                // Set response content type and headers BEFORE streaming starts (headers are locked once body writes begin)
                context.Response.ContentType = "application/fhir+json; charset=utf-8";

                // Add Preference-Applied header if validation override was used (MUST be before SerializeStreamAsync)
                if (validationOverride.HasValue)
                {
                    context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(validationOverride.Value));
                }

                // Check for _pretty parameter
                bool pretty = context.Request.Query.GetPrettyParameter();

                // Stream responses directly to HTTP (headers are now locked)
                await StreamingBundleSerializer.SerializeStreamAsync(
                    outputStream: context.Response.Body,
                    bundleType: "batch-response",
                    entryResponses: streamingContext.ResponseStream,
                    total: null,
                    selfLink: null,
                    nextLink: null,
                    pretty: pretty,
                    cancellationToken: ct);

                // Complete background tasks
                await streamingContext.CompleteAsync();

                logger.LogInformation("Successfully processed bundle (streaming mode)");

                // Response already written to stream
                return Results.Empty;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process streaming bundle");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }

    /// <summary>
    /// PUT /{resourceType} - Conditional Update (tenant-agnostic)
    /// Delegates to tenant-explicit handler with extracted tenant ID.
    /// </summary>
    private static async Task<IResult> HandleConditionalUpdateResource(
        HttpContext context,
        string resourceType,
        IMediator mediator,
        RecyclableMemoryStreamManager memoryStreamManager,
        IFhirRequestContextAccessor fhirContextAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var tenantId = fhirContextAccessor.RequestContext!.TenantId;
        return await HandleConditionalUpdateResourceExplicit(context, tenantId, resourceType, mediator, memoryStreamManager, loggerFactory, ct);
    }

    /// <summary>
    /// PUT /tenant/{tenantId:int}/{resourceType} - Conditional Update (tenant-explicit)
    /// Updates resource based on query string parameters.
    /// - 0 matches: Create new resource (201 Created)
    /// - 1 match: Update existing resource (200 OK)
    /// - Multiple matches: Error (412 Precondition Failed)
    /// </summary>
    private static async Task<IResult> HandleConditionalUpdateResourceExplicit(
        HttpContext context,
        int tenantId,
        string resourceType,
        IMediator mediator,
        RecyclableMemoryStreamManager memoryStreamManager,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        // Extract query string (search criteria)
        var queryString = context.Request.QueryString.Value;

        // FHIR spec: Bundle cannot be used in conditional operations
        // Reject with "not selective enough" error (matches test expectations)
        if (string.Equals(resourceType, KnownResourceTypes.Bundle, StringComparison.OrdinalIgnoreCase))
        {
            throw new Domain.Exceptions.BadRequestException(
                string.Format(Ignixa.Search.Resources.ConditionalOperationNotSelectiveEnough, resourceType));
        }

        if (string.IsNullOrWhiteSpace(queryString) || queryString == "?")
        {
            throw new Domain.Exceptions.BadRequestException("Conditional update requires search parameters in query string");
        }

        // Remove leading '?'
        var searchCriteria = queryString.TrimStart('?');

        // Parse request body to ResourceJsonNode
        ResourceJsonNode jsonNode;
        await using (var memoryStream = memoryStreamManager.GetStream("conditional-update-request"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;
            jsonNode = await JsonSourceNodeFactory.ParseAsync(memoryStream, ct);
        }

        // Extract X-Provenance header if present (only for standalone operations)
        var provenanceResource = await ProvenanceHeaderHelper.TryParseForStandaloneOperationAsync(
            context.Request.Headers,
            null, // No coordinator for conditional update (standalone only)
            memoryStreamManager,
            logger,
            ct);

        // Extract X-TTL header if present (TTL expiration timestamp)
        var expiresAt = TtlHeaderHelper.TryParseTtlHeader(context.Request.Headers, logger);

        // Execute conditional update via mediator
        var command = new Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate.ConditionalUpdateCommand(
            TenantId: tenantId,
            ResourceType: resourceType,
            SearchCriteria: searchCriteria,
            JsonNode: jsonNode,
            ProvenanceResource: provenanceResource,
            RequestId: context.TraceIdentifier,
            ExpiresAt: expiresAt);

        var result = await mediator.SendAsync(command, ct);

        var resourceBytes = result.Resource.Resource.SerializeToBytes();

        // Extract return preference from Prefer header (RFC 7240)
        var returnPreference = PreferHeaderParser.TryParseReturnPreference(context.Request.Headers, logger);

        // Determine actual return preference: default to representation (FHIR spec), unless minimal or OperationOutcome explicitly requested
        var actualReturnPreference = returnPreference == ReturnPreference.Unspecified
            ? ReturnPreference.Representation
            : returnPreference;

        // Add Preference-Applied header for return preference
        if (returnPreference != ReturnPreference.Unspecified)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(actualReturnPreference));
        }

        if (result.WasCreated)
        {
            // 201 Created - include Location header (absolute URL per FHIR spec)
            var location = $"{context.Request.Scheme}://{context.Request.Host}/tenant/{tenantId}/{resourceType}/{result.Resource.ResourceId}";

            if (actualReturnPreference == ReturnPreference.Minimal)
            {
                // return=minimal - return headers only, no body (FHIR spec compliant)
                return FhirResults.Created(location)
                    .WithETag(result.Resource.VersionId)
                    .WithLastModified(result.Resource.LastModified);
            }
            else if (actualReturnPreference == ReturnPreference.OperationOutcome)
            {
                // return=OperationOutcome - return OperationOutcome with success message
                var outcome = CreateSuccessOperationOutcome($"Successfully created {resourceType}/{result.Resource.ResourceId}");
                return FhirResults.Created(location, outcome, context)
                    .WithETag(result.Resource.VersionId)
                    .WithLastModified(result.Resource.LastModified);
            }
            else
            {
                // return=representation - return full resource
                return FhirResults.Created(location, resourceBytes)
                    .WithETag(result.Resource.VersionId)
                    .WithLastModified(result.Resource.LastModified);
            }
        }
        else
        {
            // 200 OK
            if (actualReturnPreference == ReturnPreference.Minimal)
            {
                // return=minimal - return headers only, no body (FHIR spec compliant)
                return new FhirResult(StatusCodes.Status200OK)
                    .WithETag(result.Resource.VersionId)
                    .WithLastModified(result.Resource.LastModified);
            }
            else if (actualReturnPreference == ReturnPreference.OperationOutcome)
            {
                // return=OperationOutcome - return OperationOutcome with success message
                var outcome = CreateSuccessOperationOutcome($"Successfully updated {resourceType}/{result.Resource.ResourceId}");
                return FhirResults.Ok(outcome, context);
            }
            else
            {
                // return=representation - return full resource
                return FhirResults.Ok(resourceBytes)
                    .WithETag(result.Resource.VersionId)
                    .WithLastModified(result.Resource.LastModified);
            }
        }
    }

    /// <summary>
    /// DELETE /{resourceType} - Conditional Delete (tenant-agnostic)
    /// Delegates to tenant-explicit handler with extracted tenant ID.
    /// </summary>
    private static async Task<IResult> HandleConditionalDeleteResource(
        HttpContext context,
        string resourceType,
        IMediator mediator,
        IFhirRequestContextAccessor fhirContextAccessor,
        CancellationToken ct)
    {
        var tenantId = fhirContextAccessor.RequestContext!.TenantId;
        return await HandleConditionalDeleteResourceExplicit(context, tenantId, resourceType, mediator, ct);
    }

    /// <summary>
    /// DELETE /tenant/{tenantId:int}/{resourceType} - Conditional Delete (tenant-explicit)
    /// Deletes resources based on query string parameters.
    ///
    /// Single mode (no _count parameter):
    /// - 0 matches: 404 Not Found
    /// - 1 match: 204 No Content (delete resource)
    /// - Multiple matches: 412 Precondition Failed
    ///
    /// Multiple mode (with _count parameter):
    /// - 0 matches: 404 Not Found
    /// - 1-N (≤ _count): 200 OK with OperationOutcome (all deleted)
    /// - > _count: 200 OK with partial delete warning
    /// </summary>
    private static async Task<IResult> HandleConditionalDeleteResourceExplicit(
        HttpContext context,
        int tenantId,
        string resourceType,
        IMediator mediator,
        CancellationToken ct)
    {
        // Extract query string (search criteria)
        var queryString = context.Request.QueryString.Value;

        // FHIR spec: Bundle cannot be used in conditional operations
        // Reject with "not selective enough" error (matches test expectations)
        if (string.Equals(resourceType, KnownResourceTypes.Bundle, StringComparison.OrdinalIgnoreCase))
        {
            throw new Domain.Exceptions.BadRequestException(
                string.Format(Ignixa.Search.Resources.ConditionalOperationNotSelectiveEnough, resourceType));
        }

        if (string.IsNullOrWhiteSpace(queryString) || queryString == "?")
        {
            throw new Domain.Exceptions.BadRequestException("Conditional delete requires search parameters in query string");
        }

        // Parse query string to extract _count and search criteria
        var searchCriteria = queryString.TrimStart('?');
        int? count = null;

        // Extract _count parameter
        var queryParams = QueryHelpers.ParseQuery(queryString);
        if (queryParams.TryGetValue("_count", out var countValue) && int.TryParse(countValue.FirstOrDefault(), out var parsedCount))
        {
            count = parsedCount;

            // Remove _count from search criteria (it's not a search parameter)
            var criteriaWithoutCount = queryParams
                .Where(kvp => kvp.Key != "_count")
                .Select(kvp => $"{kvp.Key}={kvp.Value}");
            searchCriteria = string.Join("&", criteriaWithoutCount);
        }

        // Execute conditional delete
        var command = new ConditionalDeleteCommand(
            tenantId,
            resourceType,
            searchCriteria,
            count,
            context.TraceIdentifier);

        var result = await mediator.SendAsync(command, ct);

        // Return appropriate response based on mode
        if (!count.HasValue && result.DeletedCount == 1)
        {
            // Single mode: 204 No Content
            return Results.NoContent();
        }
        else
        {
            // Multiple mode: 200 OK with verbose OperationOutcome
            var outcome = new OperationOutcomeJsonNode();
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Information,
                Code = OperationOutcomeJsonNode.IssueType.Informational,
                Diagnostics = result.IsPartialDelete
                    ? $"Partial delete: Deleted {result.DeletedCount} of {result.TotalMatches} matching resources (limit: {count}). " +
                      $"Deleted IDs: {string.Join(", ", result.DeletedIds)}"
                    : $"Deleted {result.DeletedCount} matching resource(s). Deleted IDs: {string.Join(", ", result.DeletedIds)}"
            });

            bool pretty = context.Request.Query.GetPrettyParameter();
            return Results.Bytes(outcome.SerializeToBytes(pretty), KnownContentTypes.ApplicationFhirJson);
        }
    }


    /// <summary>
    /// GET / - Base-level search (system-wide search across all resource types)
    /// Supports searching across all resources using common search parameters like _tag, _profile, _security.
    /// Example: GET /?_tag=http://e2etests|conditional-tag&_summary=count
    /// </summary>
    private static async Task<IResult> HandleBaseSearchResource(
        HttpContext context,
        int tenantId,
        IMediator mediator,
        IQueryParameterParser queryParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        IFhirVersionContext versionContext,
        IFhirRequestContextAccessor fhirContextAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("GET /?{QueryString} (base-level search)", context.Request.QueryString);

        // Get tenant configuration from FHIR request context
        var fhirContext = fhirContextAccessor.RequestContext;
        if (fhirContext?.TenantConfiguration == null)
        {
            logger.LogError("TenantConfiguration not found in IFhirRequestContext for base-level search");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var tenantConfig = fhirContext.TenantConfiguration;

        // Get version-specific search options builder and schema provider
        // CRITICAL: Pass tenantId to use tenant-specific search parameters (e.g., US Core)
        var fhirSpec = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var searchOptionsBuilder = searchOptionsBuilderFactory.Create(fhirSpec, tenantId);
        var schemaProvider = versionContext.GetSchemaProvider(fhirSpec, tenantId);

        // Parse query parameters
        var queryParameters = queryParser.Parse(context.Request.Query);

        // Build SearchOptions for base-level search (resourceType = null for system-wide search)
        // This will search across all resource types (handled by SqlEntityFrameworkSearchService)
        var searchOptions = searchOptionsBuilder.Build(null, queryParameters, schemaProvider);

        // Check for unsupported parameters with handling=strict
        var strictResult = CheckStrictHandling(context, searchOptions, null, logger);
        if (strictResult is not null)
        {
            return strictResult;
        }

        // Send search query for base-level search (null resourceType means search all types)
        var searchQuery = new SearchResourcesQuery(null, searchOptions);
        SearchResourcesResult result = await mediator.SendAsync(searchQuery, ct);

        // Build base URL for link generation
        string baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

        // Set response headers
        context.Response.ContentType = "application/fhir+json; charset=utf-8";

        // Check for _pretty parameter
        bool pretty = context.Request.Query.GetPrettyParameter();

        // Stream Bundle response with count-as-render pagination
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            searchOptions: result.SearchOptions!,
            baseUrl: baseUrl,
            queryString: context.Request.QueryString.Value ?? string.Empty,
            schemaProvider: schemaProvider,
            pretty: pretty,
            cancellationToken: ct);

        return Results.Empty;
    }

    /// <summary>
    /// POST /_search - Base-level search with form-urlencoded body (system-wide search)
    /// Supports searching across all resources using common search parameters.
    /// FHIR Spec Requirement: All pagination links SHALL be expressed as HTTP GET requests.
    /// </summary>
    private static async Task<IResult> HandlePostBaseSearchResource(
        HttpContext context,
        int tenantId,
        IMediator mediator,
        IQueryParameterParser queryParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        IFhirVersionContext versionContext,
        IFhirRequestContextAccessor fhirContextAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirEndpoints).FullName!);
        logger.LogInformation("POST /_search (base-level search)");

        // Read form data from request body
        var form = await context.Request.ReadFormAsync(ct);

        // Convert form data to query parameters
        var queryParameters = form
            .SelectMany(kvp => kvp.Value.Select(v => new QueryParameter(kvp.Key, v ?? string.Empty)))
            .ToList();

        // FHIR Spec: Query string parameters can be combined with form body parameters
        // Query string parameters take precedence over form body parameters for duplicates
        var urlParameters = queryParser.Parse(context.Request.Query);
        var urlParamKeys = urlParameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove form parameters that would be overridden by URL parameters
        queryParameters.RemoveAll(p => urlParamKeys.Contains(p.Name));
        queryParameters.AddRange(urlParameters);

        logger.LogDebug("Converted {Count} total parameters for base-level search", queryParameters.Count);

        // Get tenant configuration from FHIR request context
        var fhirContext = fhirContextAccessor.RequestContext;
        if (fhirContext?.TenantConfiguration == null)
        {
            logger.LogError("TenantConfiguration not found in IFhirRequestContext for base-level search");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var tenantConfig = fhirContext.TenantConfiguration;
        // CRITICAL: Pass tenantId to use tenant-specific search parameters (e.g., US Core)
        var fhirSpec = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var searchOptionsBuilder = searchOptionsBuilderFactory.Create(fhirSpec, tenantId);
        var schemaProvider = versionContext.GetSchemaProvider(fhirSpec, tenantId);
        var searchOptions = searchOptionsBuilder.Build(null, queryParameters, schemaProvider);

        // Check for unsupported parameters with handling=strict
        var strictResult = CheckStrictHandling(context, searchOptions, null, logger);
        if (strictResult is not null)
        {
            return strictResult;
        }

        // Send search query for base-level search (null resourceType means search all types)
        var searchQuery = new SearchResourcesQuery(null, searchOptions);
        SearchResourcesResult result = await mediator.SendAsync(searchQuery, ct);

        // Build base URL for link generation
        // FHIR Spec: Pagination links must be GET requests, so use the base URL (without /_search)
        // Detect if this is an agnostic route (single-tenant mode) or tenant-explicit route
        // For agnostic: POST /_search -> GET /?...
        // For tenant-explicit: POST /tenant/1/_search -> GET /tenant/1?...
        bool isAgnosticRoute = !context.Request.Path.Value!.StartsWith("/tenant/", StringComparison.OrdinalIgnoreCase);
        string baseUrl = isAgnosticRoute
            ? $"{context.Request.Scheme}://{context.Request.Host}"
            : $"{context.Request.Scheme}://{context.Request.Host}/tenant/{tenantId}";

        // FHIR Spec: All pagination links SHALL preserve search parameters as query parameters
        string queryString = BuildQueryStringFromParameters(queryParameters);

        // Set response headers
        context.Response.ContentType = "application/fhir+json; charset=utf-8";

        // Stream Bundle response with count-as-render pagination
        // Check for _pretty parameter (from query or form data)
        bool pretty = context.Request.Query.GetPrettyParameter();

        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            searchOptions: result.SearchOptions!,
            baseUrl: baseUrl,
            queryString: queryString,
            schemaProvider: schemaProvider,
            pretty: pretty,
            cancellationToken: ct);

        return Results.Empty;
    }

    /// <summary>
    /// Converts QueryParameter list to a query string suitable for URL links.
    /// FHIR Spec Requirement: Pagination links must preserve all search parameters.
    /// </summary>
    /// <param name="parameters">The query parameters to convert.</param>
    /// <returns>Query string with leading '?' (e.g., "?name=John&birthdate=gt2000"), or empty string if no parameters.</returns>
    private static string BuildQueryStringFromParameters(IReadOnlyList<QueryParameter> parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return string.Empty;
        }

        var encodedPairs = new List<string>();

        foreach (var param in parameters)
        {
            // Skip continuation token - it will be added by the serializer as 'after' parameter
            if (param.Name.Equals("ct", StringComparison.OrdinalIgnoreCase) ||
                param.Name.Equals("after", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // URL-encode parameter name and value
            string encodedName = Uri.EscapeDataString(param.Name);
            string encodedValue = Uri.EscapeDataString(param.Value);

            encodedPairs.Add($"{encodedName}={encodedValue}");
        }

        if (encodedPairs.Count == 0)
        {
            return string.Empty;
        }

        string queryString = "?" + string.Join("&", encodedPairs);
        return queryString;
    }

    /// <summary>
    /// Creates a success OperationOutcome for write operations (create/update/delete).
    /// </summary>
    private static OperationOutcomeJsonNode CreateSuccessOperationOutcome(string message)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Information,
            Code = OperationOutcomeJsonNode.IssueType.Informational,
            Diagnostics = message
        });
        return outcome;
    }

    /// <summary>
    /// Checks for unsupported search parameters when strict handling is requested.
    /// Returns a BadRequest result if strict handling is enabled and unsupported parameters exist.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="searchOptions">The search options containing unsupported params.</param>
    /// <param name="resourceType">Optional resource type for error message context.</param>
    /// <param name="logger">Logger for warnings.</param>
    /// <returns>BadRequest result if strict handling fails, null otherwise.</returns>
    private static IResult? CheckStrictHandling(
        HttpContext context,
        SearchOptions searchOptions,
        string? resourceType,
        ILogger logger)
    {
        if (!PreferHeaderParser.IsStrictHandling(context.Request.Headers) ||
            searchOptions.UnsupportedParams.Count == 0)
        {
            return null;
        }

        // Sanitize user input to prevent log forging (remove newlines and control characters)
        var sanitizedParams = searchOptions.UnsupportedParams
            .Select(p => p.Replace("\r", "", StringComparison.Ordinal)
                          .Replace("\n", "", StringComparison.Ordinal)
                          .Replace("\t", " ", StringComparison.Ordinal));
        logger.LogWarning(
            "Strict handling requested but unsupported parameters found: {UnsupportedParams}",
            string.Join(", ", sanitizedParams));

        var operationOutcome = new OperationOutcomeJsonNode();
        foreach (var param in searchOptions.UnsupportedParams)
        {
            var diagnostics = resourceType is not null
                ? $"Search parameter '{param}' is not supported for resource type '{resourceType}'"
                : $"Search parameter '{param}' is not supported";

            operationOutcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
                Code = OperationOutcomeJsonNode.IssueType.NotSupported,
                Diagnostics = diagnostics
            });
        }

        return Results.BadRequest(operationOutcome.MutableNode);
    }

    /// <summary>
    /// Converts a list to an async enumerable.
    /// </summary>
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (T item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await System.Threading.Tasks.Task.Yield(); // Allow cooperative multitasking
        }
    }
}
