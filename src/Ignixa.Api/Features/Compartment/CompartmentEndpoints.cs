// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Application.Features.Compartment;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.SourceNodeSerialization;

namespace Ignixa.Api.Features.Compartment;

/// <summary>
/// Minimal API endpoints for FHIR compartment search.
/// Implements: GET /{compartmentType}/{compartmentId}/{resourceType}
/// Example: GET /Patient/123/Observation - Find all Observations in Patient 123's compartment
///
/// Supports both tenant-explicit and tenant-agnostic routes.
/// </summary>
public static class CompartmentEndpoints
{
    public static IEndpointRouteBuilder MapCompartmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCompartmentTenantEndpoints();
        endpoints.MapCompartmentAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit FHIR compartment endpoints (/tenant/{tenantId}/{compartmentType}/...).
    /// Always supported in all multi-tenancy scenarios.
    /// </summary>
    public static IEndpointRouteBuilder MapCompartmentTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-explicit route: GET /tenant/{tenantId}/{compartmentType}/{compartmentId}/{resourceType}
        endpoints.MapGet("/tenant/{tenantId:int}/{compartmentType}/{compartmentId}/{resourceType}", HandleSearchCompartmentExplicitAsync)
            .WithName("SearchCompartmentExplicit")
            .Produces(StatusCodes.Status200OK, contentType: "application/fhir+json")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR compartment endpoints (/{compartmentType}/...).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// </summary>
    public static IEndpointRouteBuilder MapCompartmentAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-agnostic route: GET /{compartmentType}/{compartmentId}/{resourceType}
        endpoints.MapGet("/{compartmentType}/{compartmentId}/{resourceType}", HandleSearchCompartmentAsync)
            .WithName("SearchCompartment")
            .Produces(StatusCodes.Status200OK, contentType: "application/fhir+json")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// GET /{compartmentType}/{compartmentId}/{resourceType}
    /// Tenant-agnostic compartment search (auto-detects tenant in single-tenant mode).
    /// </summary>
    private static async Task<IResult> HandleSearchCompartmentAsync(
        HttpContext context,
        string compartmentType,
        string compartmentId,
        string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] IQueryParameterParser queryParser,
        [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "GET /{CompartmentType}/{CompartmentId}/{ResourceType} (tenant-agnostic)",
            compartmentType,
            compartmentId,
            resourceType);

        // Tenant resolution handled by TenantResolutionMiddleware
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "required",
                        diagnostics = "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/{compartmentType}/{compartmentId}/{resourceType}"
                    }
                }
            });
        }

        return await ExecuteSearchCompartmentAsync(
            context,
            tenantId,
            compartmentType,
            compartmentId,
            resourceType,
            mediator,
            queryParser,
            searchOptionsBuilderFactory,
            logger,
            cancellationToken);
    }

    /// <summary>
    /// GET /tenant/{tenantId}/{compartmentType}/{compartmentId}/{resourceType}
    /// Tenant-explicit compartment search.
    /// </summary>
    private static async Task<IResult> HandleSearchCompartmentExplicitAsync(
        HttpContext context,
        int tenantId,
        string compartmentType,
        string compartmentId,
        string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] IQueryParameterParser queryParser,
        [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "GET /tenant/{TenantId}/{CompartmentType}/{CompartmentId}/{ResourceType}",
            tenantId,
            compartmentType,
            compartmentId,
            resourceType);

        return await ExecuteSearchCompartmentAsync(
            context,
            tenantId,
            compartmentType,
            compartmentId,
            resourceType,
            mediator,
            queryParser,
            searchOptionsBuilderFactory,
            logger,
            cancellationToken);
    }

    /// <summary>
    /// Shared implementation for compartment search execution.
    /// Parses query parameters, creates SearchCompartmentQuery, and streams results.
    /// </summary>
    private static async Task<IResult> ExecuteSearchCompartmentAsync(
        HttpContext context,
        int tenantId,
        string compartmentType,
        string compartmentId,
        string resourceType,
        IMediator mediator,
        IQueryParameterParser queryParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Validate compartment type (must be one of: Patient, Practitioner, RelatedPerson, Device, Encounter)
        var validCompartmentTypes = new[] { "Patient", "Practitioner", "RelatedPerson", "Device", "Encounter" };
        if (!validCompartmentTypes.Contains(compartmentType, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = $"Invalid compartment type '{compartmentType}'. Must be one of: Patient, Practitioner, RelatedPerson, Device, Encounter"
                    }
                }
            });
        }

        // Get tenant configuration for FHIR version
        if (!context.Items.TryGetValue("TenantConfiguration", out var tenantConfigObj) ||
            tenantConfigObj is not TenantConfiguration tenantConfig)
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

        // Create SearchCompartmentQuery
        var query = new SearchCompartmentQuery(
            CompartmentType: compartmentType,
            CompartmentId: compartmentId,
            ResourceType: resourceType,
            SearchOptions: searchOptions);

        // Execute query (returns streaming IAsyncEnumerable<SearchEntryResult>)
        var result = await mediator.SendAsync(query, cancellationToken);

        // Build self link
        string selfLink = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";

        // Set response headers
        context.Response.ContentType = "application/fhir+json; charset=utf-8";

        // Stream Bundle response (95% memory reduction vs buffering)
        await StreamingBundleSerializer.SerializeAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            selfLink: selfLink,
            nextLink: null,
            pretty: false,
            cancellationToken: cancellationToken);

        // Response already written to the body, return empty result
        return Results.Empty;
    }
}
