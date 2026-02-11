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
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors.Settings;
using Ignixa.Anonymizer.Utility;

namespace Ignixa.Anonymizer.Processors;

public class SubstituteProcessor : IAnonymizerProcessor
{
    public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));
        EnsureArg.IsNotNull(node, nameof(node));
        EnsureArg.IsNotNull(context?.VisitedNodes, nameof(context));
        EnsureArg.IsNotNull(settings, nameof(settings));

        var substituteSetting = SubstituteSetting.CreateFromRuleSettings(settings);

        if (node.IsPrimitiveElement())
        {
            return SubstitutePrimitive(resource, node, substituteSetting, context);
        }

        return SubstituteComplex(resource, node, substituteSetting, context);
    }

    private static ProcessResult SubstitutePrimitive(ResourceJsonNode resource, IElement node, SubstituteSetting substituteSetting, ProcessContext context)
    {
        var processResult = new ProcessResult();

        if (context.VisitedNodes.Contains(node.Location))
        {
            return processResult;
        }

        if (substituteSetting.ReplaceWith is null)
        {
            ElementMutationHelper.ClearValue(node);
        }
        else
        {
            ElementMutationHelper.SetValue(node, substituteSetting.ReplaceWith);
        }

        processResult.AddProcessRecord(AnonymizationOperations.Substitute, node);
        return processResult;
    }

    private static ProcessResult SubstituteComplex(ResourceJsonNode resource, IElement node, SubstituteSetting substituteSetting, ProcessContext context)
    {
        var processResult = new ProcessResult();

        if (context.VisitedNodes.Contains(node.Location))
        {
            return processResult;
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
            return processResult;
        }

        // Parent can be either JsonObject (named property) or JsonArray (array element)
        if (nodeJson.Parent is not JsonObject && nodeJson.Parent is not JsonArray)
        {
            return processResult;
        }

        // Build set of keep nodes: nodes that were previously visited (modified by earlier rules)
        var keepNodeNames = new HashSet<string>();
        CollectKeepNodeNames(node, context.VisitedNodes, keepNodeNames);

        // Get the current node's JsonObject
        JsonObject? currentObj = nodeJson as JsonObject;
        if (currentObj is null)
        {
            return processResult;
        }

        // Replace children that exist in replacement
        var replacementChildNames = new HashSet<string>();
        foreach (var (key, value) in replacementObj)
        {
            replacementChildNames.Add(key);
            currentObj[key] = value?.DeepClone();
        }

        // Remove children not in replacement, unless they need to be kept
        var keysToRemove = new List<string>();
        foreach (var (key, _) in currentObj)
        {
            if (replacementChildNames.Contains(key))
            {
                continue;
            }

            if (keepNodeNames.Contains(key))
            {
                // Keep the node but clear its value
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
        context.VisitedNodes.Add(node.Location);
        foreach (var d in node.Descendants())
        {
            context.VisitedNodes.Add(d.Location);
        }

        processResult.AddProcessRecord(AnonymizationOperations.Substitute, node);
        return processResult;
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
