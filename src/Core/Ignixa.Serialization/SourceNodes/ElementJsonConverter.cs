// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Converts evaluated FHIRPath value elements to JSON. Shared so the
/// FHIRPath-primitive -> JSON contract lives in one place, consumed by
/// instance-selector construction (<see cref="SourceNodeInstanceFactory"/>) and
/// path-based mutation (FHIR Mapping Language's JsonNodeMutator).
/// </summary>
/// <remarks>
/// The primitive conversion (<see cref="ToJsonValue"/>) is the shared contract.
/// <see cref="ToJsonNode"/> additionally clones source-backed values verbatim and
/// rebuilds other complex values by element name; callers that need schema-driven
/// cardinality (array wrapping via <c>Type.IsCollection</c>) keep their own
/// complex-element walk and reuse only <see cref="ToJsonValue"/> for leaves.
/// </remarks>
public static class ElementJsonConverter
{
    /// <summary>
    /// Converts an evaluated value element to a JSON node. Source-node-backed values
    /// are cloned from their underlying JSON; primitive literals use their scalar
    /// value; other complex values are rebuilt from their children by element name.
    /// </summary>
    public static JsonNode? ToJsonNode(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var backing = element.Meta<JsonNode>();
        if (backing is not null)
        {
            return backing.DeepClone();
        }

        if (element.Value is { } scalar)
        {
            return ToJsonValue(scalar);
        }

        var obj = new JsonObject();
        foreach (var group in element.Children().GroupBy(c => c.Name))
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

    /// <summary>Converts a scalar FHIRPath primitive value to a JSON value node.</summary>
    public static JsonNode? ToJsonValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            string s => JsonValue.Create(s),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            bool b => JsonValue.Create(b),
            decimal d => JsonValue.Create(d),
            double db => JsonValue.Create(db),
            float f => JsonValue.Create(f),
            DateTime dt => JsonValue.Create(dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK")),
            DateTimeOffset dto => JsonValue.Create(dto.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK")),
            _ => JsonNode.Parse(JsonSerializer.Serialize(value)),
        };
    }
}
