// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json;
using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.DeId.Exceptions;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.DeId.Configuration.ProcessorSettings;
using Ignixa.DeId.Tools;

namespace Ignixa.DeId.Processors;

/// <summary>
/// Processor that replaces element values with a configured substitute, supporting both primitive and complex types.
/// </summary>
public class SubstituteProcessor : IDeIdProcessor
{
    public ValueTask<Result<ProcessorResult>> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessorContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(node, nameof(node));
            EnsureArg.IsNotNull(context.VisitedNodes, nameof(context.VisitedNodes));
            EnsureArg.IsNotNull(context.Settings, nameof(context.Settings));

            var settings = context.Settings.ToDictionary(kv => kv.Key, kv => kv.Value);
            var substituteSetting = SubstituteSetting.CreateFromRuleSettings(settings);

            var wasModified = node.IsPrimitiveElement()
                ? SubstitutePrimitive(resource, node, substituteSetting, context.VisitedNodes)
                : SubstituteComplex(resource, node, substituteSetting, context.VisitedNodes);

            var newResult = new ProcessorResult
            {
                WasModified = wasModified,
                OperationType = DeIdOperations.Substitute,
                ProcessedPaths = wasModified ? [node.Location ?? string.Empty] : []
            };

            return ValueTask.FromResult(Result<ProcessorResult>.Success(newResult));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<ProcessorResult>.Failure(new DeIdError(
                "PROCESSOR_ERROR",
                $"Failed to process node: {ex.Message}",
                Exception: ex,
                Path: node.Location)));
        }
    }

    private static bool SubstitutePrimitive(ResourceJsonNode resource, IElement node, SubstituteSetting substituteSetting, HashSet<string> visitedNodes)
    {
        if (visitedNodes.Contains(node.Location))
        {
            return false;
        }

        if (substituteSetting.ReplaceWith is null)
        {
            ElementMutationTool.ClearValue(node);
        }
        else
        {
            ElementMutationTool.SetValue(node, substituteSetting.ReplaceWith);
        }

        return true;
    }

    private static bool SubstituteComplex(ResourceJsonNode resource, IElement node, SubstituteSetting substituteSetting, HashSet<string> visitedNodes)
    {
        if (visitedNodes.Contains(node.Location))
        {
            return false;
        }

        var replaceWith = substituteSetting.ReplaceWith ?? "{}";
        JsonNode? replacementJson;
        try
        {
            replacementJson = JsonNode.Parse(replaceWith);
        }
        catch (JsonException)
        {
            throw new ProcessingException($"Invalid replacement JSON at path {node.GetFhirPath()}.");
        }

        if (replacementJson is not JsonObject replacementObj)
        {
            throw new ProcessingException($"Replacement value must be a JSON object for complex types at path {node.GetFhirPath()}.");
        }

        var nodeJson = node.Meta<JsonNode>();
        if (nodeJson is null)
        {
            return false;
        }

        if (nodeJson.Parent is not JsonObject && nodeJson.Parent is not JsonArray)
        {
            return false;
        }

        var keepNodeNames = new HashSet<string>();
        CollectKeepNodeNames(node, visitedNodes, keepNodeNames);

        JsonObject? currentObj = nodeJson as JsonObject;
        if (currentObj is null)
        {
            return false;
        }

        var replacementChildNames = new HashSet<string>();
        foreach (var (key, value) in replacementObj)
        {
            replacementChildNames.Add(key);
            currentObj[key] = value?.DeepClone();
        }

        var keysToRemove = new List<string>();
        foreach (var (key, _) in currentObj)
        {
            if (replacementChildNames.Contains(key))
            {
                continue;
            }

            if (keepNodeNames.Contains(key))
            {
                if (currentObj[key] is JsonValue)
                {
                    currentObj[key] = null;
                }
            }
            else
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            currentObj.Remove(key);
        }

        resource.InvalidateCaches();
        visitedNodes.Add(node.Location);
        foreach (var d in node.Descendants())
        {
            visitedNodes.Add(d.Location);
        }

        return true;
    }

    private static bool CollectKeepNodeNames(IElement node, HashSet<string> visitedNodes, HashSet<string> keepNames)
    {
        var shouldKeep = false;

        foreach (var child in node.Children())
        {
            if (CollectKeepNodeNames(child, visitedNodes, keepNames))
            {
                shouldKeep = true;
                keepNames.Add(child.Name);
            }
        }

        if (shouldKeep || visitedNodes.Contains(node.Location))
        {
            return true;
        }

        return false;
    }
}
