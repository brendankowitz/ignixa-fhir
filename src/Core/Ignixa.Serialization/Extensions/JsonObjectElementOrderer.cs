// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Reorders a FHIR resource <see cref="JsonObject"/> tree in place into StructureDefinition element order.
/// </summary>
/// <remarks>
/// Out-of-order objects are cleared and the same property nodes re-added in rank order, so references
/// held to child nodes (such as a cached <c>meta</c> wrapper) stay attached to the tree.
/// Array element order is never changed.
/// </remarks>
internal static class JsonObjectElementOrderer
{
    /// <returns>True when any object in the tree was reordered.</returns>
    public static bool Order(JsonObject resource, ISchema schema) => OrderObject(resource, type: null, schema);

    private static bool OrderObject(JsonObject obj, IType? type, ISchema schema)
    {
        // Only a resource position (the root, contained, Bundle.entry.resource) is typed by its own resourceType;
        // elsewhere "resourceType" can be an ordinary element, as in ExampleScenario.instance.
        if (ElementOrdering.IsResourcePosition(type)
            && obj[ElementOrdering.ResourceTypeProperty] is JsonValue resourceType
            && resourceType.TryGetValue(out string? typeName))
        {
            type = schema.GetTypeDefinition(typeName) ?? type;
        }

        var properties = new List<(KeyValuePair<string, JsonNode?> Property, ElementPosition Position)>(obj.Count);
        bool isInOrder = true;
        foreach (KeyValuePair<string, JsonNode?> property in obj)
        {
            ElementPosition position = ElementOrdering.Resolve(type, property.Key, schema);
            isInOrder &= properties.Count == 0 || properties[^1].Position.Rank <= position.Rank;
            properties.Add((property, position));
        }

        bool changed = false;
        foreach ((KeyValuePair<string, JsonNode?> property, ElementPosition position) in properties)
        {
            changed |= OrderValue(property.Value, position.Type, schema);
        }

        if (isInOrder)
        {
            return changed;
        }

        // OrderBy is stable, so unknown properties keep their source order.
        List<KeyValuePair<string, JsonNode?>> ordered = properties
            .OrderBy(entry => entry.Position.Rank)
            .Select(entry => entry.Property)
            .ToList();

        obj.Clear();
        foreach (KeyValuePair<string, JsonNode?> property in ordered)
        {
            obj.Add(property);
        }

        return true;
    }

    private static bool OrderValue(JsonNode? node, IType? type, ISchema schema)
    {
        switch (node)
        {
            case JsonObject obj:
                return OrderObject(obj, type, schema);
            case JsonArray array:
                bool changed = false;
                foreach (JsonNode? element in array)
                {
                    changed |= OrderValue(element, type, schema);
                }

                return changed;
            default:
                return false;
        }
    }
}
