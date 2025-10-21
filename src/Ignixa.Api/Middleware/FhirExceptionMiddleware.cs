// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Ignixa.Application.Features.ConditionalOperations;
using Ignixa.Application.Features.Resource;
using Ignixa.SourceNodeSerialization;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Handle ValidationException with proper FHIR OperationOutcome
        if (exception is ValidationException validationException)
        {
            context.Response.ContentType = "application/fhir+json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

            // Use MutableNode.ToJsonString() to get clean FHIR JSON (not the wrapper object)
            var operationOutcomeJson = validationException.OperationOutcome.SerializeToString();
            return context.Response.WriteAsync(operationOutcomeJson);
        }

        // Handle ConditionalOperationException with verbose FHIR OperationOutcome
        if (exception is ConditionalOperationException conditionalEx)
        {
            context.Response.ContentType = "application/fhir+json";

            // Determine status code based on match count
            // 0 matches: 404 Not Found
            // Multiple matches: 412 Precondition Failed
            context.Response.StatusCode = conditionalEx.MatchCount == 0
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status412PreconditionFailed;

            var issueCode = conditionalEx.MatchCount == 0 ? "not-found" : "duplicate";

            var conditionalOutcome = new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = issueCode,
                        diagnostics = conditionalEx.Message,
                        location = !string.IsNullOrEmpty(conditionalEx.SearchCriteria)
                            ? new[] { conditionalEx.SearchCriteria }
                            : Array.Empty<string>()
                    }
                }
            };

            var conditionalJson = JsonSerializer.Serialize(conditionalOutcome, JsonOptions);
            return context.Response.WriteAsync(conditionalJson);
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
