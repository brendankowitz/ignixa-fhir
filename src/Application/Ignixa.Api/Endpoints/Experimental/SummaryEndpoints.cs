// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Extensions;
using Ignixa.Api.Http;
using Ignixa.Application.Features.Experimental.Ips.Generator;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;

namespace Ignixa.Api.Endpoints.Experimental;

/// <summary>
/// Registers FHIR $summary (IPS) operation endpoints.
/// This is an experimental feature that can be enabled/disabled via configuration.
/// </summary>
public static class SummaryEndpoints
{
    /// <summary>
    /// Maps $summary endpoints to the application.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="configureTenantGroup">
    /// Optional delegate to configure the tenant route group (e.g., add filters).
    /// When called from the Api layer, this applies FhirAuthorizationFilter, FhirAuditFilter, etc.
    /// </param>
    public static IEndpointRouteBuilder MapSummaryEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<RouteGroupBuilder>? configureTenantGroup = null)
    {
        endpoints.MapSummaryTenantEndpoints(configureTenantGroup);
        endpoints.MapSummaryAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit $summary endpoints (/tenant/{tenantId}/...).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="configureTenantGroup">
    /// Optional delegate to configure the tenant route group (e.g., add filters).
    /// </param>
    private static void MapSummaryTenantEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<RouteGroupBuilder>? configureTenantGroup = null)
    {
        var tenantGroup = endpoints.MapGroup("/tenant/{tenantId:int}");

        // Apply filters if provided by the Api layer
        configureTenantGroup?.Invoke(tenantGroup);

        // GET /tenant/{tenantId}/Patient/{id}/$summary - Patient $summary (IPS) operation
        tenantGroup.MapGet("/Patient/{id}/$summary", HandlePatientSummaryById)
            .WithName("PatientSummaryById")
            .WithTags("Experimental", "Summary", "IPS")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // GET /tenant/{tenantId}/Patient/$summary?identifier=... - Patient $summary by identifier
        tenantGroup.MapGet("/Patient/$summary", HandlePatientSummaryByIdentifier)
            .WithName("PatientSummaryByIdentifier")
            .WithTags("Experimental", "Summary", "IPS")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // POST /tenant/{tenantId}/Patient/{id}/$summary - Patient $summary (IPS) operation POST variant
        tenantGroup.MapPost("/Patient/{id}/$summary", HandlePatientSummaryByIdPost)
            .WithName("PatientSummaryByIdPost")
            .WithTags("Experimental", "Summary", "IPS")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);
    }

    /// <summary>
    /// Registers tenant-agnostic $summary endpoints (/).
    /// Only enabled in single-tenant mode.
    /// </summary>
    private static void MapSummaryAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /Patient/{id}/$summary - Patient $summary (IPS) operation (agnostic route)
        endpoints.MapGet("/Patient/{id}/$summary", HandlePatientSummaryById)
            .WithName("PatientSummaryByIdAgnostic")
            .WithTags("Experimental", "Summary", "IPS")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // GET /Patient/$summary?identifier=... - Patient $summary by identifier (agnostic route)
        endpoints.MapGet("/Patient/$summary", HandlePatientSummaryByIdentifier)
            .WithName("PatientSummaryByIdentifierAgnostic")
            .WithTags("Experimental", "Summary", "IPS")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        // POST /Patient/{id}/$summary - Patient $summary (IPS) operation POST variant (agnostic route)
        endpoints.MapPost("/Patient/{id}/$summary", HandlePatientSummaryByIdPost)
            .WithName("PatientSummaryByIdPostAgnostic")
            .WithTags("Experimental", "Summary", "IPS")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);
    }

    /// <summary>
    /// Handles GET /Patient/{id}/$summary - Patient $summary (IPS) operation.
    /// GET /tenant/{tenantId}/Patient/{id}/$summary (tenant-explicit)
    /// GET /Patient/{id}/$summary (agnostic, single-tenant only)
    /// </summary>
    private static async Task<IResult> HandlePatientSummaryById(
        HttpContext context,
        string id,
        [FromServices] IMediator mediator,
        [FromQuery] string? profile,
        CancellationToken cancellationToken)
    {
        if (profile is not null && !Uri.IsWellFormedUriString(profile, UriKind.Absolute))
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
                "Profile parameter must be a well-formed absolute URI"));
        }

        var query = new IpsGeneratorQuery(
            PatientId: id,
            PatientIdentifier: null,
            Profile: profile);

        var result = await mediator.SendAsync(query, cancellationToken);

        // Check for _pretty parameter
        bool pretty = context.Request.Query.GetPrettyParameter();

        var bundleJson = result.IpsBundle.SerializeToString(pretty);
        context.Response.ContentType = KnownContentTypes.ApplicationFhirJsonUtf8;
        await context.Response.WriteAsync(bundleJson, cancellationToken);

        return Results.Empty;
    }

    /// <summary>
    /// Handles GET /Patient/$summary?identifier=... - Patient $summary by identifier.
    /// GET /tenant/{tenantId}/Patient/$summary?identifier=... (tenant-explicit)
    /// GET /Patient/$summary?identifier=... (agnostic, single-tenant only)
    /// </summary>
    private static async Task<IResult> HandlePatientSummaryByIdentifier(
        HttpContext context,
        [FromServices] IMediator mediator,
        [FromQuery] string identifier,
        [FromQuery] string? profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "Patient identifier is required when patient ID is not provided in URL"));
        }

        if (profile is not null && !Uri.IsWellFormedUriString(profile, UriKind.Absolute))
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
                "Profile parameter must be a well-formed absolute URI"));
        }

        var query = new IpsGeneratorQuery(
            PatientId: null,
            PatientIdentifier: identifier,
            Profile: profile);

        var result = await mediator.SendAsync(query, cancellationToken);

        // Check for _pretty parameter
        bool pretty = context.Request.Query.GetPrettyParameter();

        var bundleJson = result.IpsBundle.SerializeToString(pretty);
        context.Response.ContentType = KnownContentTypes.ApplicationFhirJsonUtf8;
        await context.Response.WriteAsync(bundleJson, cancellationToken);

        return Results.Empty;
    }

    /// <summary>
    /// Handles POST /Patient/{id}/$summary - Patient $summary (IPS) operation POST variant.
    /// POST /tenant/{tenantId}/Patient/{id}/$summary (tenant-explicit)
    /// POST /Patient/{id}/$summary (agnostic, single-tenant only)
    /// </summary>
    private static async Task<IResult> HandlePatientSummaryByIdPost(
        HttpContext context,
        string id,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        // Parse Parameters from request body if provided
        string? profile = null;

        if (context.Request.ContentLength > 0)
        {
            try
            {
                await using var memoryStream = memoryStreamManager.GetStream("summary-request");
                await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;

                if (memoryStream.Length > 0)
                {
                    var parameters = await JsonSourceNodeFactory.ParseAsync<ParametersJsonNode>(memoryStream, cancellationToken);
                    profile = parameters?.GetParameterStringValue("profile");
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed JSON in request body - proceed without profile parameter
                // This is acceptable since the profile parameter is optional
            }
        }

        if (profile is not null && !Uri.IsWellFormedUriString(profile, UriKind.Absolute))
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
                "Profile parameter must be a well-formed absolute URI"));
        }

        var query = new IpsGeneratorQuery(
            PatientId: id,
            PatientIdentifier: null,
            Profile: profile);

        var result = await mediator.SendAsync(query, cancellationToken);

        // Check for _pretty parameter
        bool pretty = context.Request.Query.GetPrettyParameter();

        var bundleJson = result.IpsBundle.SerializeToString(pretty);
        context.Response.ContentType = KnownContentTypes.ApplicationFhirJsonUtf8;
        await context.Response.WriteAsync(bundleJson, cancellationToken);

        return Results.Empty;
    }

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
}
