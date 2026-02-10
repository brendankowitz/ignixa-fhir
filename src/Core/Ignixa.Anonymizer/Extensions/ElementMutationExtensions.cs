// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Anonymizer.Extensions;

/// <summary>
/// Extension methods for mutating IElement values through the underlying JsonNode tree.
/// IElement is read-only; all mutations go through the mutable JsonNode backing store.
/// </summary>
public static class ElementMutationExtensions
{
    /// <summary>
    /// Sets the value of a primitive element by modifying the underlying JSON.
    /// </summary>
    public static void SetValue(this IElement element, object? value)
    {
        var jsonNode = element.Meta<JsonNode>();
        if (jsonNode == null) return;

        if (jsonNode.Parent is JsonObject parentObj)
        {
            if (value == null)
            {
                parentObj[element.Name] = null;
            }
            else
            {
                parentObj[element.Name] = CreateJsonValue(value);
            }
        }
        else if (jsonNode.Parent is JsonArray parentArray)
        {
            var index = FindIndex(parentArray, jsonNode);
            if (index >= 0)
            {
                if (value == null)
                {
                    parentArray[index] = null;
                }
                else
                {
                    parentArray[index] = CreateJsonValue(value);
                }
            }
        }
    }

    /// <summary>
    /// Removes this element from its parent JSON structure.
    /// </summary>
    public static void RemoveFromParent(this IElement element)
    {
        var jsonNode = element.Meta<JsonNode>();
        if (jsonNode?.Parent is JsonObject parentObj)
        {
            parentObj.Remove(element.Name);
        }
    }

    /// <summary>
    /// Gets the value of a primitive element as string.
    /// </summary>
    public static string? GetValueString(this IElement element)
    {
        return element.Value?.ToString();
    }

    private static JsonNode CreateJsonValue(object value)
    {
        return value switch
        {
            JsonNode jn => jn.DeepClone(),
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            decimal d => JsonValue.Create(d),
            double dbl => JsonValue.Create(dbl),
            float f => JsonValue.Create(f),
            _ => JsonValue.Create(value.ToString()!)
        };
    }

    private static int FindIndex(JsonArray array, JsonNode target)
    {
        for (int i = 0; i < array.Count; i++)
        {
            if (ReferenceEquals(array[i], target))
            {
                return i;
            }
        }
        return -1;
    }
}
