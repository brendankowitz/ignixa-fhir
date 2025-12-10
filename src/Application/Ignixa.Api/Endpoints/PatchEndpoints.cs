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
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
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
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
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
            [FromServices] IMediator mediator, [FromServices] RecyclableMemoryStreamManager memoryStreamManager, [FromServices] ILoggerFactory loggerFactory, CancellationToken ct) =>
            HandlePatchResource(context, tenantId, resourceType, id, mediator, memoryStreamManager, loggerFactory, ct))
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
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
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

        // Parse request body - detect JSON Patch vs FHIR Parameters patch
        ResourceJsonNode patchDocument;
        await using (var memoryStream = memoryStreamManager.GetStream("patch-request"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            // Check Content-Type header to detect JSON Patch (RFC 6902)
            var contentType = context.Request.ContentType;
            if (!string.IsNullOrEmpty(contentType) && contentType.Contains("application/json-patch+json", StringComparison.OrdinalIgnoreCase))
            {
                throw new Domain.Exceptions.NotImplementedException(
                    "JSON Patch (RFC 6902) is not yet supported. Please use FHIRPath Patch with Content-Type: application/fhir+json. " +
                    "See FHIR R4 Section 3.1.0.7.1 for FHIRPath Patch format using Parameters resource.");
            }

            // Peek at first character to detect array format (JSON Patch)
            memoryStream.Position = 0;
            using var reader = new System.IO.StreamReader(memoryStream, System.Text.Encoding.UTF8, leaveOpen: true);
            var firstChar = (char)reader.Peek();
            if (firstChar == '[')
            {
                throw new Domain.Exceptions.NotImplementedException(
                    "JSON Patch (RFC 6902) array format is not yet supported. Please use FHIRPath Patch (Parameters resource). " +
                    "See FHIR R4 Section 3.1.0.7.1 for FHIRPath Patch format.");
            }

            memoryStream.Position = 0;
            patchDocument = await JsonSourceNodeFactory.ParseAsync(memoryStream, cancellationToken);
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

        logger.LogInformation("Patched {ResourceType}/{Id} (version {VersionId})", resourceType, id, result.VersionId);
        return FhirResults.Ok(result.Resource, context)
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
            throw new Domain.Exceptions.PreconditionFailedException("Conditional patch requires search parameters in query string");
        }

        // Remove leading '?'
        var searchCriteria = queryString.TrimStart('?');

        // Parse request body - detect JSON Patch vs FHIR Parameters patch
        ResourceJsonNode patchDocument;
        await using (var memoryStream = memoryStreamManager.GetStream("conditional-patch-request"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            // Check Content-Type header to detect JSON Patch (RFC 6902)
            var contentType = context.Request.ContentType;
            if (!string.IsNullOrEmpty(contentType) && contentType.Contains("application/json-patch+json", StringComparison.OrdinalIgnoreCase))
            {
                throw new Domain.Exceptions.NotImplementedException(
                    "JSON Patch (RFC 6902) is not yet supported. Please use FHIRPath Patch with Content-Type: application/fhir+json. " +
                    "See FHIR R4 Section 3.1.0.7.1 for FHIRPath Patch format using Parameters resource.");
            }

            // Peek at first character to detect array format (JSON Patch)
            memoryStream.Position = 0;
            using var reader = new System.IO.StreamReader(memoryStream, System.Text.Encoding.UTF8, leaveOpen: true);
            var firstChar = (char)reader.Peek();
            if (firstChar == '[')
            {
                throw new Domain.Exceptions.NotImplementedException(
                    "JSON Patch (RFC 6902) array format is not yet supported. Please use FHIRPath Patch (Parameters resource). " +
                    "See FHIR R4 Section 3.1.0.7.1 for FHIRPath Patch format.");
            }

            memoryStream.Position = 0;
            patchDocument = await JsonSourceNodeFactory.ParseAsync(memoryStream, cancellationToken);
        }

        // Execute conditional patch
        var command = new ConditionalPatchCommand(
            tenantId,
            resourceType,
            searchCriteria,
            patchDocument,
            context.TraceIdentifier);

        var result = await mediator.SendAsync(command, cancellationToken);

        // Extract return preference from Prefer header (RFC 7240)
        var returnPreference = PreferHeaderParser.TryParseReturnPreference(context.Request.Headers);

        // Determine actual return preference: default to representation (FHIR spec)
        var actualReturnPreference = returnPreference == ReturnPreference.Unspecified
            ? ReturnPreference.Representation
            : returnPreference;

        // Add Preference-Applied header for return preference
        if (returnPreference != ReturnPreference.Unspecified)
        {
            context.Response.Headers.Append("Preference-Applied", PreferHeaderParser.ToPreferenceAppliedHeader(actualReturnPreference));
        }

        if (actualReturnPreference == ReturnPreference.Minimal)
        {
            // return=minimal - return minimal body
            return FhirResults.Ok(result.Resource.Resource, context)
                .WithETag(result.Resource.VersionId)
                .WithLastModified(result.Resource.LastModified)
                .WithMinimalBody(resourceType, result.Resource.ResourceId, result.Resource.VersionId, result.Resource.LastModified);
        }
        else if (actualReturnPreference == ReturnPreference.OperationOutcome)
        {
            // return=OperationOutcome - return OperationOutcome with success message
            var outcome = new OperationOutcomeJsonNode();
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Information,
                Code = OperationOutcomeJsonNode.IssueType.Informational,
                Diagnostics = $"Successfully patched {resourceType}/{result.Resource.ResourceId}"
            });
            return FhirResults.Ok(outcome, context);
        }
        else
        {
            // return=representation - return full resource
            return FhirResults.Ok(result.Resource.Resource, context)
                .WithETag(result.Resource.VersionId)
                .WithLastModified(result.Resource.LastModified);
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

}
