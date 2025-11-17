// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.Operations.Features.Validate;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Abstractions;
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
    /// - GET /ValueSet/$expand - Expand a ValueSet to a list of codes
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

        // GET /ValueSet/$expand - Expand a ValueSet to a list of codes
        endpoints.MapGet("/ValueSet/$expand", async (
            HttpContext httpContext,
            [FromQuery] string? url,
            [FromQuery] string? filter,
            [FromQuery] int? count,
            [FromQuery] int? offset,
            [FromServices] ITerminologyService terminologyService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return Results.BadRequest(CreateOperationOutcomeError(
                    "error",
                    "required",
                    "Parameter 'url' is required"));
            }

            var parameters = new ExpansionParameters(
                Url: url,
                Filter: filter,
                Count: count,
                Offset: offset,
                IncludeDesignations: false);

            var result = await terminologyService.ExpandValueSetAsync(parameters, cancellationToken);

            if (result == null)
            {
                return Results.NotFound(CreateOperationOutcomeError(
                    "error",
                    "not-found",
                    $"ValueSet '{url}' not found or not expanded"));
            }

            // Convert to FHIR ValueSet resource with expansion
            var valueSetJson = new
            {
                resourceType = "ValueSet",
                url,
                expansion = new
                {
                    identifier = result.Identifier,
                    timestamp = result.Timestamp.ToString("o"),
                    total = result.Total,
                    offset = result.Offset,
                    contains = result.Contains.Select(c => new
                    {
                        system = c.System,
                        code = c.Code,
                        display = c.Display,
                        version = c.Version,
                        inactive = c.Inactive
                    }).ToList()
                }
            };

            return Results.Ok(valueSetJson);
        })
        .WithName("ExpandValueSet")
        .WithTags("Operations")
        .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
        .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
        .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
        .WithOpenApi(operation =>
        {
            operation.Summary = "$expand - Expand a ValueSet to a list of codes";
            operation.Description = "Returns the expansion of a ValueSet (list of codes). Uses pre-computed expansions when available.";
            return operation;
        });

        // POST /ConceptMap/$translate - Translate code using ConceptMap
        endpoints.MapPost("/ConceptMap/$translate", async (
            HttpContext httpContext,
            [FromBody] TranslateRequestBody body,
            [FromServices] ITerminologyService terminologyService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.System))
            {
                return Results.BadRequest(CreateOperationOutcomeError(
                    "error",
                    "required",
                    "Parameters 'code' and 'system' are required"));
            }

            var parameters = new TranslateParameters(
                Url: body.Url,
                ConceptMapVersion: body.ConceptMapVersion,
                Code: body.Code,
                System: body.System,
                Version: body.Version,
                Source: body.Source,
                Target: body.Target,
                TargetSystem: body.TargetSystem,
                Reverse: body.Reverse ?? false);

            var result = await terminologyService.TranslateCodeAsync(parameters, cancellationToken);

            // Convert to FHIR Parameters resource
            var parameters_list = new List<object>
            {
                new
                {
                    name = "result",
                    valueBoolean = result.Result
                }
            };

            if (result.Message != null)
            {
                parameters_list.Add(new
                {
                    name = "message",
                    valueString = result.Message
                });
            }

            foreach (var match in result.Matches)
            {
                var parts = new List<object>
                {
                    new { name = "equivalence", valueCode = match.Equivalence },
                    new { name = "concept", valueCoding = new
                    {
                        system = match.Concept.System,
                        code = match.Concept.Code,
                        display = match.Concept.Display
                    }},
                    new { name = "source", valueUri = match.Source }
                };

                if (match.Comment != null)
                {
                    parts.Add(new { name = "comment", valueString = match.Comment });
                }

                parameters_list.Add(new
                {
                    name = "match",
                    part = parts
                });
            }

            var parametersResponse = new
            {
                resourceType = "Parameters",
                parameter = parameters_list
            };

            return Results.Ok(parametersResponse);
        })
        .WithName("TranslateCode")
        .WithTags("Operations")
        .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
        .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
        .WithOpenApi(operation =>
        {
            operation.Summary = "$translate - Translate code using ConceptMap";
            operation.Description = "Translates a code from one code system to another using ConceptMap resources.";
            return operation;
        });

        // POST /CodeSystem/$subsumes - Test subsumption relationship between codes
        endpoints.MapPost("/CodeSystem/$subsumes", async (
            HttpContext httpContext,
            [FromBody] SubsumesRequestBody body,
            [FromServices] ITerminologyService terminologyService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.CodeA) || string.IsNullOrWhiteSpace(body.CodeB) || string.IsNullOrWhiteSpace(body.System))
            {
                return Results.BadRequest(CreateOperationOutcomeError(
                    "error",
                    "required",
                    "Parameters 'codeA', 'codeB', and 'system' are required"));
            }

            var parameters = new SubsumesParameters(
                CodeA: body.CodeA,
                CodeB: body.CodeB,
                System: body.System,
                Version: body.Version);

            var result = await terminologyService.SubsumesAsync(parameters, cancellationToken);

            // Convert to FHIR Parameters resource
            var parametersResponse = new
            {
                resourceType = "Parameters",
                parameter = new[]
                {
                    new
                    {
                        name = "outcome",
                        valueCode = result.Outcome
                    }
                }
            };

            return Results.Ok(parametersResponse);
        })
        .WithName("SubsumesCodes")
        .WithTags("Operations")
        .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson)
        .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
        .WithOpenApi(operation =>
        {
            operation.Summary = "$subsumes - Test subsumption relationship between codes";
            operation.Description = "Tests if codeA subsumes codeB, is subsumed by codeB, is equivalent, or has no relationship.";
            return operation;
        });

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

        // Parse ValidationMode from Prefer header
        var preferHeader = context.Request.Headers["Prefer"].ToString();
        var validationMode = ParseValidationModeFromPreferHeader(preferHeader);

        // Create validation command
        var command = new ValidateResourceCommand(
            tenantId,
            resourceType,
            jsonNode,
            ValidationMode: validationMode,
            Mode: mode,
            Profile: profile,
            InstanceId: instanceId);

        // Execute validation
        var result = await mediator.SendAsync(command, cancellationToken);

        // Return OperationOutcome
        return Results.Ok(result.OperationOutcome);
    }

    /// <summary>
    /// Parses ValidationMode from Prefer header.
    /// Expects: Prefer: handling=strict, mode=minimal|normal|full
    /// Defaults to Normal if not specified or invalid.
    /// </summary>
    private static ValidationMode ParseValidationModeFromPreferHeader(string? preferHeader)
    {
        if (string.IsNullOrWhiteSpace(preferHeader))
        {
            return ValidationMode.Normal; // Default to Normal per FHIR spec
        }

        // Parse "mode=minimal|normal|full" from Prefer header
        // Example: "handling=strict, mode=full" → Full
        var parts = preferHeader.Split(',', StringSplitOptions.TrimEntries);
        var modePart = parts.FirstOrDefault(p => p.StartsWith("mode=", StringComparison.OrdinalIgnoreCase));

        if (modePart == null)
        {
            return ValidationMode.Normal;
        }

        var modeValue = modePart.Substring(5).Trim(); // Remove "mode=" prefix

        return modeValue.ToUpperInvariant() switch
        {
            "MINIMAL" => ValidationMode.Minimal,
            "NORMAL" => ValidationMode.Normal,
            "FULL" => ValidationMode.Full,
            _ => ValidationMode.Normal // Unknown value, default to Normal
        };
    }

    /// <summary>
    /// Request body for $translate operation.
    /// </summary>
    private record TranslateRequestBody(
        string? Url,
        string? ConceptMapVersion,
        string Code,
        string System,
        string? Version,
        string? Source,
        string? Target,
        string? TargetSystem,
        bool? Reverse);

    /// <summary>
    /// Request body for $subsumes operation.
    /// </summary>
    private record SubsumesRequestBody(
        string CodeA,
        string CodeB,
        string System,
        string? Version);
}
