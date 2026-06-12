// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.Directives;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.GraphQl.Execution;

public sealed partial class GraphQlExecutionService(
    IRequestExecutorResolver executorResolver,
    ILogger<GraphQlExecutionService> logger)
    : IGraphQlExecutionService
{
    private static readonly Regex ResourceTypePattern = BuildResourceTypePattern();
    private static readonly Regex FhirIdPattern = BuildFhirIdPattern();

    public Task<IExecutionResult> ExecuteAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(request, version, null, null, cancellationToken);

    public Task<IExecutionResult> ExecuteInstanceAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        string? resourceType,
        string? resourceId,
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

        var effectiveQuery = request.Query ?? string.Empty;
        if (string.IsNullOrWhiteSpace(effectiveQuery))
            return GraphQlError("The GraphQL query must not be empty.", "FHIR_SYNTAX_ERROR");

        var isInstanceQuery = resourceType is not null && resourceId is not null;
        if (isInstanceQuery)
        {
            var wrapResult = WrapInstanceQuery(effectiveQuery, resourceType!, resourceId!, executor);
            if (wrapResult.Error is not null)
                return wrapResult.Error;

            effectiveQuery = wrapResult.Query!;
        }

        // Parsing raw user input can throw SyntaxException on malformed queries;
        // surface it as a GraphQL error rather than letting it become an HTTP 500.
        DocumentNode document;
        try
        {
            document = Utf8GraphQLParser.Parse(effectiveQuery);
        }
        catch (SyntaxException ex)
        {
            return SyntaxErrorResult(ex);
        }

        var executedOperationName = request.OperationName;
        document = InjectSlicePathFields(document, executedOperationName);
        effectiveQuery = document.ToString();

        var builder = OperationRequestBuilder.New()
            .SetDocument(effectiveQuery);

        if (executedOperationName is not null)
            builder.SetOperationName(executedOperationName);

        if (request.Variables.HasValue)
            builder.SetVariableValues(DeserializeVariables(request.Variables.Value));

        if (resourceType is not null)
        {
            builder.AddGlobalState("InstanceResourceType", resourceType);
            builder.AddGlobalState("InstanceResourceId", (object?)resourceId);
        }

        var result = await executor.ExecuteAsync(builder.Build(), cancellationToken);

        result = PostProcessDirectives(result, document, executedOperationName, logger);

        if (isInstanceQuery)
        {
            result = UnwrapInstanceResult(result, resourceType!);
        }

        return result;
    }

    private static (string? Query, IExecutionResult? Error) WrapInstanceQuery(
        string query,
        string resourceType,
        string resourceId,
        IRequestExecutor executor)
    {
        if (!ResourceTypePattern.IsMatch(resourceType)
            || !executor.Schema.Types.Any(t => string.Equals(t.Name, resourceType, StringComparison.Ordinal)))
        {
            return (null, GraphQlError(
                $"Unknown resource type '{resourceType}'.",
                "FHIR_UNKNOWN_RESOURCE_TYPE"));
        }

        if (!FhirIdPattern.IsMatch(resourceId))
        {
            return (null, GraphQlError(
                $"Invalid resource id '{resourceId}'. Must be 1-64 characters of [A-Za-z0-9-.].",
                "FHIR_INVALID_ID"));
        }

        var trimmed = query.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return (null, GraphQlError(
                "Instance-level $graphql requires a bare selection set per the FHIR GraphQL spec (e.g., { id name { family } }).",
                "FHIR_INVALID_INSTANCE_QUERY"));
        }

        var innerSelections = trimmed[1..^1].Trim();
        var literalId = new StringValueNode(resourceId).ToString();
        return ($"{{ {resourceType}(_id: {literalId}) {{ {innerSelections} }} }}", null);
    }

    private static IExecutionResult UnwrapInstanceResult(IExecutionResult result, string resourceType)
    {
        if (result is not IOperationResult opResult)
            return result;

        // Missing instance: the wrapped field resolves to null. Returning the wrapped
        // shape { data: { Patient: null } } hides the not-found condition, so unwrap to
        // data: null and attach a FHIR_NOT_FOUND error. Skip when the executor already
        // produced errors so we don't mask the real cause with a spurious not-found.
        if (opResult.Data is not { } data
            || !data.TryGetValue(resourceType, out var resourceData)
            || resourceData is null)
        {
            if (opResult.Errors is { Count: > 0 })
                return result;

            return OperationResultBuilder.FromResult(opResult)
                .SetData(null)
                .AddError(BuildError(
                    $"Resource {resourceType} not found.",
                    "FHIR_NOT_FOUND"))
                .Build();
        }

        if (resourceData is not IReadOnlyDictionary<string, object?> resourceDict)
            return result;

        return CreateResultWithData(opResult, resourceDict);
    }

    private static IExecutionResult PostProcessDirectives(
        IExecutionResult result, DocumentNode document, string? operationName, ILogger logger)
    {
        if (result is not IOperationResult { Data: { } readOnlyData } opResult)
            return result;

        var operation = SelectOperation(document, operationName);
        if (operation?.SelectionSet is null || !HasAnyDirectives(operation.SelectionSet))
            return result;

        try
        {
            if (readOnlyData is IDictionary<string, object?> mutableData)
            {
                FlattenResultProcessor.Process(document, operationName, mutableData);
                return result;
            }

            var dataCopy = FlattenResultProcessor.DeepCopyData(readOnlyData);
            FlattenResultProcessor.Process(document, operationName, dataCopy);

            return CreateResultWithData(opResult, dataCopy);
        }
        catch (SingletonDirectiveViolationException ex)
        {
            return OperationResultBuilder.FromResult(opResult)
                .SetData(null)
                .AddError(BuildError(ex.Message, "FHIR_SINGLETON_VIOLATION"))
                .Build();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GraphQL post-processing failed");
            return OperationResultBuilder.FromResult(opResult)
                .AddError(BuildError(
                    "GraphQL result post-processing failed; directive transformations were not applied.",
                    "FHIR_POST_PROCESSING_FAILED"))
                .Build();
        }
    }

    internal static OperationDefinitionNode? SelectOperation(DocumentNode document, string? operationName)
    {
        var operations = document.Definitions.OfType<OperationDefinitionNode>();
        return operationName is not null
            ? operations.FirstOrDefault(op => op.Name?.Value == operationName)
            : operations.FirstOrDefault();
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

    private static IExecutionResult SyntaxErrorResult(SyntaxException ex)
    {
        var error = ErrorBuilder.New()
            .SetMessage(ex.Message)
            .SetCode("FHIR_SYNTAX_ERROR")
            .AddLocation(ex.Line, ex.Column)
            .Build();

        return OperationResultBuilder.New().AddError(error).Build();
    }

    private static IExecutionResult GraphQlError(string message, string code)
        => OperationResultBuilder.New().AddError(BuildError(message, code)).Build();

    private static IError BuildError(string message, string code)
        => ErrorBuilder.New().SetMessage(message).SetCode(code).Build();

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
    /// Scans the executed operation for <c>@slice(path: "X")</c> directives and implicitly
    /// injects each segment of <c>X</c> into the selection set so the discriminator is
    /// available in the execution result for post-processing.
    /// </summary>
    private static DocumentNode InjectSlicePathFields(DocumentNode document, string? operationName)
    {
        var executed = SelectOperation(document, operationName);
        if (executed is null)
            return document;

        var modifiedDefinitions = new List<IDefinitionNode>(document.Definitions.Count);
        foreach (var definition in document.Definitions)
        {
            if (ReferenceEquals(definition, executed))
            {
                var modifiedSelectionSet = RewriteSelectionSet(executed.SelectionSet);
                if (modifiedSelectionSet != executed.SelectionSet)
                {
                    modifiedDefinitions.Add(new OperationDefinitionNode(
                        executed.Location,
                        executed.Name,
                        executed.Operation,
                        executed.VariableDefinitions,
                        executed.Directives,
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
        var anyModified = false;

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
        var rewrittenSelectionSet = field.SelectionSet is not null
            ? RewriteSelectionSet(field.SelectionSet)
            : null;

        var slicePath = GetSlicePath(field);
        if (slicePath is not null && slicePath != "$index" && rewrittenSelectionSet is not null)
        {
            rewrittenSelectionSet = InjectSliceSegment(rewrittenSelectionSet, slicePath);
        }

        if (rewrittenSelectionSet == field.SelectionSet)
            return field;

        return field.WithSelectionSet(rewrittenSelectionSet);
    }

    /// <summary>
    /// Ensures every segment of a dotted slice path (e.g. <c>name.family</c>) is present in
    /// the selection set so the discriminator value is available after execution.
    /// </summary>
    private static SelectionSetNode InjectSliceSegment(SelectionSetNode selectionSet, string slicePath)
    {
        var separatorIndex = slicePath.IndexOf('.', StringComparison.Ordinal);
        var head = separatorIndex < 0 ? slicePath : slicePath[..separatorIndex];

        var existing = selectionSet.Selections
            .OfType<FieldNode>()
            .FirstOrDefault(f => (f.Alias?.Value ?? f.Name.Value) == head);

        if (separatorIndex < 0)
        {
            if (existing is not null)
                return selectionSet;

            var added = selectionSet.Selections.ToList();
            added.Add(new FieldNode(head));
            return new SelectionSetNode(added);
        }

        var tail = slicePath[(separatorIndex + 1)..];
        if (existing is null)
        {
            var childSet = InjectSliceSegment(new SelectionSetNode([]), tail);
            var added = selectionSet.Selections.ToList();
            added.Add(new FieldNode(
                null, new NameNode(head), null, [], [], childSet));
            return new SelectionSetNode(added);
        }

        var existingChildSet = existing.SelectionSet ?? new SelectionSetNode([]);
        var rewrittenChild = InjectSliceSegment(existingChildSet, tail);
        if (rewrittenChild == existing.SelectionSet)
            return selectionSet;

        var replaced = selectionSet.Selections
            .Select(s => ReferenceEquals(s, existing) ? existing.WithSelectionSet(rewrittenChild) : s)
            .ToList();
        return new SelectionSetNode(replaced);
    }

    private static string? GetSlicePath(FieldNode field)
    {
        var sliceDirective = field.Directives.FirstOrDefault(d => d.Name.Value == "slice");
        if (sliceDirective is null)
            return null;

        var pathArg = sliceDirective.Arguments.FirstOrDefault(a => a.Name.Value == "path");
        return pathArg?.Value is StringValueNode strValue ? strValue.Value : null;
    }

    [GeneratedRegex("^[A-Z][A-Za-z]+$")]
    private static partial Regex BuildResourceTypePattern();

    [GeneratedRegex("^[A-Za-z0-9\\-\\.]{1,64}$")]
    private static partial Regex BuildFhirIdPattern();
}
