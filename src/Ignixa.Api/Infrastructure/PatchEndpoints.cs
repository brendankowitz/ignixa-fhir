// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Application.Features.Patch;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Registers FHIR PATCH endpoints using FHIRPath Patch (Parameters resource).
/// </summary>
public static class PatchEndpoints
{
    private const string ContentTypeApplicationFhirJson = "application/fhir+json";
    private const string ContentTypeApplicationJson = "application/json";
    private static readonly string[] PatchMethod = new[] { "PATCH" };

    /// <summary>
    /// Registers FHIR PATCH endpoints.
    ///
    /// Route Patterns:
    /// 1. Tenant-explicit: PATCH /tenant/{tenantId:int}/{resourceType}/{id} - Always supported
    /// 2. Tenant-agnostic: PATCH /{resourceType}/{id} - Single-tenant auto-detect
    ///
    /// Request Body: Parameters resource (FHIRPath Patch operations)
    /// Content-Type: application/fhir+json
    /// </summary>
    public static IEndpointRouteBuilder MapPatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TENANT-EXPLICIT ROUTE (always supported)
        endpoints.MapMethods(
            "/tenant/{tenantId:int}/{resourceType}/{id}",
            PatchMethod,
            HandlePatchResource)
            .WithName("PatchResource")
            .Accepts<object>(ContentTypeApplicationFhirJson, ContentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, ContentTypeApplicationFhirJson, ContentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status400BadRequest, ContentTypeApplicationFhirJson)
            .Produces(StatusCodes.Status404NotFound);

        // TENANT-AGNOSTIC ROUTE (single-tenant auto-detect)
        endpoints.MapMethods(
            "/{resourceType}/{id}",
            PatchMethod,
            async (HttpContext context, string resourceType, string id,
                   [FromServices] IMediator mediator,
                   CancellationToken cancellationToken) =>
            {
                var tenantId = ExtractTenantId(context);
                return await HandlePatchResource(context, tenantId, resourceType, id, mediator, cancellationToken);
            })
            .WithName("PatchResourceAgnostic")
            .Accepts<object>(ContentTypeApplicationFhirJson, ContentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, ContentTypeApplicationFhirJson, ContentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status400BadRequest, ContentTypeApplicationFhirJson)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandlePatchResource(
        HttpContext context,
        int tenantId,
        string resourceType,
        string id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            // Read Parameters resource from body
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var patchDocument = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(patchDocument))
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
                            diagnostics = "Request body cannot be empty for PATCH operation"
                        }
                    }
                });
            }

            // Create command
            var command = new PatchResourceCommand(
                tenantId,
                resourceType,
                id,
                patchDocument);

            // Execute via Medino
            var result = await mediator.SendAsync(command, cancellationToken);

            if (result == null)
            {
                return Results.NotFound();
            }

            // Set response headers
            context.Response.Headers["ETag"] = $"W/\"{result.VersionId}\"";
            context.Response.Headers["Last-Modified"] = result.LastModified.ToString("R");
            context.Response.ContentType = ContentTypeApplicationFhirJson;

            // Return patched resource
            return Results.Ok(result.Resource);
        }
        catch (FhirPatchException ex)
        {
            // Return OperationOutcome for FHIR Patch errors
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = ex.Message
                    }
                }
            });
        }
        catch (Exception ex)
        {
            // Return OperationOutcome for unexpected errors
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Extracts tenant ID from HttpContext.Items (populated by TenantResolutionMiddleware).
    /// Throws if tenant ID not found - should never happen if middleware ran successfully.
    /// </summary>
    private static int ExtractTenantId(HttpContext context)
    {
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            throw new InvalidOperationException(
                "TenantId not found in HttpContext.Items. TenantResolutionMiddleware may not have run.");
        }

        return tenantId;
    }
}
