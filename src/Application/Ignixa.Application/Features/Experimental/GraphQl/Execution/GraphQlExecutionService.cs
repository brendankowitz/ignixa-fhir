// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate.Execution;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;

namespace Ignixa.Application.Features.Experimental.GraphQl.Execution;

public sealed class GraphQlExecutionService(IRequestExecutorResolver executorResolver)
    : IGraphQlExecutionService
{
    public Task<IExecutionResult> ExecuteAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(request, version, null, null, cancellationToken);

    public Task<IExecutionResult> ExecuteInstanceAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(request, version, resourceType, resourceId, cancellationToken);

    private async Task<IExecutionResult> ExecuteCoreAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken)
    {
        var schemaName = GraphQlNamingHelper.GetSchemaName(version);
        var executor = await executorResolver.GetRequestExecutorAsync(schemaName, cancellationToken);

        var builder = OperationRequestBuilder.New()
            .SetDocument(request.Query ?? string.Empty);

        if (request.OperationName is not null)
            builder.SetOperationName(request.OperationName);

        if (request.Variables.HasValue)
            builder.SetVariableValues(DeserializeVariables(request.Variables.Value));

        if (resourceType is not null)
        {
            builder.AddGlobalState("InstanceResourceType", resourceType);
            builder.AddGlobalState("InstanceResourceId", (object?)resourceId);
        }

        return await executor.ExecuteAsync(builder.Build(), cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> DeserializeVariables(JsonElement variables)
    {
        var result = new Dictionary<string, object?>();
        if (variables.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in variables.EnumerateObject())
            result[property.Name] = ExtractValue(property.Value);

        return result;
    }

    private static object? ExtractValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt32(out var i) => i,
        JsonValueKind.Number when element.TryGetInt64(out var l) => l,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => DeserializeVariables(element),
        _ => element.GetRawText()
    };
}
