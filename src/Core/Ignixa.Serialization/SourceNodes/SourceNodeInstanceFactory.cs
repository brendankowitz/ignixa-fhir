// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// <see cref="IInstanceFactory"/> backed by Ignixa's native source-node model.
/// Builds a JSON object for the requested type and returns it as a first-class
/// <see cref="SchemaAwareElement"/> — the same node kind the FHIRPath engine
/// navigates elsewhere — so created instances support full navigation and
/// round-trip to JSON. Declines (returns null) for types unknown to the schema.
/// </summary>
public sealed class SourceNodeInstanceFactory(ISchema schema) : IInstanceFactory
{
    private readonly ISchema _schema = schema ?? throw new ArgumentNullException(nameof(schema));

    public IElement? Create(string typeName, string? namespacePrefix, IReadOnlyList<InstanceElement> elements)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(elements);

        // This factory constructs FHIR types; System-namespace primitives are out of scope.
        if (string.Equals(namespacePrefix, "System", StringComparison.Ordinal))
        {
            return null;
        }

        var definition = _schema.GetTypeDefinition(typeName);
        if (definition is null)
        {
            // Host cannot construct an unknown type — engine yields an empty result.
            return null;
        }

        // Per spec, primitive target types carry their value via the special "value"
        // element. Build a primitive value node rather than a complex object so the
        // result behaves like any other primitive (HasPrimitiveValue, scalar Value).
        if (definition.Info.IsPrimitive
            && elements is [{ Name: "value", Values: [var primitiveValue] }]
            && ToJsonNode(primitiveValue) is JsonValue primitiveNode)
        {
            var primitiveSource = JsonNodeSourceNode.Create(primitiveNode, typeName);
            return new SchemaAwareElement(primitiveSource, _schema, definition, typeName);
        }

        var obj = new JsonObject();
        foreach (var element in elements)
        {
            var nodes = element.Values
                .Select(ToJsonNode)
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();

            if (nodes.Count == 0)
            {
                continue;
            }

            obj[element.Name] = nodes.Count == 1 ? nodes[0] : new JsonArray([.. nodes]);
        }

        var source = JsonNodeSourceNode.Create(obj, typeName);
        return new SchemaAwareElement(source, _schema, definition, typeName);
    }

    /// <summary>
    /// Converts an evaluated FHIRPath value element to a JSON node. Source-node-backed
    /// values expose their underlying JSON via <c>Meta&lt;JsonNode&gt;()</c> and are
    /// cloned verbatim; primitive literals fall back to their scalar value.
    /// </summary>
    private static JsonNode? ToJsonNode(IElement value)
    {
        var backing = value.Meta<JsonNode>();
        if (backing is not null)
        {
            return backing.DeepClone();
        }

        if (value.Value is { } scalar)
        {
            return ToJsonValue(scalar);
        }

        // Non-source-backed complex value: rebuild from its children by element name.
        var obj = new JsonObject();
        foreach (var group in value.Children().GroupBy(c => c.Name))
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                continue;
            }

            var nodes = group.Select(ToJsonNode).Where(n => n is not null).Select(n => n!).ToList();
            if (nodes.Count == 1)
            {
                obj[group.Key] = nodes[0];
            }
            else if (nodes.Count > 1)
            {
                obj[group.Key] = new JsonArray([.. nodes]);
            }
        }

        return obj.Count > 0 ? obj : null;
    }

    private static JsonNode ToJsonValue(object value) => value switch
    {
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        decimal d => JsonValue.Create(d),
        double db => JsonValue.Create(db),
        _ => JsonValue.Create(value.ToString())!,
    };
}
