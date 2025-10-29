// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Specification;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Ignixa.Api.Filters;

/// <summary>
/// Endpoint filter that validates FHIR resource types against the tenant's supported FHIR version.
///
/// Runs AFTER TenantResolutionMiddleware, so tenant context is already available in HttpContext.Items.
/// Returns 404 Not Found with FHIR OperationOutcome if the resource type is not supported.
///
/// Usage:
///   var group = endpoints.MapGroup("/tenant/{tenantId:int}")
///       .AddEndpointFilter<ResourceTypeValidationFilter>();
/// </summary>
public class ResourceTypeValidationFilter : IEndpointFilter
{
    private readonly FhirSchemaProviderResolver _schemaProviderResolver;
    private readonly ILogger<ResourceTypeValidationFilter> _logger;

    public ResourceTypeValidationFilter(
        FhirSchemaProviderResolver schemaProviderResolver,
        ILogger<ResourceTypeValidationFilter> logger)
    {
        _schemaProviderResolver = schemaProviderResolver;
        _logger = logger;
    }

    /// <summary>
    /// Validates the resource type from the route against the tenant's supported FHIR version.
    /// </summary>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract resourceType from route parameters
        // This works for routes like /{resourceType}/{id} or /{resourceType}
        if (!httpContext.Request.RouteValues.TryGetValue("resourceType", out var resourceTypeObj) ||
            resourceTypeObj is not string resourceType ||
            string.IsNullOrWhiteSpace(resourceType))
        {
            // No resourceType parameter, skip validation
            return await next(context);
        }

        // Get tenant configuration (already set by TenantResolutionMiddleware)
        if (!httpContext.Items.TryGetValue("TenantConfiguration", out var tenantConfigObj) ||
            tenantConfigObj is not TenantConfiguration tenantConfig)
        {
            _logger.LogError("TenantConfiguration not found in HttpContext.Items");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        // Resolve FHIR schema provider for tenant's version
        var fhirSpec = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var schemaProvider = _schemaProviderResolver(fhirSpec);

        // Validate resource type is supported by this FHIR version
        if (!schemaProvider.ResourceTypeNames.Contains(resourceType))
        {
            _logger.LogWarning(
                "Invalid resource type '{ResourceType}' for tenant {TenantId} (FHIR {FhirVersion})",
                resourceType,
                tenantConfig.TenantId,
                tenantConfig.FhirVersion);

            // Return 404 Not Found with FHIR OperationOutcome
            return Results.Json(
                new
                {
                    resourceType = "OperationOutcome",
                    issue = new[]
                    {
                        new
                        {
                            severity = "error",
                            code = "not-found",
                            diagnostics = $"Resource type '{resourceType}' is not supported by this server (FHIR {tenantConfig.FhirVersion})",
                        },
                    },
                },
                statusCode: StatusCodes.Status404NotFound);
        }

        // Resource type is valid - continue to handler
        return await next(context);
    }
}
