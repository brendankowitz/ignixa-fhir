// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.Operations.Features.Validate;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;
using System.Text.Json.Nodes;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Registers FHIR operation endpoints ($validate, $expand, etc.)
/// </summary>
public static class OperationEndpoints
{
    /// <summary>
    /// Registers FHIR operation endpoints.
    ///
    /// Supported Operations:
    /// - POST /$validate - System-level validation (any resource type)
    /// - POST /{resourceType}/$validate - Type-level validation
    /// - POST /{resourceType}/{id}/$validate - Instance-level validation
    /// </summary>
    public static IEndpointRouteBuilder MapOperationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOperationTenantEndpoints();
        endpoints.MapOperationAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit operation endpoints (/tenant/{tenantId}/...).
    /// </summary>
    private static IEndpointRouteBuilder MapOperationTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Create a route group for operations with tenant ID validation
        var tenantGroup = endpoints
            .MapGroup("/tenant/{tenantId:int}")
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        // POST /{resourceType}/$validate - Type-level validation
        tenantGroup.MapPost("/{resourceType}/$validate", HandleValidateResource)
            .WithName("ValidateResource")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // POST /{resourceType}/{id}/$validate - Instance-level validation
        tenantGroup.MapPost("/{resourceType}/{id}/$validate", HandleValidateResourceInstance)
            .WithName("ValidateResourceInstance")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic operation endpoints (/).
    /// Only enabled in single-tenant mode by TenantResolutionMiddleware.
    /// </summary>
    private static IEndpointRouteBuilder MapOperationAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /$validate - System-level validation
        endpoints.MapPost("/$validate", HandleValidateResourceSystem)
            .WithName("ValidateResourceSystem")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // POST /{resourceType}/$validate - Type-level validation (agnostic route)
        endpoints.MapPost("/{resourceType}/$validate", HandleValidateResourceAgnostic)
            .WithName("ValidateResourceAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // POST /{resourceType}/{id}/$validate - Instance-level validation (agnostic route)
        endpoints.MapPost("/{resourceType}/{id}/$validate", HandleValidateResourceInstanceAgnostic)
            .WithName("ValidateResourceInstanceAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    /// <summary>
    /// Creates a FHIR OperationOutcome error response with a single issue.
    /// </summary>
    private static object CreateOperationOutcomeError(string severity, string code, string diagnostics) =>
        new
        {
            resourceType = "OperationOutcome",
            issue = new[]
            {
                new
                {
                    severity,
                    code,
                    diagnostics
                }
            }
        };

    /// <summary>
    /// Handles tenant-explicit $validate for a specific resource type.
    /// POST /tenant/{tenantId}/{resourceType}/$validate
    /// </summary>
    private static async Task<IResult> HandleValidateResource(
        HttpContext context,
        int tenantId,
        string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        return await HandleValidateResourceInternal(context, tenantId, resourceType, null, mediator, memoryStreamManager, cancellationToken);
    }

    /// <summary>
    /// Handles system-level $validate (no resource type specified).
    /// POST /$validate
    /// </summary>
    private static async Task<IResult> HandleValidateResourceSystem(
        HttpContext context,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // For system-level validation, determine tenant from context
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcomeError(
                "error",
                "required",
                "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/$validate"));
        }

        return await HandleValidateResourceInternal(context, tenantId, null, null, mediator, memoryStreamManager, cancellationToken);
    }

    /// <summary>
    /// Handles agnostic $validate for a specific resource type (single-tenant only).
    /// POST /{resourceType}/$validate
    /// </summary>
    private static async Task<IResult> HandleValidateResourceAgnostic(
        HttpContext context,
        string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // For agnostic route, determine tenant from context
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcomeError(
                "error",
                "required",
                "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/{resourceType}/$validate"));
        }

        return await HandleValidateResourceInternal(context, tenantId, resourceType, null, mediator, memoryStreamManager, cancellationToken);
    }

    /// <summary>
    /// Handles tenant-explicit instance-level $validate for a specific resource instance.
    /// POST /tenant/{tenantId}/{resourceType}/{id}/$validate
    /// </summary>
    private static async Task<IResult> HandleValidateResourceInstance(
        HttpContext context,
        int tenantId,
        string resourceType,
        string id,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        return await HandleValidateResourceInternal(context, tenantId, resourceType, id, mediator, memoryStreamManager, cancellationToken);
    }

    /// <summary>
    /// Handles agnostic instance-level $validate for a specific resource instance (single-tenant only).
    /// POST /{resourceType}/{id}/$validate
    /// </summary>
    private static async Task<IResult> HandleValidateResourceInstanceAgnostic(
        HttpContext context,
        string resourceType,
        string id,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // For agnostic route, determine tenant from context
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcomeError(
                "error",
                "required",
                "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/{resourceType}/{id}/$validate"));
        }

        return await HandleValidateResourceInternal(context, tenantId, resourceType, id, mediator, memoryStreamManager, cancellationToken);
    }

    /// <summary>
    /// Core validation handler used by all validation endpoints.
    /// </summary>
    private static async Task<IResult> HandleValidateResourceInternal(
        HttpContext context,
        int tenantId,
        string? resourceType,
        string? instanceId,
        IMediator mediator,
        RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // Use memory stream to read and preserve the request body
        using var memoryStream = memoryStreamManager.GetStream();
        await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        if (memoryStream.Length == 0)
        {
            return Results.BadRequest(CreateOperationOutcomeError(
                "error",
                "required",
                "Request body must contain a FHIR resource to validate"));
        }

        // Parse JSON using JsonSourceNodeFactory
        ResourceJsonNode jsonNode;
        try
        {
            jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
        }
        catch
        {
            return Results.BadRequest(CreateOperationOutcomeError(
                "error",
                "invalid",
                "Request body must be valid JSON"));
        }

        // Extract parameters (mode and profile) from POST body if using Parameters resource
        string? mode = null;
        string? profile = null;

        if (jsonNode.ResourceType == "Parameters")
        {
            // Use ParametersJsonNode model for strongly-typed parameter access
            var parametersNode = jsonNode.As<ParametersJsonNode>();

            foreach (var param in parametersNode.Parameter)
            {
                switch (param.Name)
                {
                    case "mode":
                        mode = param.GetValueAs<string>("valueCode");
                        break;
                    case "profile":
                        profile = param.GetValueAs<string>("valueUri");
                        break;
                    case "resource":
                        // Extract the nested resource using the resource property
                        var resourceNode = param.GetValue("resource");
                        if (resourceNode is not null)
                        {
                            var resourceJson = resourceNode.ToJsonString();
                            if (resourceJson is not null)
                            {
                                using var resourceStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(resourceJson));
                                jsonNode = await JsonSourceNodeFactory.Parse(resourceStream);
                            }
                        }
                        break;
                }
            }
        }

        // Validate mode + endpoint combination (FHIR spec requirement)
        if (!string.IsNullOrEmpty(mode))
        {
            var normalizedMode = mode.ToUpperInvariant();
            if ((normalizedMode == "UPDATE" || normalizedMode == "DELETE") && string.IsNullOrEmpty(instanceId))
            {
                return Results.BadRequest(CreateOperationOutcomeError(
                    "error",
                    "invalid",
                    $"Validation mode '{mode}' requires instance-level endpoint: [base]/{{resourceType}}/{{id}}/$validate"));
            }
        }

        // Create validation command
        var command = new ValidateResourceCommand(
            tenantId,
            resourceType,
            jsonNode,
            mode,
            profile,
            instanceId);

        // Execute validation
        var result = await mediator.SendAsync(command, cancellationToken);

        // Return OperationOutcome
        return Results.Ok(result.OperationOutcome);
    }
}
