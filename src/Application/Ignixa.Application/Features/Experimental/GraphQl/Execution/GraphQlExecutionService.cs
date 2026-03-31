// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate.Execution;
using HotChocolate.Language;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.Directives;
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

        var result = await executor.ExecuteAsync(builder.Build(), cancellationToken);

        return PostProcessDirectives(result, request.Query);
    }

    private static IExecutionResult PostProcessDirectives(IExecutionResult result, string? query)
    {
        if (string.IsNullOrEmpty(query))
            return result;

        if (result is not IOperationResult { Data: { } readOnlyData })
            return result;

        try
        {
            var document = Utf8GraphQLParser.Parse(query);

            // Quick check: skip parsing overhead when no directives present
            var hasDirectives = document.Definitions
                .OfType<OperationDefinitionNode>()
                .Any(op => HasAnyDirectives(op.SelectionSet));

            if (!hasDirectives)
                return result;

            // Data is IReadOnlyDictionary — try mutable cast first, deep-copy if needed
            if (readOnlyData is IDictionary<string, object?> mutableData)
            {
                FlattenResultProcessor.Process(document, mutableData);
                return result;
            }

            // Deep-copy to mutable dict, process, build new result
            var dataCopy = FlattenResultProcessor.DeepCopyData(readOnlyData);
            FlattenResultProcessor.Process(document, dataCopy);

            return new OperationResult(
                dataCopy,
                result.ExpectOperationResult().Errors,
                result.ExpectOperationResult().Extensions,
                result.ContextData,
                result.ExpectOperationResult().Items,
                result.ExpectOperationResult().Incremental,
                result.ExpectOperationResult().Label,
                result.ExpectOperationResult().Path,
                result.ExpectOperationResult().HasNext,
                result.ExpectOperationResult().RequestIndex,
                result.ExpectOperationResult().VariableIndex);
        }
        catch
        {
            // If post-processing fails, return the unmodified result
            return result;
        }
    }

    private static bool HasAnyDirectives(SelectionSetNode? selectionSet)
    {
        if (selectionSet is null)
            return false;

        foreach (var selection in selectionSet.Selections.OfType<FieldNode>())
        {
            if (selection.Directives.Count > 0)
                return true;
            if (HasAnyDirectives(selection.SelectionSet))
                return true;
        }

        return false;
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
        JsonValueKind.Array => element.EnumerateArray().Select(ExtractValue).ToList(),
        _ => element.GetRawText()
    };
}
