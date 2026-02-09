using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Processors;
using Ignixa.Anonymizer.Visitors;

namespace Ignixa.Anonymizer.Extensions;

public static class ElementNodeOperationExtensions
{
    public static ResourceJsonNode Anonymize(
        this ResourceJsonNode resource,
        IElement rootElement,
        AnonymizationFhirPathRule[] rules,
        Dictionary<string, IAnonymizerProcessor> processors)
    {
        var visitor = new AnonymizationVisitor(rules, processors);
        rootElement.Accept(resource, visitor);

        RemoveEmptyNodes(resource.MutableNode);
        resource.InvalidateCaches();

        return resource;
    }

    /// <summary>
    /// Removes empty nodes (null value, no children, not a resource) from the mutable JsonObject tree.
    /// </summary>
    public static void RemoveEmptyNodes(JsonObject node)
    {
        if (node is null)
        {
            return;
        }

        var keysToRemove = new List<string>();

        foreach (var (key, value) in node)
        {
            if (key == "resourceType")
            {
                continue;
            }

            if (value is JsonObject childObj)
            {
                RemoveEmptyNodes(childObj);
                if (IsEmptyJsonNode(childObj))
                {
                    keysToRemove.Add(key);
                }
            }
            else if (value is JsonArray arr)
            {
                RemoveEmptyNodesFromArray(arr);
                if (arr.Count == 0)
                {
                    keysToRemove.Add(key);
                }
            }
            else if (value is null)
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            node.Remove(key);
        }
    }

    private static void RemoveEmptyNodesFromArray(JsonArray arr)
    {
        for (int i = arr.Count - 1; i >= 0; i--)
        {
            if (arr[i] is JsonObject childObj)
            {
                RemoveEmptyNodes(childObj);
                if (IsEmptyJsonNode(childObj))
                {
                    arr.RemoveAt(i);
                }
            }
            else if (arr[i] is null)
            {
                arr.RemoveAt(i);
            }
        }
    }

    private static bool IsEmptyJsonNode(JsonObject obj)
    {
        EnsureArg.IsNotNull(obj);
        return obj.Count == 0;
    }
}
