// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Extensions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.Operations.Features.DeIdentify;
using Ignixa.DeId.Darts;
using Ignixa.DeId.Darts.Configuration;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Registers FHIR $de-identify operation endpoints.
/// </summary>
public static class DeIdOperationEndpoints
{
    /// <summary>
    /// Registers FHIR $de-identify operation endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapDeIdOperationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDeIdOperationTenantEndpoints();
        endpoints.MapDeIdOperationAgnosticEndpoints();
        return endpoints;
    }

    private static IEndpointRouteBuilder MapDeIdOperationTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tenantGroup = endpoints
            .MapGroup("/tenant/{tenantId:int}")
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>();

        tenantGroup.MapPost("/$de-identify", HandleDeIdentify)
            .WithName("DeIdentify")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    private static IEndpointRouteBuilder MapDeIdOperationAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/$de-identify", HandleDeIdentifyAgnostic)
            .WithName("DeIdentifyAgnostic")
            .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
            .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson);

        return endpoints;
    }

    private static async Task<IResult> HandleDeIdentify(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        return await HandleDeIdentifyInternal(context, tenantId, mediator, memoryStreamManager, cancellationToken);
    }

    private static async Task<IResult> HandleDeIdentifyAgnostic(
        HttpContext context,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            return FhirResults.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "TenantId not found. In multi-tenant mode, use /tenant/{tenantId}/$de-identify"));
        }

        return await HandleDeIdentifyInternal(context, tenantId, mediator, memoryStreamManager, cancellationToken);
    }

    private static async Task<IResult> HandleDeIdentifyInternal(
        HttpContext context,
        int tenantId,
        IMediator mediator,
        RecyclableMemoryStreamManager memoryStreamManager,
        CancellationToken cancellationToken)
    {
        using var memoryStream = memoryStreamManager.GetStream();
        await context.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        if (memoryStream.Length == 0)
        {
            return FhirResults.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Required,
                "Request body must contain a FHIR Parameters resource with the resource to de-identify"));
        }

        ResourceJsonNode resourceNode;
        string policy;

        try
        {
            resourceNode = await JsonSourceNodeFactory.ParseAsync(memoryStream, cancellationToken);
        }
        catch (Exception ex)
        {
            return FhirResults.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
                $"Request body must be valid JSON: {ex.Message}"));
        }

        ResourceJsonNode? configLibrary = null;

        // The $de-identify operation accepts a Parameters resource per FHIR spec
        // parameter[0].name = "resource", parameter[0].resource = {Resource}
        // parameter[1].name = "policy", parameter[1].valueString = "HHS_SAFE_HARBOR_DETERMINISTIC_METHOD"
        // parameter[2].name = "configuration", parameter[2].resource = {Library} (custom de-id config)
        if (resourceNode.ResourceType == "Parameters")
        {
            var parametersNode = resourceNode.As<ParametersJsonNode>();
            var resourceParam = parametersNode.GetParameterResource<ResourceJsonNode>("resource");
            var policyParam = parametersNode.GetParameterStringValue("policy");
            var configurationParam = parametersNode.GetParameterResource<ResourceJsonNode>("configuration");

            if (resourceParam is null)
            {
                return FhirResults.BadRequest(CreateOperationOutcome(
                    OperationOutcomeJsonNode.IssueSeverity.Error,
                    OperationOutcomeJsonNode.IssueType.Required,
                    "Required parameter 'resource' is missing"));
            }

            resourceNode = resourceParam;
            policy = policyParam ?? DartsConstants.PolicySafeHarbor;
            configLibrary = configurationParam;
        }
        else
        {
            // Direct resource body (non-standard but convenient)
            policy = context.Request.Query["policy"].FirstOrDefault()
                ?? DartsConstants.PolicySafeHarbor;
        }

        // If no custom Library configuration was provided, use a built-in bootstrap policy.
        // Future enhancement: load Library resource from database by policy code.
        if (configLibrary is null)
        {
            try
            {
                configLibrary = CreateBootstrapLibrary(policy);
            }
            catch (ArgumentException ex)
            {
                return FhirResults.BadRequest(CreateOperationOutcome(
                    OperationOutcomeJsonNode.IssueSeverity.Error,
                    OperationOutcomeJsonNode.IssueType.Invalid,
                    ex.Message));
            }
        }

        var command = new DeIdentifyCommand(
            tenantId,
            resourceNode,
            policy,
            configLibrary);

        var result = await mediator.SendAsync(command, cancellationToken);

        return result.IsSuccess
            ? Results.Ok(result.OutputResource!.MutableNode)
            : FhirResults.BadRequest(CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Processing,
                result.ErrorMessage!));
    }

    private static ResourceJsonNode CreateBootstrapLibrary(string policy)
    {
        var options = policy switch
        {
            DartsConstants.PolicySafeHarbor => BootstrapPolicies.CreateSafeHarborOptions(),
            DartsConstants.PolicyExpertDetermination => BootstrapPolicies.CreateExpertDeterminationOptions(),
            _ => throw new ArgumentException($"Unknown de-identification policy: '{policy}'. Supported policies: {DartsConstants.PolicySafeHarbor}, {DartsConstants.PolicyExpertDetermination}.")
        };

        return LibraryConfigurationLoader.CreateLibraryResource(
            $"deid-{policy.ToUpperInvariant()}",
            policy,
            options);
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
