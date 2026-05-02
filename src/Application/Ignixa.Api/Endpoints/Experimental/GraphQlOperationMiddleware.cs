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

        var originalBody = context.Response.Body;
        using var capturedBody = new MemoryStream();
        context.Response.Body = capturedBody;

        try
        {
            await next(context);

            if (context.Response.StatusCode >= 400)
            {
                capturedBody.Seek(0, SeekOrigin.Begin);
                await capturedBody.CopyToAsync(originalBody);
                return;
            }

            capturedBody.Seek(0, SeekOrigin.Begin);
            JsonElement operationResult;
            try
            {
                operationResult = await JsonSerializer.DeserializeAsync<JsonElement>(capturedBody, cancellationToken: context.RequestAborted);
            }
            catch (JsonException)
            {
                capturedBody.Seek(0, SeekOrigin.Begin);
                await capturedBody.CopyToAsync(originalBody);
                return;
            }

            if (!operationResult.TryGetProperty("resourceType", out var rtProp)
                || rtProp.GetString() is not string resourceType)
            {
                capturedBody.Seek(0, SeekOrigin.Begin);
                await capturedBody.CopyToAsync(originalBody);
                return;
            }

            var executionService = context.RequestServices.GetRequiredService<IGraphQlExecutionService>();
            var contextAccessor = context.RequestServices.GetRequiredService<IFhirRequestContextAccessor>();
            var version = contextAccessor.RequestContext?.FhirVersion ?? FhirVersion.R4;

            var wrappedQuery = WrapOperationQuery(graphQlQuery, resourceType);
            var requestBody = new GraphQlRequestBody(wrappedQuery, null, null);
            var globalState = new Dictionary<string, object?>
            {
                ["OperationResult"] = operationResult,
            };

            var result = await executionService.ExecuteAsync(requestBody, version, globalState, context.RequestAborted);
            result = UnwrapOperationResult(result, resourceType);

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = StatusCodes.Status200OK;
            await ResultFormatter.FormatAsync(result, originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    /// <summary>
    /// Wraps an operation-level selection set into a typed resource query.
    /// Input: <c>{ result: parameter(name: "result") { value: valueBoolean } }</c>
    /// Output: <c>{ Parameters { result: parameter(name: "result") { value: valueBoolean } } }</c>
    /// </summary>
    private static string WrapOperationQuery(string query, string resourceType)
    {
        var trimmed = query.Trim();

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            var innerSelections = trimmed[1..^1].Trim();
            return $"{{ {resourceType} {{ {innerSelections} }} }}";
        }

        return query;
    }

    /// <summary>
    /// Unwraps operation query result: extracts the resource object from the wrapper field
    /// so fields appear at the data root level.
    /// </summary>
    private static IExecutionResult UnwrapOperationResult(IExecutionResult result, string resourceType)
    {
        if (result is not IOperationResult { Data: { } data } opResult)
            return result;

        if (!data.TryGetValue(resourceType, out var resourceData))
            return result;

        if (resourceData is not IReadOnlyDictionary<string, object?> resourceDict)
            return result;

        return OperationResultBuilder
            .FromResult(opResult)
            .SetData(resourceDict)
            .Build();
    }
}
