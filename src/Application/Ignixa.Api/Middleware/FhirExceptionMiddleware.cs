// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Ignixa.Api.Http;
using Ignixa.Serialization;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Api.Middleware;

/// <summary>
/// Middleware to handle exceptions and return FHIR OperationOutcome responses.
/// </summary>
public class FhirExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FhirExceptionMiddleware> _logger;

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
        catch (FhirException fhirEx)
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
        if (exception is FhirException fhirException)
        {
            context.Response.ContentType = KnownContentTypes.ApplicationFhirJson;
            context.Response.StatusCode = fhirException.StatusCode;

            return context.Response.Body.WriteAsync(fhirException.OperationOutcome.SerializeToBytes()).AsTask();
        }

        // Handle other exceptions with generic OperationOutcome
        var statusCode = HttpStatusCode.InternalServerError;
        var severity = OperationOutcomeJsonNode.IssueSeverity.Error;
        var code = OperationOutcomeJsonNode.IssueType.Exception;

        // Map specific exceptions to HTTP status codes
        if (exception is ArgumentException or ArgumentNullException)
        {
            statusCode = HttpStatusCode.BadRequest;
            code = OperationOutcomeJsonNode.IssueType.Invalid;
        }
        else if (exception is InvalidOperationException)
        {
            statusCode = HttpStatusCode.BadRequest;
            code = OperationOutcomeJsonNode.IssueType.Processing;
        }

        var operationOutcome = new OperationOutcomeJsonNode();
        operationOutcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = severity,
            Code = code,
            Diagnostics = exception.Message
        });

        context.Response.ContentType = KnownContentTypes.ApplicationFhirJson;
        context.Response.StatusCode = (int)statusCode;

        return context.Response.Body.WriteAsync(operationOutcome.SerializeToBytes()).AsTask();
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
