// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Api.Extensions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Operations.Features.MemberMatch;
using Ignixa.Application.Operations.Features.PatientEverything;
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
using Ignixa.Abstractions;
using Ignixa.Search.Models;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Registers FHIR operation endpoints ($validate, $everything, etc.)
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
    /// - GET /Patient/{id}/$everything - Patient $everything operation
    /// - POST /Patient/$member-match - Member match operation
    ///
    /// NOTE: $transform endpoints moved to Experimental/TransformEndpoints.cs
    /// NOTE: $summary endpoints moved to Experimental/SummaryEndpoints.cs
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
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>()
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

        // GET /Patient/{id}/$everything - Patient $everything operation (tenant-explicit)
        tenantGroup.MapGet("/Patient/{id}/$everything", HandlePatientEverything)
            .WithName("PatientEverything")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // NOTE: $summary endpoints moved to Experimental/SummaryEndpoints.cs
        // NOTE: $transform endpoints moved to Experimental/TransformEndpoints.cs

        // POST /Patient/$member-match - Member match operation (tenant-explicit)
        tenantGroup.MapPost("/Patient/$member-match", HandleMemberMatch)
            .WithName("MemberMatch")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status422UnprocessableEntity, KnownContentTypes.ApplicationFhirJson);

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

        // GET /Patient/{id}/$everything - Patient $everything operation (agnostic route)
        endpoints.MapGet("/Patient/{id}/$everything", HandlePatientEverything)
            .WithName("PatientEverythingAgnostic")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // NOTE: $summary endpoints moved to Experimental/SummaryEndpoints.cs
        // NOTE: $transform endpoints moved to Experimental/TransformEndpoints.cs

        // POST /Patient/$member-match - Member match operation (agnostic route)
        endpoints.MapPost("/Patient/$member-match", HandleMemberMatchAgnostic)
            .WithName("MemberMatchAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status422UnprocessableEntity, KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    /// <summary>
    /// Creates a FHIR OperationOutcome response with a single issue.
    /// </summary>
    private static OperationOutcomeJsonNode CreateOperationOutcome(
        OperationOutcomeJsonNode.IssueSeverity severity,
        OperationOutcomeJsonNode.IssueType code,
        string diagnostics)
    {
        var outcome = new OperationOutcomeJsonNode();

        outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = severity,
            Code = code,
            Diagnostics = diagnostics
        });

        return outcome;
    }

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
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
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
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
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
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
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
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "Request body must contain a FHIR resource to validate"));
        }

        // Parse JSON using JsonSourceNodeFactory
        ResourceJsonNode jsonNode;
        try
        {
            jsonNode = await JsonSourceNodeFactory.ParseAsync(memoryStream, cancellationToken);
        }
        catch
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
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
                            var resourceBytes = System.Text.Encoding.UTF8.GetBytes(resourceNode.ToJsonString() ?? "{}");
                            jsonNode = JsonSourceNodeFactory.Parse(resourceBytes);
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
                return Results.BadRequest(CreateOperationOutcome(
                    OperationOutcomeJsonNode.IssueSeverity.Error,
                    OperationOutcomeJsonNode.IssueType.Invalid,
                    $"Validation mode '{mode}' requires instance-level endpoint: [base]/{{resourceType}}/{{id}}/$validate"));
            }
        }

        // Parse ValidationDepth from Prefer header
        var preferHeader = context.Request.Headers["Prefer"].ToString();
        var validationMode = ParseValidationDepthFromPreferHeader(preferHeader);

        // Create validation command
        var command = new ValidateResourceCommand(
            tenantId,
            resourceType,
            jsonNode,
            ValidationDepth: validationMode,
            Mode: mode,
            Profile: profile,
            InstanceId: instanceId);

        // Execute validation
        var result = await mediator.SendAsync(command, cancellationToken);

        // Return OperationOutcome
        return Results.Ok(result.OperationOutcome);
    }

    /// <summary>
    /// Parses ValidationDepth from Prefer header.
    /// Expects: Prefer: handling=strict, mode=minimal|spec|full
    /// Defaults to Spec if not specified or invalid.
    /// </summary>
    private static ValidationDepth ParseValidationDepthFromPreferHeader(string? preferHeader)
    {
        if (string.IsNullOrWhiteSpace(preferHeader))
        {
            return ValidationDepth.Spec; // Default to Spec per FHIR spec
        }

        // Parse "mode=minimal|spec|full" from Prefer header
        // Example: "handling=strict, mode=full" → Full
        var parts = preferHeader.Split(',', StringSplitOptions.TrimEntries);
        var modePart = parts.FirstOrDefault(p => p.StartsWith("mode=", StringComparison.OrdinalIgnoreCase));

        if (modePart == null)
        {
            return ValidationDepth.Spec;
        }

        var modeValue = modePart.Substring(5).Trim(); // Remove "mode=" prefix

        return modeValue.ToUpperInvariant() switch
        {
            "MINIMAL" => ValidationDepth.Minimal,
            "SPEC" => ValidationDepth.Spec,
            "NORMAL" => ValidationDepth.Spec, // Backward compatibility
            "FULL" => ValidationDepth.Full,
            _ => ValidationDepth.Spec // Unknown value, default to Spec
        };
    }

    /// <summary>
    /// Handles Patient $everything operation.
    /// GET /tenant/{tenantId}/Patient/{id}/$everything (tenant-explicit)
    /// GET /Patient/{id}/$everything (agnostic, single-tenant only)
    /// Tenant resolution is handled by TenantResolutionMiddleware and FhirRequestContextMiddleware.
    /// </summary>
    private static async Task<IResult> HandlePatientEverything(
        HttpContext context,
        string id,
        [FromServices] IMediator mediator,
        [FromQuery] string? start,
        [FromQuery] string? end,
        [FromQuery] DateTimeOffset? _since,
        [FromQuery] string? _type,
        [FromQuery] int? _count,
        CancellationToken cancellationToken)
    {
        // Parse _type parameter (comma-delimited list of resource types)
        ISet<string>? types = null;
        if (!string.IsNullOrEmpty(_type))
        {
            types = new HashSet<string>(_type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        // Parse partial date strings using PartialDateTime for FHIR compliance
        DateTimeOffset? startOffset = null;
        if (!string.IsNullOrEmpty(start))
        {
            var partialStart = PartialDateTime.Parse(start);
            // For start date: use first moment (month=1, day=1, hour=0, minute=0, second=0)
            startOffset = partialStart.ToDateTimeOffset(
                defaultMonth: 1,
                defaultDaySelector: (year, month) => 1,
                defaultHour: 0,
                defaultMinute: 0,
                defaultSecond: 0,
                defaultFraction: 0,
                defaultUtcOffset: TimeSpan.Zero);
        }

        DateTimeOffset? endOffset = null;
        if (!string.IsNullOrEmpty(end))
        {
            var partialEnd = PartialDateTime.Parse(end);
            // For end date: use last moment (month=12, day=last day of month, hour=23, minute=59, second=59)
            endOffset = partialEnd.ToDateTimeOffset(
                defaultMonth: 12,
                defaultDaySelector: (year, month) => DateTime.DaysInMonth(year, month),
                defaultHour: 23,
                defaultMinute: 59,
                defaultSecond: 59,
                defaultFraction: 0.9999999m,
                defaultUtcOffset: TimeSpan.Zero);
        }

        // Create Patient $everything query
        var query = new PatientEverythingQuery(
            PatientId: id,
            Start: startOffset,
            End: endOffset,
            Since: _since,
            Types: types,
            Count: _count);

        // Execute via mediator (returns streaming IAsyncEnumerable<SearchEntryResult>)
        var result = await mediator.SendAsync(query, cancellationToken);

        // Build base URL for link generation
        string baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";

        // Set response headers
        context.Response.ContentType = KnownContentTypes.ApplicationFhirJsonUtf8;

        // Check for _pretty parameter
        bool pretty = context.Request.Query.GetPrettyParameter();

        // Stream Bundle response using StreamingBundleSerializer
        // This provides optimal memory efficiency by streaming results as they're retrieved
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            searchOptions: result.SearchOptions!,
            baseUrl: baseUrl,
            queryString: context.Request.QueryString.Value ?? string.Empty,
            pretty: pretty,
            cancellationToken: cancellationToken);

        // Response already written to the body, return empty result
        return Results.Empty;
    }

    // NOTE: $transform handlers moved to Experimental/TransformEndpoints.cs
    // NOTE: $summary handlers moved to Experimental/SummaryEndpoints.cs

    // ==================== $member-match Operation Handlers ====================

    /// <summary>
    /// Handles POST /tenant/{tenantId}/Patient/$member-match (tenant-explicit).
    /// </summary>
    private static async Task<IResult> HandleMemberMatch(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // Parse Parameters from request body
        ParametersJsonNode? parameters;
        try
        {
            await using var memoryStream = memoryStreamManager.GetStream("member-match-request");
            await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            parameters = await JsonSourceNodeFactory.ParseAsync<ParametersJsonNode>(memoryStream, cancellationToken);
        }
        catch
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
                "Request body must be a valid FHIR Parameters resource"));
        }

        if (parameters == null)
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "Request body must contain a FHIR Parameters resource"));
        }

        // Extract parameters per HRex specification
        var memberPatient = parameters.GetParameterResource<ResourceJsonNode>("MemberPatient");
        var coverageToMatch = parameters.GetParameterResource<ResourceJsonNode>("CoverageToMatch");
        var coverageToLink = parameters.GetParameterResource<ResourceJsonNode>("CoverageToLink");
        var consent = parameters.GetParameterResource<ResourceJsonNode>("Consent");

        // Validate required parameters
        if (memberPatient == null)
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "Required parameter 'MemberPatient' is missing"));
        }

        if (coverageToMatch == null)
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "Required parameter 'CoverageToMatch' is missing"));
        }

        // Create command
        var command = new MemberMatchCommand(
            MemberPatient: memberPatient,
            CoverageToMatch: coverageToMatch,
            CoverageToLink: coverageToLink,
            Consent: consent);

        // Execute via mediator
        var result = await mediator.SendAsync(command, cancellationToken);

        if (result.Success)
        {
            // Build and return successful response using FhirResults
            var responseParameters = MemberMatchHandler.BuildResponseParameters(result);
            return FhirResults.Ok(responseParameters, context);
        }

        // Build error response
        var errorOutcome = MemberMatchHandler.BuildErrorOperationOutcome(result);

        // Return appropriate HTTP status based on error type
        return result.ErrorCode switch
        {
            "no-match" or "multiple-matches" => Results.UnprocessableEntity(errorOutcome.MutableNode),
            _ => Results.BadRequest(errorOutcome.MutableNode)
        };
    }

    /// <summary>
    /// Handles POST /Patient/$member-match (tenant-agnostic, single-tenant only).
    /// </summary>
    private static async Task<IResult> HandleMemberMatchAgnostic(
        HttpContext context,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // For agnostic route, determine tenant from context
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/Patient/$member-match"));
        }

        return await HandleMemberMatch(context, tenantId, mediator, memoryStreamManager, cancellationToken);
    }
}
