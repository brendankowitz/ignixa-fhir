// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.Serialization;

/// <summary>
/// Writes a FHIR JSON tree without the null properties and empty objects or arrays that FHIR JSON
/// forbids (https://hl7.org/fhir/json.html, invariant ele-1). The tree itself is not modified.
/// </summary>
/// <remarks>
/// A container whose members are all empty is itself empty, so emptiness propagates to parents.
/// Array items without content are dropped, except in a repeating primitive and its <c>_</c> companion:
/// there an empty item is written as a <c>null</c> placeholder when the other array has content at
/// the same position, keeping the two arrays aligned.
/// </remarks>
internal static class FhirJsonNodeWriter
{
    // Utf8JsonWriter's default MaxDepth. Deeper probing stops so the writer, not a stack overflow, rejects the tree.
    private const int MaxProbeDepth = 1000;

    public static void Write(Utf8JsonWriter writer, JsonNode node, JsonSerializerOptions options)
    {
        switch (node)
        {
            case JsonObject obj:
                WriteObject(writer, obj, options);
                break;
            case JsonArray array:
                WriteArray(writer, array, companion: null, options);
                break;
            default:
                node.WriteTo(writer, options);
                break;
        }
    }

    public static bool HasContent([NotNullWhen(true)] JsonNode? node) => HasContent(node, depth: 0);

    /// <summary>
    /// Gets the array paired with the property <paramref name="name"/>: <c>_x</c> for <c>x</c>, and <c>x</c> for <c>_x</c>.
    /// </summary>
    public static JsonArray? CompanionOf(JsonObject parent, string name) =>
        parent.TryGetPropertyValue(name.StartsWith('_') ? name[1..] : "_" + name, out JsonNode? companion)
            ? companion as JsonArray
            : null;

    public static void WriteArrayItem(Utf8JsonWriter writer, JsonArray array, int index, JsonArray? companion, JsonSerializerOptions options)
    {
        JsonNode? item = array[index];
        if (HasContent(item))
        {
            Write(writer, item, options);
        }
        else if (companion is not null && index < companion.Count && HasContent(companion[index]))
        {
            writer.WriteNullValue();
        }
    }

    private static bool HasContent(JsonNode? node, int depth)
    {
        if (depth > MaxProbeDepth)
        {
            return true;
        }

        switch (node)
        {
            case null:
                return false;
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    if (HasContent(property.Value, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    if (HasContent(item, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return true;
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonObject obj, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((string name, JsonNode? value) in obj)
        {
            if (!HasContent(value))
            {
                continue;
            }

            writer.WritePropertyName(name);
            if (value is JsonArray array)
            {
                WriteArray(writer, array, CompanionOf(obj, name), options);
            }
            else
            {
                Write(writer, value, options);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, JsonArray array, JsonArray? companion, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        for (int i = 0; i < array.Count; i++)
        {
            WriteArrayItem(writer, array, i, companion, options);
        }

        writer.WriteEndArray();
    }
}
