// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Api.Extensions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Api.Infrastructure;
using Ignixa.Application.Features.ConditionalOperations.ConditionalPatch;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Infrastructure;
using Ignixa.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using FhirOperationOutcomeIssue = Ignixa.Models.OperationOutcomeIssue;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Registers FHIR PATCH endpoints using FHIRPath Patch (Parameters resource).
/// Supports both direct PATCH by ID and conditional PATCH via query parameters.
/// </summary>
public static class PatchEndpoints
{
    private const string JsonPatchContentType = "application/json-patch+json";

    private const string FhirPathPatchRequirement =
        "Use FHIRPath Patch: send a Parameters resource with Content-Type application/fhir+json " +
        "(in a bundle, put the Parameters resource in entry.resource). See http://hl7.org/fhir/fhirpatch.html.";

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
    /// JSON Patch (application/json-patch+json, array bodies, or Binary entries) is rejected with 400.
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
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>()
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
            .Produces<object>(StatusCodes.Status412PreconditionFailed, KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status400BadRequest);

        // PATCH /{resourceType}/{id} - Direct Patch
        tenantGroup.MapPatch("/{resourceType}/{id}", (HttpContext context, int tenantId, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePatchResource(context, tenantId, resourceType, id, mediator, memoryStreamManager, loggerFactory, ct))
            .WithName("PatchResource")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

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
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>()
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        // PATCH /{resourceType} - Conditional Patch (agnostic)
        // IMPORTANT: Must be registered BEFORE PATCH /{resourceType}/{id} to match correctly
        agnosticGroup.MapPatch("/{resourceType}", (HttpContext context, string resourceType,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, CancellationToken ct) =>
            HandleConditionalPatchResource(context, resourceType, mediator, memoryStreamManager, fhirContextAccessor, ct))
            .WithName("ConditionalPatchResourceAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status412PreconditionFailed, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest);

        // PATCH /{resourceType}/{id} - Direct Patch (agnostic)
        agnosticGroup.MapPatch("/{resourceType}/{id}", (HttpContext context, string resourceType, string id,
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager, [FromServices] IFhirRequestContextAccessor fhirContextAccessor, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePatchResource(context, fhirContextAccessor.RequestContext!.TenantId, resourceType, id, mediator, memoryStreamManager, loggerFactory, ct))
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
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(PatchEndpoints).FullName!);
        logger.LogInformation("PATCH /tenant/{TenantId}/{ResourceType}/{Id}", tenantId, resourceType, id);

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.NotFound(new { error = $"Resource type '{resourceType}' not supported" });
        }

        var patchDocument = await ReadFhirPathPatchDocumentAsync(context, memoryStreamManager, "patch-request", cancellationToken);

        // Execute patch via mediator
        var command = new PatchResourceCommand(
            tenantId,
            resourceType,
            id,
            patchDocument,
            IfMatch: Application.Utilities.ConditionalHeaderParser.ParseIfNoneMatch(context.Request.Headers["If-Match"].FirstOrDefault()));

        var result = await mediator.SendAsync(command, cancellationToken);

        if (result == null)
        {
            logger.LogInformation("Resource {ResourceType}/{Id} not found for patch", resourceType, id);
            return Results.NotFound();
        }

        // Extract return preference from Prefer header (RFC 7240)
        var returnPreference = PreferHeaderParser.TryParseReturnPreference(context.Request.Headers, logger);

        // Determine actual return preference: default to representation (FHIR spec)
        var actualReturnPreference = returnPreference == ReturnPreference.Unspecified
            ? ReturnPreference.Representation
            : returnPreference;

        // Add Preference-Applied header for return preference
        if (returnPreference != ReturnPreference.Unspecified)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(actualReturnPreference));
        }

        logger.LogInformation("Patched {ResourceType}/{Id} (version {VersionId})", resourceType, id, result.VersionId);

        var location = BuildVersionedLocation(context, tenantId, resourceType, result.ResourceId, result.VersionId);

        if (actualReturnPreference == ReturnPreference.Minimal)
        {
            // return=minimal - return headers only, no body (FHIR spec compliant)
            return new FhirResult(StatusCodes.Status200OK)
                .WithLocation(location)
                .WithETag(result.VersionId)
                .WithLastModified(result.LastModified);
        }
        else if (actualReturnPreference == ReturnPreference.OperationOutcome)
        {
            // return=OperationOutcome - return OperationOutcome with success message
            var outcome = new OperationOutcome();
            outcome.Issue.Add(new FhirOperationOutcomeIssue
            {
                SeverityCode = FhirOperationOutcomeIssue.IssueSeverityCode.Information,
                IssueTypeCode = FhirOperationOutcomeIssue.IssueTypeCommon.Informational,
                Diagnostics = $"Successfully patched {resourceType}/{id}"
            });
            return FhirResults.Ok(outcome, context)
                .WithLocation(location);
        }
        else
        {
            // return=representation - return full resource
            return FhirResults.Ok(result.Resource, context)
                .WithLocation(location)
                .WithETag(result.VersionId)
                .WithLastModified(result.LastModified);
        }
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
        IFhirRequestContextAccessor fhirContextAccessor,
        CancellationToken cancellationToken)
    {
        var tenantId = fhirContextAccessor.RequestContext!.TenantId;
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

        // FHIR spec: Bundle cannot be used in conditional operations
        // Reject with "not selective enough" error (matches test expectations)
        if (string.Equals(resourceType, KnownResourceTypes.Bundle, StringComparison.OrdinalIgnoreCase))
        {
            throw new Domain.Exceptions.BadRequestException(
                string.Format(Ignixa.Search.Resources.ConditionalOperationNotSelectiveEnough, resourceType));
        }

        if (string.IsNullOrWhiteSpace(queryString) || queryString == "?")
        {
            // Return 400 Bad Request for missing search criteria (consistent with conditional update/delete)
            throw new Domain.Exceptions.BadRequestException("Conditional patch requires search parameters in query string");
        }

        // Remove leading '?'
        var searchCriteria = queryString.TrimStart('?');

        var patchDocument = await ReadFhirPathPatchDocumentAsync(context, memoryStreamManager, "conditional-patch-request", cancellationToken);

        // Execute conditional patch
        var command = new ConditionalPatchCommand(
            tenantId,
            resourceType,
            searchCriteria,
            patchDocument,
            context.TraceIdentifier);

        var result = await mediator.SendAsync(command, cancellationToken);

        // Create logger for Prefer header parsing
        var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(typeof(PatchEndpoints).FullName!);

        // Extract return preference from Prefer header (RFC 7240)
        var returnPreference = PreferHeaderParser.TryParseReturnPreference(context.Request.Headers, logger);

        // Determine actual return preference: default to representation (FHIR spec)
        var actualReturnPreference = returnPreference == ReturnPreference.Unspecified
            ? ReturnPreference.Representation
            : returnPreference;

        // Add Preference-Applied header for return preference
        if (returnPreference != ReturnPreference.Unspecified)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(actualReturnPreference));
        }

        var location = BuildVersionedLocation(context, tenantId, resourceType, result.Resource.ResourceId, result.Resource.VersionId);

        if (actualReturnPreference == ReturnPreference.Minimal)
        {
            // return=minimal - return headers only, no body (FHIR spec compliant)
            return new FhirResult(StatusCodes.Status200OK)
                .WithLocation(location)
                .WithETag(result.Resource.VersionId)
                .WithLastModified(result.Resource.LastModified);
        }
        else if (actualReturnPreference == ReturnPreference.OperationOutcome)
        {
            // return=OperationOutcome - return OperationOutcome with success message
            var outcome = new OperationOutcome();
            outcome.Issue.Add(new FhirOperationOutcomeIssue
            {
                SeverityCode = FhirOperationOutcomeIssue.IssueSeverityCode.Information,
                IssueTypeCode = FhirOperationOutcomeIssue.IssueTypeCommon.Informational,
                Diagnostics = $"Successfully patched {resourceType}/{result.Resource.ResourceId}"
            });
            return FhirResults.Ok(outcome, context)
                .WithLocation(location);
        }
        else
        {
            // return=representation - return full resource
            return FhirResults.Ok(result.Resource.Resource, context)
                .WithLocation(location)
                .WithETag(result.Resource.VersionId)
                .WithLastModified(result.Resource.LastModified);
        }
    }

    /// <summary>
    /// Reads a FHIRPath Patch Parameters body and rejects every other patch shape with 400: the JSON Patch
    /// content type, a bare JSON array, an empty body, or any Binary resource. Bundle PATCH entries arrive
    /// with Content-Type application/fhir+json, so a Binary-wrapped patch can only be caught from the body.
    /// </summary>
    /// <exception cref="Domain.Exceptions.BadRequestException">The body is not a FHIRPath Patch document.</exception>
    private static async Task<ResourceJsonNode> ReadFhirPathPatchDocumentAsync(
        HttpContext context,
        RecyclableMemoryStreamManager memoryStreamManager,
        string streamTag,
        CancellationToken cancellationToken)
    {
        var contentType = context.Request.ContentType;
        if (!string.IsNullOrEmpty(contentType) &&
            contentType.Contains(JsonPatchContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new Domain.Exceptions.BadRequestException(
                $"JSON Patch (Content-Type: {JsonPatchContentType}) is not supported. {FhirPathPatchRequirement}");
        }

        await using var memoryStream = memoryStreamManager.GetStream(streamTag);
        await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);

        memoryStream.Position = 0;
        switch (ReadFirstSignificantByte(memoryStream))
        {
            case -1:
                throw new Domain.Exceptions.BadRequestException(
                    $"PATCH requires a request body. {FhirPathPatchRequirement}");
            case '[':
                throw new Domain.Exceptions.BadRequestException(
                    $"JSON Patch (RFC 6902) array bodies are not supported. {FhirPathPatchRequirement}");
        }

        memoryStream.Position = 0;
        var patchDocument = await JsonSourceNodeFactory.ParseAsync(memoryStream, cancellationToken);

        if (string.Equals(patchDocument.ResourceType, "Binary", StringComparison.Ordinal))
        {
            var binaryContentType = patchDocument.MutableNode["contentType"] is System.Text.Json.Nodes.JsonValue value &&
                value.TryGetValue<string>(out var declared)
                ? declared
                : "unspecified";
            throw new Domain.Exceptions.BadRequestException(
                $"A Binary patch payload (contentType '{binaryContentType}') is not supported; JSON Patch and XML Patch are not supported. {FhirPathPatchRequirement}");
        }

        return patchDocument;
    }

    /// <summary>
    /// Returns the first byte after JSON whitespace and any UTF-8 BOM, or -1 if the stream has no content.
    /// </summary>
    private static int ReadFirstSignificantByte(Stream stream)
    {
        int value;
        do
        {
            value = stream.ReadByte();
        }
        while (value is ' ' or '\t' or '\r' or '\n' or 0xEF or 0xBB or 0xBF);

        return value;
    }

    /// <summary>
    /// Builds the absolute versioned Location URL, in the tenant-agnostic form when the request arrived on an agnostic route.
    /// </summary>
    private static string BuildVersionedLocation(HttpContext context, int tenantId, string resourceType, string id, string versionId)
    {
        var isAgnosticRoute = context.Items.TryGetValue("IsAgnosticRoute", out var flag) && flag is true;
        var path = isAgnosticRoute
            ? $"/{resourceType}/{id}/_history/{versionId}"
            : $"/tenant/{tenantId}/{resourceType}/{id}/_history/{versionId}";
        return $"{context.Request.Scheme}://{context.Request.Host}{path}";
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
