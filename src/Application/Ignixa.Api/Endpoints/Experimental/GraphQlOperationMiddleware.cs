// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate.Execution;
using HotChocolate.Execution.Serialization;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Infrastructure;

namespace Ignixa.Api.Endpoints.Experimental;

/// <summary>
/// Middleware that intercepts FHIR operation responses when a <c>_graphql</c> query parameter
/// is present, and applies the GraphQL query to the operation output.
/// </summary>
/// <remarks>
/// Per the FHIR specification, the <c>_graphql</c> query parameter can be added to any FHIR
/// operation to apply a GraphQL query to its result. For example:
/// <code>
/// GET /ValueSet/x/$validate-code?_graphql={result:parameter(name:"result"){value:valueBoolean}}
/// </code>
/// This middleware captures the downstream response body, then executes the GraphQL query
/// against the FHIR schema and returns the GraphQL result instead.
/// </remarks>
public sealed class GraphQlOperationMiddleware(Microsoft.AspNetCore.Http.RequestDelegate next)
{
    private static readonly JsonResultFormatter ResultFormatter = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var graphQlQuery = context.Request.Query["_graphql"].FirstOrDefault();

        if (string.IsNullOrEmpty(graphQlQuery))
        {
            await next(context);
            return;
        }

        // Capture the original response stream so we can intercept the output
        var originalBody = context.Response.Body;
        using var capturedBody = new MemoryStream();
        context.Response.Body = capturedBody;

        try
        {
            await next(context);

            // If the operation failed, return the error response as-is
            if (context.Response.StatusCode >= 400)
            {
                capturedBody.Seek(0, SeekOrigin.Begin);
                await capturedBody.CopyToAsync(originalBody);
                return;
            }

            // Parse the captured response as JSON
            capturedBody.Seek(0, SeekOrigin.Begin);
            JsonElement operationResult;
            try
            {
                operationResult = await JsonSerializer.DeserializeAsync<JsonElement>(capturedBody, cancellationToken: context.RequestAborted);
            }
            catch (JsonException)
            {
                // If the response isn't valid JSON, pass it through unchanged
                capturedBody.Seek(0, SeekOrigin.Begin);
                await capturedBody.CopyToAsync(originalBody);
                return;
            }

            // Execute GraphQL against the operation result
            var executionService = context.RequestServices.GetRequiredService<IGraphQlExecutionService>();
            var contextAccessor = context.RequestServices.GetRequiredService<IFhirRequestContextAccessor>();
            var version = contextAccessor.RequestContext?.FhirVersion ?? FhirVersion.R4;

            var requestBody = new GraphQlRequestBody(graphQlQuery, null, null);

            // TODO: For full implementation, the GraphQL query should execute against
            // the operationResult as root value. This requires a custom execution path
            // that binds the parsed JsonElement as the query root.
            // For now, we execute a standard GraphQL query and document this as a limitation.
            var result = await executionService.ExecuteAsync(requestBody, version, context.RequestAborted);

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await ResultFormatter.FormatAsync(result, originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
