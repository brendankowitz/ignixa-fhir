// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Api.Filters;

/// <summary>
/// Endpoint filter that records metrics for FHIR operations.
/// Runs AFTER FhirAuditFilter in the filter pipeline.
/// Fire-and-forget pattern - does not block responses.
///
/// Usage:
///   var group = endpoints.MapGroup("/tenant/{tenantId:int}")
///       .AddEndpointFilter&lt;FhirAuthorizationFilter&gt;()
///       .AddEndpointFilter&lt;FhirAuditFilter&gt;()
///       .AddEndpointFilter&lt;FhirMetricsFilter&gt;();
/// </summary>
public class FhirMetricsFilter(
    IMetricsService metricsService,
    IFhirRequestContextAccessor fhirContextAccessor,
    ILogger<FhirMetricsFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var startTime = DateTimeOffset.UtcNow;
        var requestSize = httpContext.Request.ContentLength ?? 0;

        // Execute endpoint handler
        object? result;
        Exception? exception = null;

        try
        {
            result = await next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            // Record metrics (fire-and-forget)
            var endTime = DateTimeOffset.UtcNow;
#pragma warning disable CS4014
            RecordMetricsAsync(httpContext, startTime, endTime, requestSize, exception);
#pragma warning restore CS4014
        }

        return result;
    }

    private async Task RecordMetricsAsync(
        HttpContext httpContext,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        long requestSize,
        Exception? exception)
    {
        try
        {
            var fhirContext = fhirContextAccessor.RequestContext;
            if (fhirContext is null) return; // Skip if no FHIR context

            var metrics = new FhirOperationMetrics
            {
                Timestamp = startTime,
                CorrelationId = httpContext.TraceIdentifier,
                OperationId = httpContext.TraceIdentifier,
                TenantId = fhirContext.TenantId,
                ResourceType = httpContext.Request.RouteValues.TryGetValue("resourceType", out var rt) ? rt as string : null,
                ResourceId = httpContext.Request.RouteValues.TryGetValue("id", out var id) ? id as string : null,
                FhirVersion = fhirContext.FhirVersion.ToString(),
                HttpMethod = httpContext.Request.Method,
                FhirOperation = DetermineFhirOperation(httpContext),
                StatusCode = httpContext.Response.StatusCode,
                Success = exception is null && httpContext.Response.StatusCode < 400,
                RequestSizeBytes = requestSize,
                ResponseSizeBytes = httpContext.Response.ContentLength ?? 0,
                DurationMilliseconds = (long)(endTime - startTime).TotalMilliseconds
            };

            await metricsService.RecordMetricAsync(metrics);
        }
        catch (Exception ex)
        {
            // Sanitize to prevent log injection in error path (char overload avoids CA1307)
            var safeMethod = httpContext.Request.Method.Replace('\r', ' ').Replace('\n', ' ');
            var safePath = (httpContext.Request.Path.Value ?? "/").Replace('\r', ' ').Replace('\n', ' ');
            logger.LogError(ex, "Failed to record metrics for {Method} {Path}",
                safeMethod,
                safePath);
        }
    }

    private static string DetermineFhirOperation(HttpContext ctx)
    {
        var method = ctx.Request.Method.ToUpperInvariant();
        var path = ctx.Request.Path.Value ?? string.Empty;
        var hasId = ctx.Request.RouteValues.ContainsKey("id");

        return (method, hasId, path.Contains("/_search", StringComparison.Ordinal)) switch
        {
            ("GET", true, _) => "read",
            ("GET", false, _) => "search",
            ("POST", _, true) => "search",
            ("POST", false, _) => "create",
            ("PUT", _, _) => "update",
            ("DELETE", _, _) => "delete",
            ("PATCH", _, _) => "patch",
            _ => "unknown"
        };
    }
}
