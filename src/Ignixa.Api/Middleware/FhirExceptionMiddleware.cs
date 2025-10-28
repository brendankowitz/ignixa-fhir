// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Ignixa.Api.Http;
using Ignixa.Serialization;

namespace Ignixa.Api.Middleware;

/// <summary>
/// Middleware to handle exceptions and return FHIR OperationOutcome responses.
/// </summary>
public class FhirExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FhirExceptionMiddleware> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FhirExceptionMiddleware(RequestDelegate next, ILogger<FhirExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Domain.Exceptions.FhirException fhirEx)
        {
            _logger.LogWarning(fhirEx, "FHIR exception occurred: {ExceptionType}", fhirEx.GetType().Name);
            await HandleExceptionAsync(context, fhirEx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // If response has already started, we can't modify headers
        // This typically happens when streaming responses encounter errors mid-stream
        if (context.Response.HasStarted)
        {
            // Response already started - just return to avoid "Headers are read-only" errors
            // The streaming operation will fail appropriately on the client side
            return Task.CompletedTask;
        }

        // Handle all FhirException types generically
        if (exception is Domain.Exceptions.FhirException fhirException)
        {
            context.Response.ContentType = KnownContentTypes.ApplicationFhirJson;
            context.Response.StatusCode = fhirException.StatusCode;

            var operationOutcomeJson = fhirException.OperationOutcome.SerializeToString();
            return context.Response.WriteAsync(operationOutcomeJson);
        }

        // Handle other exceptions with generic OperationOutcome
        var statusCode = HttpStatusCode.InternalServerError;
        var severity = "error";
        var code = "exception";

        // Map specific exceptions to HTTP status codes
        if (exception is ArgumentException or ArgumentNullException)
        {
            statusCode = HttpStatusCode.BadRequest;
            code = "invalid";
        }
        else if (exception is InvalidOperationException)
        {
            statusCode = HttpStatusCode.BadRequest;
            code = "processing";
        }

        var operationOutcome = new
        {
            resourceType = "OperationOutcome",
            issue = new[]
            {
                new
                {
                    severity,
                    code,
                    diagnostics = exception.Message
                }
            }
        };

        context.Response.ContentType = "application/fhir+json";
        context.Response.StatusCode = (int)statusCode;

        var json = JsonSerializer.Serialize(operationOutcome, JsonOptions);
        return context.Response.WriteAsync(json);
    }
}

/// <summary>
/// Extension methods for adding FhirExceptionMiddleware to the pipeline.
/// </summary>
public static class FhirExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseFhirExceptionHandler(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<FhirExceptionMiddleware>();
    }
}
