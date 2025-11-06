// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for health checks.
/// Provides a simple /health/check endpoint for container health monitoring and load balancer checks.
/// </summary>
public static class HealthCheckEndpoints
{
    public static IEndpointRouteBuilder MapHealthCheckEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Health check endpoint: GET /health/check
        endpoints.MapGet("/health/check", HandleHealthCheck)
            .WithName("HealthCheck")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status503ServiceUnavailable, contentType: "application/json")
            .WithOpenApi();

        return endpoints;
    }

    /// <summary>
    /// GET /health/check
    /// Simple health check endpoint for container/load balancer monitoring.
    /// Returns 200 OK if the application is healthy, 503 Service Unavailable if unhealthy.
    /// </summary>
    private static IResult HandleHealthCheck(
        HttpContext context,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GET /health/check");

        try
        {
            // Basic health check: if we can process the request, we're healthy
            var response = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = "0.1.0"
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed");

            var errorResponse = new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                error = ex.Message
            };

            return Results.Json(errorResponse, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
