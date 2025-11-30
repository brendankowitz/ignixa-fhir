// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Middleware;

/// <summary>
/// Middleware that initializes IFhirRequestContext for each HTTP request.
/// Runs AFTER TenantResolutionMiddleware to access tenant information from HttpContext.Items.
/// Populates tenant, FHIR version, resource type, and version context reference.
///
/// Execution Order:
/// 1. TenantResolutionMiddleware (sets HttpContext.Items["TenantId"])
/// 2. FhirRequestContextMiddleware (creates IFhirRequestContext) ← THIS
/// 3. Routing (sets route values)
/// 4. Endpoint execution (handlers access context via IFhirRequestContextAccessor)
/// </summary>
public class FhirRequestContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FhirRequestContextMiddleware> _logger;

    public FhirRequestContextMiddleware(
        RequestDelegate next,
        ILogger<FhirRequestContextMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(
        HttpContext httpContext,
        IFhirRequestContextAccessor contextAccessor,
        IFhirVersionContext versionContext)
    {
        // Skip if context already set (e.g., bundle entry created isolated context)
        if (contextAccessor.RequestContext != null)
        {
            _logger.LogTrace("FHIR request context already set, skipping middleware initialization");
            await _next(httpContext);
            return;
        }

        // Create new context for this HTTP request
        var fhirContext = new FhirRequestContext();

        // Extract tenant information (if available from TenantResolutionMiddleware)
        if (httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) &&
            tenantIdObj is int tenantId)
        {
            fhirContext.TenantId = tenantId;
            fhirContext.TenantConfiguration =
                httpContext.Items["TenantConfiguration"] as TenantConfiguration;

            _logger.LogTrace(
                "Extracted tenant from HttpContext.Items: TenantId={TenantId}",
                tenantId);
        }

        // Extract FHIR version from Content-Type/Accept headers
        fhirContext.FhirVersion = FhirVersionExtractor.ExtractFhirVersion(httpContext);

        // Extract resource type from route parameters
        if (httpContext.Request.RouteValues.TryGetValue("resourceType", out var resourceTypeObj))
        {
            fhirContext.ResourceType = resourceTypeObj?.ToString();
        }

        // Set version context reference for convenience (Option A)
        fhirContext.VersionContext = versionContext;

        // Note: Bundle processing state (DeferredWriteCoordinator, BundleEntryIndex, BundleAssignedResourceId)
        // is set directly by BundleEntryExecutor in isolated contexts, not by this middleware.
        // ExecutingBatchOrTransaction will be set when DeferredWriteCoordinator is assigned.
        fhirContext.ExecutingBatchOrTransaction = fhirContext.DeferredWriteCoordinator != null;

        // Set context for downstream handlers
        contextAccessor.RequestContext = fhirContext;

        _logger.LogDebug(
            "Initialized FHIR request context: Tenant={TenantId}, Version={Version}, ResourceType={ResourceType}, InBundle={InBundle}",
            fhirContext.TenantId,
            fhirContext.FhirVersion,
            fhirContext.ResourceType ?? "(none)",
            fhirContext.ExecutingBatchOrTransaction);

        await _next(httpContext);
    }
}
