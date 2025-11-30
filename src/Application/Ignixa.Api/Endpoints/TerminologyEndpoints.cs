// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Http;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Operations.Features.Terminology.Expand;
using Ignixa.Application.Operations.Features.Terminology.Subsumes;
using Ignixa.Application.Operations.Features.Terminology.Translate;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Registers FHIR terminology endpoints ($expand, $translate, $subsumes).
/// See: https://hl7.org/fhir/R4/terminology-service.html
/// </summary>
public static class TerminologyEndpoints
{
    public static IEndpointRouteBuilder MapTerminologyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTerminologyTenantEndpoints();
        endpoints.MapTerminologyAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit FHIR terminology endpoints (/tenant/{tenantId}/...).
    /// Always supported in all multi-tenancy scenarios.
    /// </summary>
    public static IEndpointRouteBuilder MapTerminologyTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-EXPLICIT ROUTES (always supported)
        var tenantGroup = endpoints.MapGroup("/tenant/{tenantId:int}");

        // GET /tenant/{tenantId}/ValueSet/$expand
        tenantGroup.MapGet("/ValueSet/$expand", HandleExpandValueSetTenant)
            .WithName("ExpandValueSetTenant")
            .WithTags("Terminology")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .WithOpenApi(operation =>
            {
                operation.Summary = "$expand - Expand a ValueSet to a list of codes (tenant-explicit)";
                operation.Description = "Returns the expansion of a ValueSet (list of codes). Uses pre-computed expansions when available.";
                return operation;
            });

        // POST /tenant/{tenantId}/ConceptMap/$translate
        tenantGroup.MapPost("/ConceptMap/$translate", HandleTranslateCodeTenant)
            .WithName("TranslateCodeTenant")
            .WithTags("Terminology")
            .Accepts<TranslateRequestDto>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
            .WithOpenApi(operation =>
            {
                operation.Summary = "$translate - Translate code using ConceptMap (tenant-explicit)";
                operation.Description = "Translates a code from one code system to another using ConceptMap resources.";
                return operation;
            });

        // POST /tenant/{tenantId}/CodeSystem/$subsumes
        tenantGroup.MapPost("/CodeSystem/$subsumes", HandleSubsumesTenant)
            .WithName("SubsumesTenant")
            .WithTags("Terminology")
            .Accepts<SubsumesRequestDto>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
            .WithOpenApi(operation =>
            {
                operation.Summary = "$subsumes - Test subsumption relationship between codes (tenant-explicit)";
                operation.Description = "Tests if codeA subsumes codeB, is subsumed by codeB, is equivalent, or has no relationship.";
                return operation;
            });

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR terminology endpoints (/...).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// </summary>
    public static IEndpointRouteBuilder MapTerminologyAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapExpandEndpoints(endpoints);
        MapTranslateEndpoints(endpoints);
        MapSubsumesEndpoints(endpoints);
        return endpoints;
    }

    #region $expand Operation

    private static void MapExpandEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Tenant-agnostic route
        endpoints.MapGet("/ValueSet/$expand", HandleExpandValueSet)
            .WithName("ExpandValueSet")
            .WithTags("Terminology")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, KnownContentTypes.ApplicationFhirJson)
            .WithOpenApi(operation =>
            {
                operation.Summary = "$expand - Expand a ValueSet to a list of codes";
                operation.Description = "Returns the expansion of a ValueSet (list of codes). Uses pre-computed expansions when available.";
                return operation;
            });
    }

    private static async Task<IResult> HandleExpandValueSet(
        HttpContext context,
        [FromQuery] string? url,
        [FromQuery] string? filter,
        [FromQuery] int? count,
        [FromQuery] int? offset,
        [FromQuery] bool? includeDesignations,
        [FromServices] IMediator mediator,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Results.BadRequest(CreateOperationOutcome("error", "required", "Parameter 'url' is required"));
        }

        var tenantId = fhirContextAccessor.RequestContext!.TenantId;

        var query = new ExpandValueSetQuery(tenantId, url, filter, count, offset, includeDesignations ?? false);

        try
        {
            var result = await mediator.SendAsync(query, cancellationToken);
            return Results.Ok(result.ValueSetResource);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(CreateOperationOutcome("error", "not-found", ex.Message));
        }
    }

    /// <summary>
    /// GET /tenant/{tenantId}/ValueSet/$expand (tenant-explicit)
    /// Expands a ValueSet to a list of codes for a specific tenant.
    /// </summary>
    private static async Task<IResult> HandleExpandValueSetTenant(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromQuery] string? url,
        [FromQuery] string? filter,
        [FromQuery] int? count,
        [FromQuery] int? offset,
        [FromQuery] bool? includeDesignations,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Results.BadRequest(CreateOperationOutcome("error", "required", "Parameter 'url' is required"));
        }

        var query = new ExpandValueSetQuery(tenantId, url, filter, count, offset, includeDesignations ?? false);

        try
        {
            var result = await mediator.SendAsync(query, cancellationToken);
            return Results.Ok(result.ValueSetResource);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(CreateOperationOutcome("error", "not-found", ex.Message));
        }
    }

    #endregion

    #region $translate Operation

    private static void MapTranslateEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/ConceptMap/$translate", HandleTranslateCode)
            .WithName("TranslateCode")
            .WithTags("Terminology")
            .Accepts<TranslateRequestDto>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
            .WithOpenApi(operation =>
            {
                operation.Summary = "$translate - Translate code using ConceptMap";
                operation.Description = "Translates a code from one code system to another using ConceptMap resources.";
                return operation;
            });
    }

    private static async Task<IResult> HandleTranslateCode(
        HttpContext context,
        [FromBody] TranslateRequestDto body,
        [FromServices] IMediator mediator,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.System))
        {
            return Results.BadRequest(CreateOperationOutcome("error", "required", "Parameters 'code' and 'system' are required"));
        }

        var tenantId = fhirContextAccessor.RequestContext!.TenantId;

        var command = new TranslateCodeCommand(
            tenantId,
            body.Code,
            body.System,
            body.Url,
            body.ConceptMapVersion,
            body.Version,
            body.Source,
            body.Target,
            body.TargetSystem,
            body.Reverse ?? false);

        var result = await mediator.SendAsync(command, cancellationToken);
        return Results.Ok(result.ParametersResource);
    }

    /// <summary>
    /// POST /tenant/{tenantId}/ConceptMap/$translate (tenant-explicit)
    /// Translates a code from one code system to another for a specific tenant.
    /// </summary>
    private static async Task<IResult> HandleTranslateCodeTenant(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromBody] TranslateRequestDto body,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.System))
        {
            return Results.BadRequest(CreateOperationOutcome("error", "required", "Parameters 'code' and 'system' are required"));
        }

        var command = new TranslateCodeCommand(
            tenantId,
            body.Code,
            body.System,
            body.Url,
            body.ConceptMapVersion,
            body.Version,
            body.Source,
            body.Target,
            body.TargetSystem,
            body.Reverse ?? false);

        var result = await mediator.SendAsync(command, cancellationToken);
        return Results.Ok(result.ParametersResource);
    }

    #endregion

    #region $subsumes Operation

    private static void MapSubsumesEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/CodeSystem/$subsumes", HandleSubsumes)
            .WithName("SubsumesCodes")
            .WithTags("Terminology")
            .Accepts<SubsumesRequestDto>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
            .WithOpenApi(operation =>
            {
                operation.Summary = "$subsumes - Test subsumption relationship between codes";
                operation.Description = "Tests if codeA subsumes codeB, is subsumed by codeB, is equivalent, or has no relationship.";
                return operation;
            });
    }

    private static async Task<IResult> HandleSubsumes(
        HttpContext context,
        [FromBody] SubsumesRequestDto body,
        [FromServices] IMediator mediator,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.CodeA) || string.IsNullOrWhiteSpace(body.CodeB) || string.IsNullOrWhiteSpace(body.System))
        {
            return Results.BadRequest(CreateOperationOutcome("error", "required", "Parameters 'codeA', 'codeB', and 'system' are required"));
        }

        var tenantId = fhirContextAccessor.RequestContext!.TenantId;

        var query = new SubsumesQuery(tenantId, body.CodeA, body.CodeB, body.System, body.Version);
        var result = await mediator.SendAsync(query, cancellationToken);
        return Results.Ok(result.ParametersResource);
    }

    /// <summary>
    /// POST /tenant/{tenantId}/CodeSystem/$subsumes (tenant-explicit)
    /// Tests subsumption relationship between codes for a specific tenant.
    /// </summary>
    private static async Task<IResult> HandleSubsumesTenant(
        HttpContext context,
        [FromRoute] int tenantId,
        [FromBody] SubsumesRequestDto body,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.CodeA) || string.IsNullOrWhiteSpace(body.CodeB) || string.IsNullOrWhiteSpace(body.System))
        {
            return Results.BadRequest(CreateOperationOutcome("error", "required", "Parameters 'codeA', 'codeB', and 'system' are required"));
        }

        var query = new SubsumesQuery(tenantId, body.CodeA, body.CodeB, body.System, body.Version);
        var result = await mediator.SendAsync(query, cancellationToken);
        return Results.Ok(result.ParametersResource);
    }

    #endregion

    #region DTOs

    private record TranslateRequestDto(
        string Code,
        string System,
        string? Url = null,
        string? ConceptMapVersion = null,
        string? Version = null,
        string? Source = null,
        string? Target = null,
        string? TargetSystem = null,
        bool? Reverse = null);

    private record SubsumesRequestDto(
        string CodeA,
        string CodeB,
        string System,
        string? Version = null);

    #endregion

    #region Helper Methods

    private static object CreateOperationOutcome(string severity, string code, string diagnostics) =>
        new
        {
            resourceType = "OperationOutcome",
            issue = new[]
            {
                new { severity, code, diagnostics }
            }
        };

    #endregion
}
