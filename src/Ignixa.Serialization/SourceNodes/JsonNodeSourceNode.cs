// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Utilities;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Wraps a JsonNode (JsonObject, JsonArray, or JsonValue) as an ISourceNode.
/// Implements FHIR-specific logic for shadow properties, extensions, and choice types.
/// Ported from JsonElementSourceNode to support JsonNode-based architecture.
/// </summary>
public class JsonNodeSourceNode : ISourceNode, IResourceTypeSupplier, IAnnotated
{
    private const string _resourceType = "resourceType";
    private const char _shadowNodePrefix = '_';
    internal const char ChoiceTypeSuffix = '*';

    private readonly JsonNode? _contentNode;
    private readonly int? _arrayIndex;
    private readonly JsonNode? _valueNode;
    private Dictionary<string, Lazy<IEnumerable<ISourceNode>>>? _cachedNodes;

    protected JsonNodeSourceNode(JsonNode? valueNode, JsonNode? contentNode, string name, int? arrayIndex, string location)
    {
        _valueNode = valueNode;
        _contentNode = contentNode;
        _arrayIndex = arrayIndex;
        Name = name;
        Location = location;
    }

    public string ResourceType => _contentNode is JsonObject obj ? GetResourceTypePropertyFromObject(obj, Name) : null;

    public string Name { get; }

    public string Text
    {
        get
        {
            if (_valueNode is JsonValue jsonValue)
            {
                try
                {
                    // Try to get as string
                    if (jsonValue.TryGetValue(out string stringValue))
                    {
                        return stringValue?.Trim();
                    }

                    // Try to get raw value as string
                    var rawText = jsonValue.ToJsonString();
                    if (!string.IsNullOrWhiteSpace(rawText))
                    {
                        return PrimitiveTypeConverter.ConvertTo<string>(rawText.Trim().Trim('"'));
                    }
                }
                catch
                {
                    // Fallback to JSON representation
                    return _valueNode.ToJsonString().Trim('"');
                }
            }

            // Objects and arrays don't have text
            if (_valueNode is JsonObject or JsonArray)
            {
                return null;
            }

            return null;
        }
    }

    public string Location { get; }

    public IEnumerable<object> Annotations(Type type)
    {
        if (type == GetType() || type == typeof(ISourceNode) || type == typeof(IResourceTypeSupplier))
        {
            return [this];
        }

        // Expose the underlying JsonNode for direct mutation
        if (type == typeof(JsonNode))
        {
            // Return the content node (JsonObject) if available, otherwise the value node
            var node = _contentNode ?? _valueNode;
            if (node != null)
            {
                return [node];
            }
        }

        return [];
    }

    public IEnumerable<ISourceNode> Children(string name = null)
    {
        if (_cachedNodes == null)
        {
            var list = new Dictionary<string, Lazy<IEnumerable<ISourceNode>>>();

            if (_contentNode is JsonObject obj && obj.Count > 0)
            {
                // ProcessObjectProperties handles shadow property pairing, extensions, and choice types
                // JsonObject.Select returns (Key, Value) where Value is nullable
                foreach ((string, Lazy<IEnumerable<ISourceNode>>) item in ProcessObjectProperties(
                    obj.Select(x => (x.Key, x.Value)),
                    Location))
                {
                    list.Add(item.Item1, item.Item2);
                }
            }

            _cachedNodes = list;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return _cachedNodes.SelectMany(x => x.Value.Value);
        }

        // Handle choice type suffix (e.g., "value*" matches "valueString", "valueCode", etc.)
        if (name.EndsWith(ChoiceTypeSuffix))
        {
            string matchPrefix = name.TrimEnd(ChoiceTypeSuffix);
            return _cachedNodes
                .Where(x => x.Key.StartsWith(matchPrefix, StringComparison.Ordinal))
                .SelectMany(x => x.Value.Value)
                .ToArray();
        }

        if (_cachedNodes.TryGetValue(name, out Lazy<IEnumerable<ISourceNode>> exactMatch))
        {
            return exactMatch.Value;
        }

        return [];
    }

    /// <summary>
    /// Creates a new JsonNodeSourceNode from a JsonNode (public factory method for external use).
    /// </summary>
    /// <param name="node">The JsonNode to wrap.</param>
    /// <param name="name">The name of the root node (default: "root").</param>
    /// <returns>An ISourceNode wrapping the JsonNode.</returns>
    public static ISourceNode Create(JsonNode node, string name = "root")
    {
        if (node is JsonObject obj)
        {
            return FromRoot(obj, name);
        }

        return new JsonNodeSourceNode(node, node, name, null, name);
    }

    internal static JsonNodeSourceNode FromRoot(JsonObject rootNode, string name = "")
    {
        string resourceType = GetResourceTypePropertyFromObject(rootNode, name);
        return new JsonNodeSourceNode(null, rootNode, name, null, resourceType);
    }

    /// <summary>
    /// Process object properties with FHIR shadow property pairing.
    /// Groups properties by base name (trimming '_' prefix) and pairs them.
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1414:Tuple types in signatures should have element names", Justification = "Internal method for processing object properties.")]
    internal static List<(string, Lazy<IEnumerable<ISourceNode>>)> ProcessObjectProperties(
        IEnumerable<(string Name, JsonNode? Value)> objectEnumerator,
        string location)
    {
        var list = new List<(string, Lazy<IEnumerable<ISourceNode>>)>();

        // Group by base name (trim '_' prefix) to pair regular properties with shadow properties
        foreach (IGrouping<string, (string Name, JsonNode? Value)> item in objectEnumerator
                     .Where(x => x.Value != null) // Skip null values
                     .GroupBy(x => x.Name.TrimStart(_shadowNodePrefix))
                     .Where(x => !string.Equals(x.Key, _resourceType, StringComparison.OrdinalIgnoreCase)))
        {
            if (item.Count() == 1)
            {
                // Single property (no shadow)
                (string Name, JsonNode? Value) innerItem = item.First();
                (string Name, Lazy<IEnumerable<ISourceNode>>) values = (
                    innerItem.Name,
                    new Lazy<IEnumerable<ISourceNode>>(() => JsonNodeToSourceNodes(innerItem.Name, location, innerItem.Value!).ToList())
                );
                list.Add(values);
            }
            else if (item.Count() == 2)
            {
                // Property with shadow (e.g., birthDate + _birthDate)
                (string Name, JsonNode? Value) innerItem = item.SingleOrDefault(x => !x.Name.StartsWith(_shadowNodePrefix));
                (string Name, JsonNode? Value) shadowItem = item.SingleOrDefault(x => x.Name.StartsWith(_shadowNodePrefix));
                (string Name, Lazy<IEnumerable<ISourceNode>>) values = (
                    innerItem.Name,
                    new Lazy<IEnumerable<ISourceNode>>(() => JsonNodeToSourceNodes(innerItem.Name, location, innerItem.Value!, shadowItem.Value).ToList())
                );
                list.Add(values);
            }
            else
            {
                throw new NotSupportedException($"Expected 1 or 2 nodes with name '{item.Key}', found {item.Count()}");
            }
        }

        return list;
    }

    /// <summary>
    /// Converts JsonNode to ISourceNode instances, handling arrays and shadow property pairing.
    /// </summary>
    private static IEnumerable<ISourceNode> JsonNodeToSourceNodes(
        string name,
        string location,
        JsonNode item,
        JsonNode? shadowItem = null)
    {
        (IReadOnlyList<JsonNode> List, bool ArrayProperty) itemList = ExpandArray(item);
        (IReadOnlyList<JsonNode> List, bool ArrayProperty)? shadowItemList = shadowItem != null
            ? ExpandArray(shadowItem)
            : (Array.Empty<JsonNode>(), false);

        bool isArray = shadowItemList.Value.ArrayProperty || itemList.ArrayProperty;
        int maxCount = Math.Max(itemList.List.Count, shadowItemList.Value.List.Count);

        for (int i = 0; i < maxCount; i++)
        {
            JsonNode? first = ItemAt(itemList.List, i);
            JsonNode? shadow = ItemAt(shadowItemList.Value.List, i);

            JsonNode? content;
            JsonNode? value;

            // Determine which is content (object) and which is value (primitive)
            if (first is JsonObject)
            {
                content = first;
                value = shadow;
            }
            else
            {
                content = shadow;
                value = first;
            }

            string arrayText = isArray ? $"[{i}]" : null;
            string itemLocation = $"{location}.{name}{arrayText}";

            yield return new JsonNodeSourceNode(
                value,
                content,
                name,
                itemList.ArrayProperty ? i : null,
                itemLocation);
        }

        static (IReadOnlyList<JsonNode> List, bool ArrayProperty) ExpandArray(JsonNode prop)
        {
            if (prop == null)
            {
                return ([], false);
            }

            if (prop is JsonArray array)
            {
                // Filter out nulls from the array
                return (array.OfType<JsonNode>().ToList(), true);
            }

            return ([prop], false);
        }

        static JsonNode? ItemAt(IReadOnlyList<JsonNode> list, int i)
        {
            return list?.Count > i ? list[i] : null;
        }
    }

    private static string? GetResourceTypePropertyFromObject(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(_resourceType, out var typeNode))
        {
            return null;
        }

        // Only return resourceType if it's a string and not the special "instance" case
        if (typeNode is JsonValue value && name != "instance")
        {
            if (value.TryGetValue(out string stringValue))
            {
                return stringValue;
            }
        }

        return null;
    }
}
