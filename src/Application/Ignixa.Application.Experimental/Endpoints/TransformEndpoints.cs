// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Experimental.Extensions;
using Ignixa.Application.Experimental.Features.Transform;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;

namespace Ignixa.Application.Experimental.Endpoints;

/// <summary>
/// Registers FHIR $transform operation endpoints.
/// This is an experimental feature that can be enabled/disabled via configuration.
/// </summary>
public static class TransformEndpoints
{
    private const string ApplicationFhirJson = "application/fhir+json";

    /// <summary>
    /// Maps $transform endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapTransformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTransformTenantEndpoints();
        endpoints.MapTransformAgnosticEndpoints();
        return endpoints;
    }

    /// <summary>
    /// Registers tenant-explicit $transform endpoints (/tenant/{tenantId}/...).
    /// </summary>
    private static void MapTransformTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tenantGroup = endpoints.MapGroup("/tenant/{tenantId:int}");

        // POST /tenant/{tenantId}/StructureMap/$transform - Type-level transform
        tenantGroup.MapPost("/StructureMap/$transform", HandleTransformType)
            .WithName("TransformResourceType")
            .WithTags("Experimental", "Transform")
            .Accepts<object>(ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, ApplicationFhirJson);

        // POST /tenant/{tenantId}/StructureMap/{id}/$transform - Instance-level transform
        tenantGroup.MapPost("/StructureMap/{id}/$transform", HandleTransformInstance)
            .WithName("TransformResourceInstance")
            .WithTags("Experimental", "Transform")
            .Accepts<object>(ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, ApplicationFhirJson);
    }

    /// <summary>
    /// Registers tenant-agnostic $transform endpoints (/).
    /// Only enabled in single-tenant mode.
    /// </summary>
    private static void MapTransformAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /StructureMap/$transform - Type-level transform (agnostic route)
        endpoints.MapPost("/StructureMap/$transform", HandleTransformTypeAgnostic)
            .WithName("TransformResourceTypeAgnostic")
            .WithTags("Experimental", "Transform")
            .Accepts<object>(ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, ApplicationFhirJson);

        // POST /StructureMap/{id}/$transform - Instance-level transform (agnostic route)
        endpoints.MapPost("/StructureMap/{id}/$transform", HandleTransformInstanceAgnostic)
            .WithName("TransformResourceInstanceAgnostic")
            .WithTags("Experimental", "Transform")
            .Accepts<object>(ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status404NotFound, ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, ApplicationFhirJson);
    }

    private static async Task<IResult> HandleTransformType(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        ParametersJsonNode? parameters;
        try
        {
            await using var memoryStream = memoryStreamManager.GetStream("transform-request");
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

        var source = parameters.GetParameterStringValue("source");
        var sourceMap = parameters.GetParameterResource<StructureMapJsonNode>("sourceMap");
        var srcMaps = parameters.GetParameterStringValues("srcMap").ToList();
        var supportingMaps = parameters.GetParameterResources<StructureMapJsonNode>("supportingMap").ToList();
        var content = parameters.GetParameterResource<ResourceJsonNode>("content");

        var command = new TransformResourceCommand(
            Source: source,
            SourceMap: sourceMap,
            SrcMaps: srcMaps,
            SupportingMaps: supportingMaps,
            Content: content);

        try
        {
            var result = await mediator.SendAsync(command, cancellationToken);
            var resourceBytes = result.SerializeToBytes();
            return Results.Bytes(resourceBytes, ApplicationFhirJson);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Processing,
                $"Transformation failed: {ex.Message}"));
        }
    }

    private static async Task<IResult> HandleTransformInstance(
        HttpContext context,
        int tenantId,
        string id,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        ParametersJsonNode? parameters;
        try
        {
            await using var memoryStream = memoryStreamManager.GetStream("transform-request");
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

        var content = parameters.GetParameterResource<ResourceJsonNode>("content");
        var command = new TransformResourceCommand(
            Source: $"StructureMap/{id}",
            Content: content);

        try
        {
            var result = await mediator.SendAsync(command, cancellationToken);
            var resourceBytes = result.SerializeToBytes();
            return Results.Bytes(resourceBytes, ApplicationFhirJson);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(CreateOperationOutcome(
                    OperationOutcomeJsonNode.IssueSeverity.Error,
                    OperationOutcomeJsonNode.IssueType.NotFound,
                    $"StructureMap/{id} not found"));
            }

            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Processing,
                $"Transformation failed: {ex.Message}"));
        }
    }

    private static async Task<IResult> HandleTransformTypeAgnostic(
        HttpContext context,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/StructureMap/$transform"));
        }

        return await HandleTransformType(context, tenantId, mediator, memoryStreamManager, cancellationToken);
    }

    private static async Task<IResult> HandleTransformInstanceAgnostic(
        HttpContext context,
        string id,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return Results.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/StructureMap/{id}/$transform"));
        }

        return await HandleTransformInstance(context, tenantId, id, mediator, memoryStreamManager, cancellationToken);
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
