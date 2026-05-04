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
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.GraphQl.Execution;

public sealed class GraphQlExecutionService(
    IRequestExecutorResolver executorResolver,
    ILogger<GraphQlExecutionService> logger)
    : IGraphQlExecutionService
{
    public Task<IExecutionResult> ExecuteAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(request, version, null, null, null, cancellationToken);

    public Task<IExecutionResult> ExecuteAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        IReadOnlyDictionary<string, object?> globalState,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(request, version, null, null, globalState, cancellationToken);

    public Task<IExecutionResult> ExecuteInstanceAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(request, version, resourceType, resourceId, null, cancellationToken);

    private async Task<IExecutionResult> ExecuteCoreAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        string? resourceType,
        string? resourceId,
        IReadOnlyDictionary<string, object?>? globalState,
        CancellationToken cancellationToken)
    {
        var schemaName = GraphQlNamingHelper.GetSchemaName(version);
        var executor = await executorResolver.GetRequestExecutorAsync(schemaName, cancellationToken);

        var effectiveQuery = request.Query ?? string.Empty;

        // Instance-level queries: wrap the user's selection set in a resource field.
        // Per the FHIR $graphql spec, instance queries like /Patient/123/$graphql
        // accept a bare selection set (e.g., { id name { family } }) that applies
        // directly to the resource. We rewrite this as { Patient(id: "123") { ... } }
        // so it's valid against the root Query type.
        var isInstanceQuery = resourceType is not null && resourceId is not null;
        if (isInstanceQuery)
        {
            effectiveQuery = WrapInstanceQuery(effectiveQuery, resourceType!, resourceId!);
        }

        effectiveQuery = InjectSlicePathFields(effectiveQuery);

        var builder = OperationRequestBuilder.New()
            .SetDocument(effectiveQuery);

        if (request.OperationName is not null)
            builder.SetOperationName(request.OperationName);

        if (request.Variables.HasValue)
            builder.SetVariableValues(DeserializeVariables(request.Variables.Value));

        if (resourceType is not null)
        {
            builder.AddGlobalState("InstanceResourceType", resourceType);
            builder.AddGlobalState("InstanceResourceId", (object?)resourceId);
        }

        if (globalState is not null)
        {
            foreach (var (key, value) in globalState)
                builder.AddGlobalState(key, value);
        }

        var result = await executor.ExecuteAsync(builder.Build(), cancellationToken);

        result = PostProcessDirectives(result, effectiveQuery, logger);

        // Instance-level queries: unwrap the resource data to the root level.
        // The wrapped query produces { data: { Patient: { ... } } } but the caller
        // expects { data: { id: ..., name: ... } }.
        if (isInstanceQuery)
        {
            result = UnwrapInstanceResult(result, resourceType!);
        }

        return result;
    }

    /// <summary>
    /// Wraps an instance-level selection set into a typed resource query.
    /// Input: <c>{ id name { family } }</c>
    /// Output: <c>{ Patient(id: "123") { id name { family } } }</c>
    /// </summary>
    private static string WrapInstanceQuery(string query, string resourceType, string resourceId)
    {
        var trimmed = query.Trim();

        // Extract inner selections from the braces
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            var innerSelections = trimmed[1..^1].Trim();
            // Escape any double quotes in the resource ID
            var escapedId = resourceId.Replace("\"", "\\\"", StringComparison.Ordinal);
            return $"{{ {resourceType}(id: \"{escapedId}\") {{ {innerSelections} }} }}";
        }

        return query;
    }

    /// <summary>
    /// Unwraps instance query result: extracts the resource object from the wrapper field
    /// so fields appear at the data root level.
    /// </summary>
    private static IExecutionResult UnwrapInstanceResult(IExecutionResult result, string resourceType)
    {
        if (result is not IOperationResult { Data: { } data } opResult)
            return result;

        if (!data.TryGetValue(resourceType, out var resourceData))
            return result;

        if (resourceData is not IReadOnlyDictionary<string, object?> resourceDict)
            return result;

        return CreateResultWithData(opResult, resourceDict);
    }

    private static IExecutionResult PostProcessDirectives(
        IExecutionResult result, string? query, ILogger logger)
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

            return CreateResultWithData(result.ExpectOperationResult(), dataCopy);
        }
        catch (SingletonDirectiveViolationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GraphQL post-processing failed for query: {Query}", query);
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

    /// <summary>
    /// Creates a new <see cref="IOperationResult"/> by replacing the data dictionary
    /// while preserving all other properties from the original result.
    /// Uses <see cref="OperationResultBuilder"/> to ensure <c>IsDataSet</c> is <c>true</c>.
    /// Constructing <see cref="OperationResult"/> directly via its public constructor
    /// defaults <c>IsDataSet</c> to <c>false</c>, causing the JSON formatter to omit data.
    /// </summary>
    private static IOperationResult CreateResultWithData(
        IOperationResult original,
        IReadOnlyDictionary<string, object?> data)
    {
        return OperationResultBuilder
            .FromResult(original)
            .SetData(data)
            .Build();
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

    /// <summary>
    /// Scans the query for <c>@slice(path: "X")</c> directives and implicitly injects
    /// <c>X</c> into the selection set so the discriminator is available in the execution
    /// result for post-processing.
    /// </summary>
    private static string InjectSlicePathFields(string query)
    {
        if (string.IsNullOrEmpty(query))
            return query;

        var document = Utf8GraphQLParser.Parse(query);
        var modified = RewriteDocument(document);
        return modified.ToString();
    }

    private static DocumentNode RewriteDocument(DocumentNode document)
    {
        var modifiedDefinitions = new List<IDefinitionNode>();
        foreach (var definition in document.Definitions)
        {
            if (definition is OperationDefinitionNode operation)
            {
                var modifiedSelectionSet = RewriteSelectionSet(operation.SelectionSet);
                if (modifiedSelectionSet != operation.SelectionSet)
                {
                    modifiedDefinitions.Add(new OperationDefinitionNode(
                        operation.Location,
                        operation.Name,
                        operation.Operation,
                        operation.VariableDefinitions,
                        operation.Directives,
                        modifiedSelectionSet));
                    continue;
                }
            }

            modifiedDefinitions.Add(definition);
        }

        return new DocumentNode(modifiedDefinitions);
    }

    private static SelectionSetNode RewriteSelectionSet(SelectionSetNode selectionSet)
    {
        var modifiedSelections = new List<ISelectionNode>();
        bool anyModified = false;

        foreach (var selection in selectionSet.Selections)
        {
            if (selection is FieldNode field)
            {
                var modifiedField = RewriteField(field);
                modifiedSelections.Add(modifiedField);
                if (modifiedField != field)
                    anyModified = true;
            }
            else
            {
                modifiedSelections.Add(selection);
            }
        }

        if (!anyModified)
            return selectionSet;

        return new SelectionSetNode(modifiedSelections);
    }

    private static FieldNode RewriteField(FieldNode field)
    {
        // Recurse into nested selections first
        SelectionSetNode? rewrittenSelectionSet = field.SelectionSet is not null
            ? RewriteSelectionSet(field.SelectionSet)
            : null;

        // If this field has @slice(path: "X") and X != "$index", inject X into the selection set
        var slicePath = GetSlicePath(field);
        if (slicePath is not null && slicePath != "$index" && rewrittenSelectionSet is not null)
        {
            var fieldNames = rewrittenSelectionSet.Selections
                .OfType<FieldNode>()
                .Select(f => f.Alias?.Value ?? f.Name.Value)
                .ToHashSet();

            if (!fieldNames.Contains(slicePath))
            {
                var newSelections = rewrittenSelectionSet.Selections.ToList();
                newSelections.Add(new FieldNode(slicePath));
                rewrittenSelectionSet = new SelectionSetNode(newSelections);
            }
        }

        if (rewrittenSelectionSet == field.SelectionSet)
            return field;

        return field.WithSelectionSet(rewrittenSelectionSet);
    }

    private static string? GetSlicePath(FieldNode field)
    {
        var sliceDirective = field.Directives.FirstOrDefault(d => d.Name.Value == "slice");
        if (sliceDirective is null)
            return null;

        var pathArg = sliceDirective.Arguments.FirstOrDefault(a => a.Name.Value == "path");
        return pathArg?.Value is StringValueNode strValue ? strValue.Value : null;
    }
}
