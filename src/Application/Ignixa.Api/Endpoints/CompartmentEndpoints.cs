// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Abstractions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.Features.Compartment;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

namespace Ignixa.Api.Endpoints;

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
    /// Routes with {resourceType} validate against tenant's FHIR version via ResourceTypeValidationFilter.
    /// </summary>
    public static IEndpointRouteBuilder MapCompartmentTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Create a route group with the filter for resource-type-specific routes
        var tenantGroup = endpoints
            .MapGroup("/tenant/{tenantId:int}")
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        // Tenant-explicit route: GET /{compartmentType}/{compartmentId}/{resourceType}
        tenantGroup.MapGet("/{compartmentType}/{compartmentId}/{resourceType}", HandleSearchCompartmentExplicitAsync)
            .WithName("SearchCompartmentExplicit")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        // Tenant-explicit wildcard route: GET /{compartmentType}/{compartmentId}/*
        // IMPORTANT: Must use literal "/*" constraint to match asterisk character
        // Wildcard route doesn't have resourceType parameter, so filter doesn't apply, but register on group for consistency
        tenantGroup.MapGet("/{compartmentType}/{compartmentId}/*", HandleSearchCompartmentWildcardExplicitAsync)
            .WithName("SearchCompartmentWildcardExplicit")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Registers tenant-agnostic FHIR compartment endpoints (/{compartmentType}/...).
    /// Supported in single-tenant mode (auto-detect) and distributed mode (future).
    /// Blocked in multi-tenant mode by TenantResolutionMiddleware (400 Bad Request).
    /// Routes with {resourceType} validate against tenant's FHIR version via ResourceTypeValidationFilter.
    /// </summary>
    public static IEndpointRouteBuilder MapCompartmentAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Create a route group with the filter for resource-type-specific routes
        var agnosticGroup = endpoints
            .MapGroup(string.Empty)
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        // Tenant-agnostic route: GET /{compartmentType}/{compartmentId}/{resourceType}
        agnosticGroup.MapGet("/{compartmentType}/{compartmentId}/{resourceType}", HandleSearchCompartmentAsync)
            .WithName("SearchCompartment")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        // Tenant-agnostic wildcard route: GET /{compartmentType}/{compartmentId}/*
        // Wildcard route doesn't have resourceType parameter, so filter doesn't apply, but register on group for consistency
        agnosticGroup.MapGet("/{compartmentType}/{compartmentId}/*", HandleSearchCompartmentWildcardAsync)
            .WithName("SearchCompartmentWildcard")
            .Produces(StatusCodes.Status200OK, contentType: KnownContentTypes.ApplicationFhirJson)
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
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(CompartmentEndpoints).FullName!);
        logger.LogInformation(
            "GET /{CompartmentType}/{CompartmentId}/{ResourceType} (tenant-agnostic)",
            compartmentType,
            compartmentId,
            resourceType);

        // Tenant resolution handled by TenantResolutionMiddleware
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            var outcome = new OperationOutcomeJsonNode();
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
                Code = OperationOutcomeJsonNode.IssueType.Required,
                Diagnostics = "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/{compartmentType}/{compartmentId}/{resourceType}"
            });
            return Results.BadRequest(outcome);
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
            fhirContextAccessor,
            loggerFactory,
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
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(CompartmentEndpoints).FullName!);
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
            fhirContextAccessor,
            loggerFactory,
            cancellationToken);
    }

    /// <summary>
    /// Shared implementation for compartment search execution.
    /// Parses query parameters, creates SearchCompartmentQuery, and streams results.
    /// Handles both specific resource types (e.g., "Observation") and wildcard ("*").
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
        IFhirRequestContextAccessor fhirContextAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(CompartmentEndpoints).FullName!);
        // Validate compartment type (must be one of: Patient, Practitioner, RelatedPerson, Device, Encounter)
        var validCompartmentTypes = new[] { "Patient", "Practitioner", "RelatedPerson", "Device", "Encounter" };
        if (!validCompartmentTypes.Contains(compartmentType, StringComparer.OrdinalIgnoreCase))
        {
            var outcome = new OperationOutcomeJsonNode();
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
                Code = OperationOutcomeJsonNode.IssueType.Invalid,
                Diagnostics = $"Invalid compartment type '{compartmentType}'. Must be one of: Patient, Practitioner, RelatedPerson, Device, Encounter"
            });
            return Results.BadRequest(outcome);
        }

        // Get tenant configuration from FHIR request context (works for both regular and bundle entry requests)
        var fhirContext = fhirContextAccessor.RequestContext;
        if (fhirContext?.TenantConfiguration == null)
        {
            logger.LogError("TenantConfiguration not found in IFhirRequestContext for compartmentType '{CompartmentType}'", compartmentType);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var tenantConfig = fhirContext.TenantConfiguration;

        // Get version-specific search options builder
        // CRITICAL: Pass tenantId to use tenant-specific search parameters (e.g., US Core)
        var fhirSpec = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var searchOptionsBuilder = searchOptionsBuilderFactory.Create(fhirSpec, fhirContext.TenantId);

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

        // Build base URL for link generation
        string baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";

        // Set response headers
        context.Response.ContentType = KnownContentTypes.ApplicationFhirJsonUtf8;

        // Stream Bundle response (95% memory reduction vs buffering)
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            searchOptions: searchOptions,
            baseUrl: baseUrl,
            queryString: context.Request.QueryString.Value ?? string.Empty,
            pretty: false,
            cancellationToken: cancellationToken);

        // Response already written to the body, return empty result
        return Results.Empty;
    }

    /// <summary>
    /// GET /{compartmentType}/{compartmentId}/*
    /// Tenant-agnostic compartment wildcard search (searches all resource types in compartment).
    /// </summary>
    private static async Task<IResult> HandleSearchCompartmentWildcardAsync(
        HttpContext context,
        string compartmentType,
        string compartmentId,
        [FromServices] IMediator mediator,
        [FromServices] IQueryParameterParser queryParser,
        [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(CompartmentEndpoints).FullName!);
        logger.LogInformation(
            "GET /{CompartmentType}/{CompartmentId}/* (tenant-agnostic wildcard)",
            compartmentType,
            compartmentId);

        // Tenant resolution handled by TenantResolutionMiddleware
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            var outcome = new OperationOutcomeJsonNode();
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
                Code = OperationOutcomeJsonNode.IssueType.Required,
                Diagnostics = "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/{compartmentType}/{compartmentId}/*"
            });
            return Results.BadRequest(outcome);
        }

        return await ExecuteSearchCompartmentAsync(
            context,
            tenantId,
            compartmentType,
            compartmentId,
            "*", // Wildcard marker
            mediator,
            queryParser,
            searchOptionsBuilderFactory,
            fhirContextAccessor,
            loggerFactory,
            cancellationToken);
    }

    /// <summary>
    /// GET /tenant/{tenantId}/{compartmentType}/{compartmentId}/*
    /// Tenant-explicit compartment wildcard search (searches all resource types in compartment).
    /// </summary>
    private static async Task<IResult> HandleSearchCompartmentWildcardExplicitAsync(
        HttpContext context,
        int tenantId,
        string compartmentType,
        string compartmentId,
        [FromServices] IMediator mediator,
        [FromServices] IQueryParameterParser queryParser,
        [FromServices] ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(CompartmentEndpoints).FullName!);
        logger.LogInformation(
            "GET /tenant/{TenantId}/{CompartmentType}/{CompartmentId}/* (wildcard)",
            tenantId,
            compartmentType,
            compartmentId);

        return await ExecuteSearchCompartmentAsync(
            context,
            tenantId,
            compartmentType,
            compartmentId,
            "*", // Wildcard marker
            mediator,
            queryParser,
            searchOptionsBuilderFactory,
            fhirContextAccessor,
            loggerFactory,
            cancellationToken);
    }
}
