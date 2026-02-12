// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Anonymizer.Tools;

/// <summary>
/// Helper for mutating IElement values through the underlying JsonNode tree.
/// IElement is read-only; all mutations go through the mutable JsonNode backing store.
/// </summary>
internal static class ElementMutationTool
{
    /// <summary>
    /// Sets the value of a primitive element by mutating the parent JsonObject or JsonArray.
    /// </summary>
    public static void SetValue(IElement node, object? value)
    {
        var jsonNode = node.Meta<JsonNode>();
        if (jsonNode is null) return;

        JsonNode? newValue = value switch
        {
            null => null,
            JsonNode jn => jn.DeepClone(),
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            decimal d => JsonValue.Create(d),
            double dbl => JsonValue.Create(dbl),
            _ => JsonValue.Create(value.ToString())
        };

        if (jsonNode.Parent is JsonObject parentObj)
        {
            parentObj[node.Name] = newValue;
        }
        else if (jsonNode.Parent is JsonArray parentArr)
        {
            var index = GetArrayIndex(parentArr, jsonNode);
            if (index >= 0)
            {
                parentArr[index] = newValue;
            }
        }
    }

    /// <summary>
    /// Clears the value of a primitive element (sets it to null in JSON).
    /// </summary>
    public static void ClearValue(IElement node)
    {
        var jsonNode = node.Meta<JsonNode>();
        if (jsonNode is null) return;

        if (jsonNode.Parent is JsonObject parentObj)
        {
            parentObj[node.Name] = null;
        }
        else if (jsonNode.Parent is JsonArray parentArr)
        {
            var index = GetArrayIndex(parentArr, jsonNode);
            if (index >= 0)
            {
                parentArr[index] = null;
            }
        }
    }

    /// <summary>
    /// Removes a property entirely from its parent JsonObject.
    /// </summary>
    public static void RemoveProperty(IElement node)
    {
        var jsonNode = node.Meta<JsonNode>();
        if (jsonNode is null) return;

        if (jsonNode.Parent is JsonObject parentObj)
        {
            parentObj.Remove(node.Name);
        }
        else if (jsonNode.Parent is JsonArray parentArr)
        {
            var index = GetArrayIndex(parentArr, jsonNode);
            if (index >= 0)
            {
                parentArr.RemoveAt(index);
            }
        }
    }

    private static int GetArrayIndex(JsonArray arr, JsonNode node)
    {
        for (int i = 0; i < arr.Count; i++)
        {
            if (ReferenceEquals(arr[i], node))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Gets the current string value of a primitive element.
    /// </summary>
    public static string? GetStringValue(IElement node)
    {
        return node.Value?.ToString();
    }
}
