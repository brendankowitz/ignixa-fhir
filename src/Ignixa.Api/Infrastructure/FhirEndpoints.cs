// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IO;
using Ignixa.Application.Features;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.ConditionalOperations;
using Ignixa.Application.Features.ConditionalOperations.ConditionalDelete;
using Ignixa.Application.Features.ConditionalOperations.ConditionalRead;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Utilities;
using Ignixa.Domain.Models;
using Ignixa.Validation;
using Ignixa.Search.Parsing;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.Models;
using Ignixa.SourceNodeSerialization.SourceNodes;
using DeferredWriteCoordinator = Ignixa.Application.Features.Bundle.DeferredWriteCoordinator;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Registers FHIR RESTful endpoints for all resource types.
/// No controllers, no switch statements - pure endpoint routing.
/// </summary>
public static class FhirEndpoints
{
    private const string _contentTypeApplicationFhirJson = "application/fhir+json";
    private const string _contentTypeApplicationJson = "application/json";

    // Reusable JsonSerializerOptions for FHIR bundle serialization (CA1869 compliance)
    private static readonly JsonSerializerOptions BundleJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
    /// </summary>
    public static IEndpointRouteBuilder MapFhirTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-EXPLICIT ROUTES (always supported, all scenarios)

        // GET /tenant/{tenantId:int}/{resourceType}/{id} - Read resource
        endpoints.MapGet("/tenant/{tenantId:int}/{resourceType}/{id}", HandleGetResource)
            .WithName("GetResource")
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status404NotFound);

        // PUT /tenant/{tenantId:int}/{resourceType} - Conditional Update (no ID in URL, uses query string)
        // IMPORTANT: Must be registered BEFORE PUT /{resourceType}/{id} to match correctly
        endpoints.MapPut("/tenant/{tenantId:int}/{resourceType}", HandleConditionalUpdateResourceExplicit)
            .WithName("ConditionalUpdateResourceExplicit")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status201Created, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, _contentTypeApplicationFhirJson);

        // PUT /tenant/{tenantId:int}/{resourceType}/{id} - Create or update resource
        endpoints.MapPut("/tenant/{tenantId:int}/{resourceType}/{id}", HandlePutResource)
            .WithName("PutResource")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created);

        // DELETE /tenant/{tenantId:int}/{resourceType} - Conditional Delete (no ID in URL, uses query string)
        // IMPORTANT: Must be registered BEFORE DELETE /{resourceType}/{id} to match correctly
        endpoints.MapDelete("/tenant/{tenantId:int}/{resourceType}", HandleConditionalDeleteResourceExplicit)
            .WithName("ConditionalDeleteResourceExplicit")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, _contentTypeApplicationFhirJson);

        // DELETE /tenant/{tenantId:int}/{resourceType}/{id} - Delete resource
        endpoints.MapDelete("/tenant/{tenantId:int}/{resourceType}/{id}", HandleDeleteResource)
            .WithName("DeleteResource")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);


        // GET /tenant/{tenantId:int}/{resourceType} - Search resources
        endpoints.MapGet("/tenant/{tenantId:int}/{resourceType}", (HttpContext context, int tenantId, string resourceType,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandleSearchResource(context, tenantId, resourceType, mediator, queryParser, searchOptionsBuilderFactory, logger, ct))
            .WithName("SearchResource")
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        // POST /tenant/{tenantId:int}/{resourceType} - Create resource (server assigns ID)
        endpoints.MapPost("/tenant/{tenantId:int}/{resourceType}", HandlePostResource)
            .WithName("PostResource")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status201Created);

        // POST /tenant/{tenantId:int} - Transaction/Batch bundle
        endpoints.MapPost("/tenant/{tenantId:int}", HandleBundle)
            .WithName("Bundle")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson)
            .Produces(StatusCodes.Status501NotImplemented);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR endpoints (/{resourceType}/...).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// </summary>
    public static IEndpointRouteBuilder MapFhirAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-AGNOSTIC ROUTES (FHIR-compliant, single-tenant auto-detection)
        // Middleware validates: single tenant = auto-apply, multi-tenant = 400 Bad Request
        // These routes delegate to the same handlers - middleware provides tenant context

        // GET /{resourceType}/{id} - Read resource (agnostic)
        endpoints.MapGet("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandleGetResource(context, ExtractTenantId(context), resourceType, id, mediator, logger, ct))
            .WithName("GetResourceAgnostic")
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        // PUT /{resourceType} - Conditional Update (agnostic, no ID in URL, uses query string)
        // IMPORTANT: Must be registered BEFORE PUT /{resourceType}/{id} to match correctly
        endpoints.MapPut("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, CancellationToken ct) =>
            HandleConditionalUpdateResource(context, resourceType, mediator, ct))
            .WithName("ConditionalUpdateResourceAgnostic")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status201Created, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest);

        // PUT /{resourceType}/{id} - Create or update resource (agnostic)
        endpoints.MapPut("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
            [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandlePutResource(context, ExtractTenantId(context), resourceType, id, mediator, memoryStreamManager, logger, ct))
            .WithName("PutResourceAgnostic")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        // DELETE /{resourceType} - Conditional Delete (agnostic, no ID in URL, uses query string)
        // IMPORTANT: Must be registered BEFORE DELETE /{resourceType}/{id} to match correctly
        endpoints.MapDelete("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, CancellationToken ct) =>
            HandleConditionalDeleteResource(context, resourceType, mediator, ct))
            .WithName("ConditionalDeleteResourceAgnostic")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, _contentTypeApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest);

        // DELETE /{resourceType}/{id} - Delete resource (agnostic)
        endpoints.MapDelete("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandleDeleteResource(context, ExtractTenantId(context), resourceType, id, mediator, logger, ct))
            .WithName("DeleteResourceAgnostic")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);


        // GET /{resourceType} - Search resources (agnostic)
        endpoints.MapGet("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] IQueryParameterParser queryParser,
            [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory, [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandleSearchResource(context, ExtractTenantId(context), resourceType, mediator, queryParser, searchOptionsBuilderFactory, logger, ct))
            .WithName("SearchResourceAgnostic")
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status400BadRequest);

        // POST /{resourceType} - Create resource with server-assigned ID (agnostic)
        endpoints.MapPost("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
            [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandlePostResource(context, ExtractTenantId(context), resourceType, mediator, memoryStreamManager, logger, ct))
            .WithName("PostResourceAgnostic")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        // POST / - Transaction/Batch bundle (agnostic)
        endpoints.MapPost("/", (HttpContext context, [FromServices] BundleProcessor bundleProcessor,
            [FromServices] StreamingBundleParser streamingParser, [FromServices] ILogger<Program> logger, CancellationToken ct) =>
            HandleBundle(context, ExtractTenantId(context), bundleProcessor, streamingParser, logger, ct))
            .WithName("BundleAgnostic")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status501NotImplemented);

        return endpoints;
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
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("GET /tenant/{TenantId}/{ResourceType}/{Id}", tenantId, resourceType, id);

        // Validate resource type exists
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.NotFound(new { error = $"Resource type '{resourceType}' not supported" });
        }

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
                context.Response.Headers["ETag"] = ConditionalHeaderParser.FormatETag(conditionalResult.Resource.VersionId);
                context.Response.Headers["Last-Modified"] = ConditionalHeaderParser.FormatHttpDate(conditionalResult.Resource.LastModified);

                logger.LogInformation("Resource {ResourceType}/{Id} not modified, returning 304", resourceType, id);
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            // Resource modified: Add headers and return resource
            context.Response.Headers["ETag"] = ConditionalHeaderParser.FormatETag(conditionalResult.Resource.VersionId);
            context.Response.Headers["Last-Modified"] = ConditionalHeaderParser.FormatHttpDate(conditionalResult.Resource.LastModified);

            logger.LogInformation("Resource {ResourceType}/{Id} modified, returning resource", resourceType, id);
            return Results.Bytes(conditionalResult.Resource.ResourceBytes, _contentTypeApplicationFhirJson);
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

        // ALWAYS include ETag and Last-Modified headers in normal GET responses
        context.Response.Headers["ETag"] = ConditionalHeaderParser.FormatETag(result.VersionId);
        context.Response.Headers["Last-Modified"] = result.LastModified.ToString("R");

        // Return raw JSON bytes (zero-copy serialization)
        return Results.Bytes(result.ResourceBytes, _contentTypeApplicationFhirJson);
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
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("PUT /tenant/{TenantId}/{ResourceType}/{Id}", tenantId, resourceType, id);

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.BadRequest(new { error = $"Resource type '{resourceType}' not supported" });
        }

        // Read request body
        ResourceJsonNode jsonNode;
        await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("request-body"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;
            jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
        }

        // Validate resource type matches
        if (!string.Equals(jsonNode.ResourceType, resourceType, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Resource type mismatch: expected '{ExpectedType}', got '{ActualType}'",
                resourceType,
                jsonNode.ResourceType);
            return Results.BadRequest(new { error = $"Resource type must be '{resourceType}', got '{jsonNode.ResourceType}'" });
        }

        // Extract deferred write coordinator from HttpContext if in bundle context
        var coordinator = context.Items.TryGetValue("DeferredWriteCoordinator", out var coordinatorObj)
            ? coordinatorObj as DeferredWriteCoordinator
            : null;

        // Extract validation tier preference from Prefer header, or from HttpContext.Items if in bundle
        var validationOverride = PreferHeaderParser.TryParseValidationLevel(context.Request.Headers, logger);
        if (!validationOverride.HasValue &&
            context.Items.TryGetValue("ValidationTierOverride", out var contextOverride) &&
            contextOverride is ValidationTier bundleValidationTier)
        {
            validationOverride = bundleValidationTier;
            logger.LogDebug("Using bundle validation tier override: {ValidationTier}", validationOverride.Value);
        }

        // Send generic command with optional coordinator and validation override
        var command = new CreateOrUpdateResourceCommand(resourceType, id, jsonNode, System.Net.Http.HttpMethod.Put, coordinator, null, validationOverride);
        ResourceKey result = await mediator.SendAsync(command, ct);

        // Add ETag and Preference-Applied headers
        context.Response.Headers.Append("ETag", $"W/\"{result.VersionId}\"");
        if (validationOverride.HasValue)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(validationOverride.Value));
        }

        // Determine if created or updated
        bool isCreated = result.VersionId == "1";

        if (isCreated)
        {
            logger.LogInformation("Created {ResourceType}/{Id} (version {Version}) in tenant {TenantId}", resourceType, result.Id, result.VersionId, tenantId);
            return Results.Created($"/tenant/{tenantId}/{resourceType}/{result.Id}", new
            {
                resourceType = resourceType,
                id = result.Id,
                meta = new { versionId = result.VersionId }
            });
        }

        logger.LogInformation("Updated {ResourceType}/{Id} (version {Version}) in tenant {TenantId}", resourceType, result.Id, result.VersionId, tenantId);
        return Results.Ok(new
        {
            resourceType = resourceType,
            id = result.Id,
            meta = new { versionId = result.VersionId }
        });
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
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("GET /tenant/{TenantId}/{ResourceType}?{QueryString}", tenantId, resourceType, context.Request.QueryString);

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.NotFound(new { error = $"Resource type '{resourceType}' not supported" });
        }

        // Get tenant configuration to determine FHIR version
        if (!context.Items.TryGetValue("TenantConfiguration", out var tenantConfigObj) ||
            tenantConfigObj is not Domain.Models.TenantConfiguration tenantConfig)
        {
            logger.LogError("TenantConfiguration not found in HttpContext.Items");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        // Get version-specific search options builder
        var fhirSpec = Ignixa.SourceNodeSerialization.FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var searchOptionsBuilder = searchOptionsBuilderFactory.Create(fhirSpec);

        // Parse query parameters
        var queryParameters = queryParser.Parse(context.Request.Query);

        // Build SearchOptions
        var searchOptions = searchOptionsBuilder.Build(resourceType, queryParameters);

        // Send search query
        var searchQuery = new SearchResourcesQuery(resourceType, searchOptions);
        SearchResourcesResult result = await mediator.SendAsync(searchQuery, ct);

        // Build self link
        string selfLink = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";

        // Set response headers
        context.Response.ContentType = "application/fhir+json; charset=utf-8";

        // Stream Bundle response
        await StreamingBundleSerializer.SerializeAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            selfLink: selfLink,
            nextLink: null,
            pretty: false,
            cancellationToken: ct);

        // Response already written to the body, return empty result
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
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("POST /tenant/{TenantId}/{ResourceType}", tenantId, resourceType);

        // Check for If-None-Exist header (conditional create)
        if (context.Request.Headers.TryGetValue("If-None-Exist", out var ifNoneExist))
        {
            logger.LogInformation(
                "Conditional create detected for {ResourceType} with search criteria: {SearchCriteria}",
                resourceType,
                ifNoneExist.ToString());

            // Read request body
            string requestBody;
            await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("request-body"))
            {
                await context.Request.Body.CopyToAsync(memoryStream, ct);
                requestBody = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
            }

            // Execute conditional create
            var command = new Application.Features.ConditionalOperations.ConditionalCreate.ConditionalCreateCommand(
                TenantId: tenantId,
                ResourceType: resourceType,
                IfNoneExist: ifNoneExist.ToString(),
                RequestBody: requestBody,
                RequestId: context.TraceIdentifier);

            var result = await mediator.SendAsync(command, ct);

            // Return appropriate status code based on WasCreated
            var statusCode = result.WasCreated ? StatusCodes.Status201Created : StatusCodes.Status200OK;

            // Add headers
            context.Response.Headers.Append("ETag", $"W/\"{result.Resource.VersionId}\"");
            context.Response.Headers.Append("Last-Modified", result.Resource.LastModified.ToString("R"));

            if (result.WasCreated)
            {
                // 201 Created - return Location header
                var location = $"/tenant/{tenantId}/{resourceType}/{result.Resource.ResourceId}";
                context.Response.Headers.Append("Location", location);

                logger.LogInformation(
                    "Conditional create: Created new {ResourceType}/{Id} (version {VersionId})",
                    result.Resource.ResourceType,
                    result.Resource.ResourceId,
                    result.Resource.VersionId);

                return Results.Created(location, new
                {
                    resourceType = result.Resource.ResourceType,
                    id = result.Resource.ResourceId,
                    meta = new { versionId = result.Resource.VersionId }
                });
            }
            else
            {
                // 200 OK - existing resource returned
                logger.LogInformation(
                    "Conditional create: Returned existing {ResourceType}/{Id} (version {VersionId})",
                    result.Resource.ResourceType,
                    result.Resource.ResourceId,
                    result.Resource.VersionId);

                // Serialize the resource and return
                var resourceJson = result.Resource.Resource.SerializeToString();
                return Results.Content(resourceJson, _contentTypeApplicationFhirJson, statusCode: statusCode);
            }
        }

        // Standard create (no If-None-Exist header)

        // Check if we're in a bundle context with a pre-assigned ID (for urn:uuid references)
        string id;
        if (context.Items.TryGetValue("BundleAssignedResourceId", out var assignedIdObj) && assignedIdObj is string assignedId)
        {
            id = assignedId;
            logger.LogInformation("Using bundle-assigned ID {Id} for new {ResourceType} in tenant {TenantId}", id, resourceType, tenantId);
        }
        else
        {
            // Generate ID
            id = Guid.NewGuid().ToString("N");
            logger.LogInformation("Generated ID {Id} for new {ResourceType} in tenant {TenantId}", id, resourceType, tenantId);
        }

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.BadRequest(new { error = $"Resource type '{resourceType}' not supported" });
        }

        // Read request body
        ResourceJsonNode jsonNode;
        await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("request-body"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;
            jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
        }

        // Validate resource type matches
        if (!string.Equals(jsonNode.ResourceType, resourceType, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Resource type mismatch: expected '{ExpectedType}', got '{ActualType}'",
                resourceType,
                jsonNode.ResourceType);
            return Results.BadRequest(new { error = $"Resource type must be '{resourceType}', got '{jsonNode.ResourceType}'" });
        }

        // Extract deferred write coordinator from HttpContext if in bundle context
        var coordinator = context.Items.TryGetValue("DeferredWriteCoordinator", out var coordinatorObj)
            ? coordinatorObj as DeferredWriteCoordinator
            : null;

        // Extract validation tier preference from Prefer header, or from HttpContext.Items if in bundle
        var validationOverride = PreferHeaderParser.TryParseValidationLevel(context.Request.Headers, logger);
        if (!validationOverride.HasValue &&
            context.Items.TryGetValue("ValidationTierOverride", out var contextOverride) &&
            contextOverride is ValidationTier bundleValidationTier)
        {
            validationOverride = bundleValidationTier;
            logger.LogDebug("Using bundle validation tier override: {ValidationTier}", validationOverride.Value);
        }

        // Send generic command with HTTP POST method
        var createCommand = new CreateOrUpdateResourceCommand(resourceType, id, jsonNode, System.Net.Http.HttpMethod.Post, coordinator, null, validationOverride);
        ResourceKey createResult = await mediator.SendAsync(createCommand, ct);

        // Add ETag and Preference-Applied headers
        context.Response.Headers.Append("ETag", $"W/\"{createResult.VersionId}\"");
        if (validationOverride.HasValue)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(validationOverride.Value));
        }

        // POST always creates (returns 201 Created)
        string createLocation = $"/tenant/{tenantId}/{resourceType}/{createResult.Id}";
        logger.LogInformation(
            "Created {ResourceType}/{Id} (version {VersionId}) in tenant {TenantId}",
            resourceType,
            createResult.Id,
            createResult.VersionId,
            tenantId);

        return Results.Created(createLocation, new
        {
            resourceType,
            id = createResult.Id,
            meta = new { versionId = createResult.VersionId }
        });
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
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("DELETE /tenant/{TenantId}/{ResourceType}/{Id}", tenantId, resourceType, id);

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.NotFound(new { error = $"Resource type '{resourceType}' not supported" });
        }

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
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("POST /tenant/{TenantId} (Bundle)", tenantId);

        // Extract validation preference from Prefer header (applies to all entries in bundle)
        var validationOverride = PreferHeaderParser.TryParseValidationLevel(context.Request.Headers, logger);
        if (validationOverride.HasValue)
        {
            context.Items["ValidationTierOverride"] = validationOverride;
            logger.LogInformation("Bundle validation preference set to {ValidationTier}", validationOverride.Value);
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
                responseJson = JsonSerializer.Serialize(responseBundle, BundleJsonOptions);
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

            return Results.Content(responseJson, _contentTypeApplicationFhirJson);
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

                // Stream responses directly to HTTP (headers are now locked)
                await StreamingBundleSerializer.SerializeStreamAsync(
                    outputStream: context.Response.Body,
                    bundleType: "batch-response",
                    entryResponses: streamingContext.ResponseStream,
                    total: null,
                    selfLink: null,
                    nextLink: null,
                    pretty: false,
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
        CancellationToken ct)
    {
        var tenantId = ExtractTenantId(context);
        return await HandleConditionalUpdateResourceExplicit(context, tenantId, resourceType, mediator, ct);
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
        CancellationToken ct)
    {
        // Extract query string (search criteria)
        var queryString = context.Request.QueryString.Value;

        if (string.IsNullOrWhiteSpace(queryString) || queryString == "?")
        {
            return Results.BadRequest(new
            {
                error = "Conditional update requires search parameters in query string"
            });
        }

        // Remove leading '?'
        var searchCriteria = queryString.TrimStart('?');

        // Read request body
        using var memoryStream = new MemoryStream();
        await context.Request.Body.CopyToAsync(memoryStream, ct);
        var requestBody = Encoding.UTF8.GetString(memoryStream.ToArray());

        // Execute conditional update via mediator
        var command = new Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate.ConditionalUpdateCommand(
            TenantId: tenantId,
            ResourceType: resourceType,
            SearchCriteria: searchCriteria,
            RequestBody: requestBody,
            RequestId: context.TraceIdentifier);

        var result = await mediator.SendAsync(command, ct);

        // Return 201 Created or 200 OK based on WasCreated
        var statusCode = result.WasCreated ? StatusCodes.Status201Created : StatusCodes.Status200OK;

        // Add ETag header
        context.Response.Headers.Append("ETag", $"W/\"{result.Resource.VersionId}\"");

        // Serialize resource to JSON
        var resourceJson = result.Resource.Resource.SerializeToString();

        if (result.WasCreated)
        {
            // 201 Created - include Location header
            var location = $"/tenant/{tenantId}/{resourceType}/{result.Resource.ResourceId}";
            return Results.Created(location, resourceJson);
        }
        else
        {
            // 200 OK
            return Results.Content(resourceJson, _contentTypeApplicationFhirJson, statusCode: statusCode);
        }
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
    /// DELETE /{resourceType} - Conditional Delete (tenant-agnostic)
    /// Delegates to tenant-explicit handler with extracted tenant ID.
    /// </summary>
    private static async Task<IResult> HandleConditionalDeleteResource(
        HttpContext context,
        string resourceType,
        IMediator mediator,
        CancellationToken ct)
    {
        var tenantId = ExtractTenantId(context);
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

        if (string.IsNullOrWhiteSpace(queryString) || queryString == "?")
        {
            return Results.BadRequest(new
            {
                error = "Conditional delete requires search parameters in query string"
            });
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
            var outcome = new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "information",
                        code = "informational",
                        diagnostics = result.IsPartialDelete
                            ? $"Partial delete: Deleted {result.DeletedCount} of {result.TotalMatches} matching resources (limit: {count}). " +
                              $"Deleted IDs: {string.Join(", ", result.DeletedIds)}"
                            : $"Deleted {result.DeletedCount} matching resource(s). Deleted IDs: {string.Join(", ", result.DeletedIds)}"
                    }
                }
            };

            return Results.Json(outcome, statusCode: StatusCodes.Status200OK, contentType: "application/fhir+json");
        }
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
