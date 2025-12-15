// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization;
using Ignixa.Domain.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Api.Filters;

/// <summary>
/// Endpoint filter that creates AuditEvent resources for FHIR operations.
/// Runs AFTER FhirAuthorizationFilter (only audit authorized requests).
///
/// Architecture Decision: Uses fire-and-forget pattern to avoid blocking responses.
/// Audit failures are logged but don't fail the request.
///
/// Usage:
///   var group = endpoints.MapGroup("/tenant/{tenantId:int}")
///       .AddEndpointFilter&lt;FhirAuthorizationFilter&gt;()  // Run first
///       .AddEndpointFilter&lt;FhirAuditFilter&gt;();         // Run after authz
/// </summary>
public class FhirAuditFilter(IAuditLogger auditLogger, ILogger<FhirAuditFilter> logger) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var startTime = DateTimeOffset.UtcNow;

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
            // Create audit event (fire-and-forget)
            var endTime = DateTimeOffset.UtcNow;

#pragma warning disable CS4014 // Do not await - fire-and-forget pattern for audit
            CreateAuditEventAsync(httpContext, startTime, endTime, exception);
#pragma warning restore CS4014
        }

        return result;
    }

    /// <summary>
    /// Creates an audit event asynchronously (fire-and-forget).
    /// </summary>
    private async Task CreateAuditEventAsync(
        HttpContext httpContext,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        Exception? exception)
    {
        try
        {
            // Note: These values are already validated/parsed by ASP.NET Core pipeline:
            // - Method: HttpRequest.Method is validated by Kestrel
            // - Path: PathString is parsed/validated
            // - ClientIp: IPAddress.ToString() is safe
            // - UserId: JWT claims are validated by auth middleware
            // Structured logging with {placeholders} is immune to log injection.
            var userId = httpContext.User.FindFirst(FhirClaimTypes.Subject)?.Value ??
                        httpContext.User.FindFirst(FhirClaimTypes.ObjectId)?.Value ??
                        httpContext.User.FindFirst(FhirClaimTypes.NameIdentifier)?.Value ??
                        "anonymous";

            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var method = httpContext.Request.Method;
            // Sanitize path to prevent log injection attacks (char overload avoids CA1307)
            var path = (httpContext.Request.Path.Value ?? "/")
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            var statusCode = httpContext.Response.StatusCode;

            var action = DetermineAuditAction(method);
            var outcome = DetermineAuditOutcome(statusCode, exception);

            var auditEvent = new HttpRequestAuditEvent
            {
                Action = action,
                Outcome = outcome,
                UserId = userId,
                ClientIp = clientIp,
                Method = method,
                Path = path,
                StatusCode = statusCode,
                DurationMs = (endTime - startTime).TotalMilliseconds,
                CorrelationId = httpContext.TraceIdentifier
            };

            auditLogger.LogHttpRequest(auditEvent);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            // Log but don't fail request - audit is best-effort
            // Sanitize to prevent log injection in error path (char overload avoids CA1307)
            var safeMethod = httpContext.Request.Method.Replace('\r', ' ').Replace('\n', ' ');
            var safePath = (httpContext.Request.Path.Value ?? "/").Replace('\r', ' ').Replace('\n', ' ');
            logger.LogError(ex, "Failed to create audit event for {Method} {Path}",
                safeMethod,
                safePath);
        }
    }

    /// <summary>
    /// Determines audit action from HTTP method.
    /// Maps to FHIR AuditEvent.action value set.
    /// </summary>
    private static string DetermineAuditAction(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => "R",    // Read
            "POST" => "C",   // Create
            "PUT" => "U",    // Update
            "PATCH" => "U",  // Update
            "DELETE" => "D", // Delete
            _ => "E"         // Execute
        };
    }

    /// <summary>
    /// Determines audit outcome from response status code.
    /// Maps to FHIR AuditEvent.outcome value set.
    /// </summary>
    private static string DetermineAuditOutcome(int statusCode, Exception? exception)
    {
        if (exception is not null)
        {
            return "8"; // Serious failure
        }

        return statusCode switch
        {
            >= 200 and < 300 => "0", // Success
            >= 400 and < 500 => "4", // Minor failure (client error)
            >= 500 => "8",           // Serious failure (server error)
            _ => "0"                 // Default to success
        };
    }
}
