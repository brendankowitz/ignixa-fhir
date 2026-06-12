// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate.Execution.Serialization;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.Endpoints.Experimental;

public static class GraphQlEndpoints
{
    private static readonly JsonResultFormatter ResultFormatter = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapGraphQlEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<RouteGroupBuilder>? configureTenantGroup = null)
    {
        endpoints.MapGraphQlTenantEndpoints(configureTenantGroup);
        endpoints.MapGraphQlAgnosticEndpoints();
        return endpoints;
    }

    private static void MapGraphQlTenantEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<RouteGroupBuilder>? configureTenantGroup = null)
    {
        var tenantGroup = endpoints.MapGroup("/tenant/{tenantId:int}");
        configureTenantGroup?.Invoke(tenantGroup);

        tenantGroup.MapPost("/$graphql", HandleSystemPost)
            .WithName("GraphQlSystemPostTenant")
            .WithTags("Experimental", "GraphQL")
            .Accepts<GraphQlRequestBody>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "application/json");

        tenantGroup.MapGet("/$graphql", HandleSystemGet)
            .WithName("GraphQlSystemGetTenant")
            .WithTags("Experimental", "GraphQL")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "application/json");

        tenantGroup.MapPost("/{resourceType}/{id}/$graphql", HandleInstancePost)
            .WithName("GraphQlInstancePostTenant")
            .WithTags("Experimental", "GraphQL")
            .Accepts<GraphQlRequestBody>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "application/json");

        tenantGroup.MapGet("/{resourceType}/{id}/$graphql", HandleInstanceGet)
            .WithName("GraphQlInstanceGetTenant")
            .WithTags("Experimental", "GraphQL")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "application/json");
    }

    private static void MapGraphQlAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/$graphql", HandleSystemPost)
            .WithName("GraphQlSystemPostAgnostic")
            .WithTags("Experimental", "GraphQL")
            .Accepts<GraphQlRequestBody>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "application/json");

        endpoints.MapGet("/$graphql", HandleSystemGet)
            .WithName("GraphQlSystemGetAgnostic")
            .WithTags("Experimental", "GraphQL")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "application/json");

        endpoints.MapPost("/{resourceType}/{id}/$graphql", HandleInstancePost)
            .WithName("GraphQlInstancePostAgnostic")
            .WithTags("Experimental", "GraphQL")
            .Accepts<GraphQlRequestBody>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "application/json");

        endpoints.MapGet("/{resourceType}/{id}/$graphql", HandleInstanceGet)
            .WithName("GraphQlInstanceGetAgnostic")
            .WithTags("Experimental", "GraphQL")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "application/json");
    }

    private static async Task<IResult> HandleSystemPost(
        HttpContext context,
        [FromServices] IGraphQlExecutionService executionService,
        [FromServices] IFhirRequestContextAccessor contextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var (body, error) = await ReadBodyAsync(context, cancellationToken);
        if (error is not null)
            return error;

        var version = ResolveVersion(contextAccessor, loggerFactory);
        var result = await executionService.ExecuteAsync(body!, version, cancellationToken);
        return await WriteResultAsync(context, result, cancellationToken);
    }

    private static async Task<IResult> HandleSystemGet(
        HttpContext context,
        [FromServices] IGraphQlExecutionService executionService,
        [FromServices] IFhirRequestContextAccessor contextAccessor,
        [FromServices] IOptions<ExperimentalOptions> options,
        [FromServices] ILoggerFactory loggerFactory,
        [FromQuery] string? query,
        [FromQuery] string? operationName,
        [FromQuery] string? variables,
        CancellationToken cancellationToken)
    {
        var (body, error) = BuildGetBody(options, query, operationName, variables);
        if (error is not null)
            return error;

        var version = ResolveVersion(contextAccessor, loggerFactory);
        var result = await executionService.ExecuteAsync(body!, version, cancellationToken);
        return await WriteResultAsync(context, result, cancellationToken);
    }

    private static async Task<IResult> HandleInstancePost(
        HttpContext context,
        string resourceType,
        string id,
        [FromServices] IGraphQlExecutionService executionService,
        [FromServices] IFhirRequestContextAccessor contextAccessor,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var (body, error) = await ReadBodyAsync(context, cancellationToken);
        if (error is not null)
            return error;

        var version = ResolveVersion(contextAccessor, loggerFactory);
        var result = await executionService.ExecuteInstanceAsync(body!, version, resourceType, id, cancellationToken);
        return await WriteResultAsync(context, result, cancellationToken);
    }

    private static async Task<IResult> HandleInstanceGet(
        HttpContext context,
        string resourceType,
        string id,
        [FromServices] IGraphQlExecutionService executionService,
        [FromServices] IFhirRequestContextAccessor contextAccessor,
        [FromServices] IOptions<ExperimentalOptions> options,
        [FromServices] ILoggerFactory loggerFactory,
        [FromQuery] string? query,
        [FromQuery] string? operationName,
        [FromQuery] string? variables,
        CancellationToken cancellationToken)
    {
        var (body, error) = BuildGetBody(options, query, operationName, variables);
        if (error is not null)
            return error;

        var version = ResolveVersion(contextAccessor, loggerFactory);
        var result = await executionService.ExecuteInstanceAsync(body!, version, resourceType, id, cancellationToken);
        return await WriteResultAsync(context, result, cancellationToken);
    }

    private static async Task<(GraphQlRequestBody? Body, IResult? Error)> ReadBodyAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        GraphQlRequestBody body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<GraphQlRequestBody>(
                       context.Request.Body, JsonOptions, cancellationToken)
                   ?? new GraphQlRequestBody(null, null, null);
        }
        catch (JsonException ex)
        {
            return (null, Results.BadRequest($"Invalid JSON in request body: {ex.Message}"));
        }

        if (string.IsNullOrWhiteSpace(body.Query))
            return (null, Results.BadRequest("'query' is required."));

        return (body, null);
    }

    private static (GraphQlRequestBody? Body, IResult? Error) BuildGetBody(
        IOptions<ExperimentalOptions> options,
        string? query,
        string? operationName,
        string? variables)
    {
        if (!options.Value.Features.GraphQl.EnableGetRequests)
            return (null, Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

        if (string.IsNullOrWhiteSpace(query))
            return (null, Results.BadRequest("'query' query parameter is required."));

        try
        {
            JsonElement? parsedVariables = null;
            if (!string.IsNullOrEmpty(variables))
                parsedVariables = JsonSerializer.Deserialize<JsonElement>(variables);

            return (new GraphQlRequestBody(query, operationName, parsedVariables), null);
        }
        catch (JsonException ex)
        {
            return (null, Results.BadRequest($"Invalid JSON in 'variables' query parameter: {ex.Message}"));
        }
    }

    private static FhirVersion ResolveVersion(
        IFhirRequestContextAccessor contextAccessor,
        ILoggerFactory loggerFactory)
    {
        var version = contextAccessor.RequestContext?.FhirVersion;
        if (version is not null)
            return version.Value;

        loggerFactory.CreateLogger(typeof(GraphQlEndpoints))
            .LogWarning("FHIR request context unavailable; defaulting GraphQL execution to {Version}.", FhirVersion.R4);
        return FhirVersion.R4;
    }

    private static async Task<IResult> WriteResultAsync(
        HttpContext context,
        HotChocolate.Execution.IExecutionResult result,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = StatusCodes.Status200OK;
        await ResultFormatter.FormatAsync(result, context.Response.Body, cancellationToken);
        return Results.Empty;
    }
}

