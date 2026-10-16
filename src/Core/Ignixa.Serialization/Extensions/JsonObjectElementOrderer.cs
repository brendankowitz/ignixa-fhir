// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Reorders a FHIR resource <see cref="JsonObject"/> tree in place into StructureDefinition element order.
/// </summary>
/// <remarks>
/// Out-of-order objects are cleared and the same property nodes re-added in rank order, so references
/// held to child nodes (such as a cached <c>meta</c> wrapper) stay attached to the tree.
/// Array element order is never changed. Objects already in order are not modified or copied.
/// </remarks>
internal static class JsonObjectElementOrderer
{
    /// <returns>True when any object in the tree was reordered.</returns>
    public static bool Order(JsonObject resource, ISchema schema) =>
        OrderObject(resource, type: null, ElementOrdering.For(schema), schema);

    private static bool OrderObject(JsonObject obj, IType? type, ElementOrdering ordering, ISchema schema)
    {
        // Only a resource position (the root, contained, Bundle.entry.resource) is typed by its own resourceType;
        // elsewhere "resourceType" can be an ordinary element, as in ExampleScenario.instance.
        if (ElementOrdering.IsResourcePosition(type)
            && obj.TryGetPropertyValue(ElementOrdering.ResourceTypeProperty, out JsonNode? resourceType)
            && resourceType is JsonValue value
            && value.TryGetValue(out string? typeName))
        {
            type = schema.GetTypeDefinition(typeName) ?? type;
        }

        bool isResource = ElementOrdering.IsResourcePosition(type);
        FrozenDictionary<string, ElementPosition>? positions = type is null ? null : ordering.ForParent(type);

        bool changed = false;
        bool isInOrder = true;
        int previousRank = int.MinValue;
        for (int i = 0; i < obj.Count; i++)
        {
            KeyValuePair<string, JsonNode?> property = obj.GetAt(i);
            ElementPosition position = Resolve(positions, isResource, property.Key);
            isInOrder &= previousRank <= position.Rank;
            previousRank = position.Rank;
            changed |= OrderValue(property.Value, position.Type, ordering, schema);
        }

        if (isInOrder)
        {
            return changed;
        }

        Reorder(obj, positions, isResource);
        return true;
    }

    private static ElementPosition Resolve(FrozenDictionary<string, ElementPosition>? positions, bool isResource, string name)
    {
        if (isResource && name == ElementOrdering.ResourceTypeProperty)
        {
            return ElementOrdering.ResourceType;
        }

        return positions is not null && positions.TryGetValue(name, out ElementPosition position)
            ? position
            : ElementOrdering.Unknown;
    }

    private static void Reorder(JsonObject obj, FrozenDictionary<string, ElementPosition>? positions, bool isResource)
    {
        var entries = new (int Rank, KeyValuePair<string, JsonNode?> Property)[obj.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            KeyValuePair<string, JsonNode?> property = obj.GetAt(i);
            entries[i] = (Resolve(positions, isResource, property.Key).Rank, property);
        }

        // Insertion sort: stable, so unknown properties keep their source order, and fastest for the
        // handful of properties an object has.
        for (int i = 1; i < entries.Length; i++)
        {
            (int Rank, KeyValuePair<string, JsonNode?> Property) entry = entries[i];
            int j = i - 1;
            while (j >= 0 && entries[j].Rank > entry.Rank)
            {
                entries[j + 1] = entries[j];
                j--;
            }

            entries[j + 1] = entry;
        }

        obj.Clear();
        foreach ((_, KeyValuePair<string, JsonNode?> property) in entries)
        {
            obj.Add(property);
        }
    }

    private static bool OrderValue(JsonNode? node, IType? type, ElementOrdering ordering, ISchema schema)
    {
        switch (node)
        {
            case JsonObject obj:
                return OrderObject(obj, type, ordering, schema);
            case JsonArray array:
                bool changed = false;
                for (int i = 0; i < array.Count; i++)
                {
                    changed |= OrderValue(array[i], type, ordering, schema);
                }

                return changed;
            default:
                return false;
        }
    }
}
