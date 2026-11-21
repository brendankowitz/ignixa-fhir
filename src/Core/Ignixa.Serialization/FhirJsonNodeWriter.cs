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
    // Utf8JsonWriter uses this when JsonWriterOptions.MaxDepth is 0.
    private const int DefaultMaxDepth = 1000;

    /// <remarks>
    /// A parsed tree is materialized only where it must be pruned: each object or array is first written
    /// unchanged to a buffer, and those bytes are copied when they contain no null or empty container.
    /// </remarks>
    public static void Write(Utf8JsonWriter writer, JsonNode node, JsonSerializerOptions options)
    {
        if (node is not (JsonObject or JsonArray) || !TryWriteUnchanged(writer, node, options))
        {
            WritePruned(writer, node, options);
        }
    }

    /// <summary>
    /// Writes <paramref name="node"/> as the root of a new pooled buffer, which the caller disposes.
    /// </summary>
    public static PooledByteBufferWriter WriteToBuffer(JsonNode node, JsonWriterOptions writerOptions, JsonSerializerOptions options)
    {
        int maxDepth = writerOptions.MaxDepth == 0 ? DefaultMaxDepth : writerOptions.MaxDepth;
        PooledByteBufferWriter buffer = WriteToNewBuffer(node, writerOptions, options, pruned: false);
        if (node is not (JsonObject or JsonArray) || !ContainsNullOrEmpty(buffer.WrittenSpan, maxDepth))
        {
            return buffer;
        }

        buffer.Dispose();
        return WriteToNewBuffer(node, writerOptions, options, pruned: true);
    }

    private static PooledByteBufferWriter WriteToNewBuffer(JsonNode node, JsonWriterOptions writerOptions, JsonSerializerOptions options, bool pruned)
    {
        PooledByteBufferWriter? buffer = null;
        try
        {
            buffer = new PooledByteBufferWriter();
            using var writer = new Utf8JsonWriter(buffer, writerOptions);
            if (pruned)
            {
                WritePruned(writer, node, options);
            }
            else
            {
                node.WriteTo(writer, options);
            }

            writer.Flush();
            PooledByteBufferWriter written = buffer;
            buffer = null;
            return written;
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    private static void WritePruned(Utf8JsonWriter writer, JsonNode node, JsonSerializerOptions options)
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

    private static bool TryWriteUnchanged(Utf8JsonWriter writer, JsonNode node, JsonSerializerOptions options)
    {
        // Copied bytes keep the scratch writer's indentation, which only matches at the root.
        if (writer.Options.Indented && writer.CurrentDepth > 0)
        {
            return false;
        }

        int maxDepth = Math.Max(1, MaxDepth(writer) - writer.CurrentDepth);
        using PooledByteBufferWriter scratch = WriteToNewBuffer(node, writer.Options with { MaxDepth = maxDepth }, options, pruned: false);
        ReadOnlySpan<byte> json = scratch.WrittenSpan;
        if (ContainsNullOrEmpty(json, maxDepth))
        {
            return false;
        }

        writer.WriteRawValue(json, skipInputValidation: true);
        return true;
    }

    private static bool ContainsNullOrEmpty(ReadOnlySpan<byte> json, int maxDepth)
    {
        // Utf8JsonWriter writes an empty container as {} or [] (also when indented), so bytes without
        // "{}", "[]" or "null" have none; otherwise the tokens decide, since strings may contain them.
        if (json.IndexOf("{}"u8) < 0 && json.IndexOf("[]"u8) < 0 && json.IndexOf("null"u8) < 0)
        {
            return false;
        }

        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = maxDepth + 1 });
        JsonTokenType previous = JsonTokenType.None;
        while (reader.Read())
        {
            JsonTokenType token = reader.TokenType;
            if (token == JsonTokenType.Null
                || (token == JsonTokenType.EndObject && previous == JsonTokenType.StartObject)
                || (token == JsonTokenType.EndArray && previous == JsonTokenType.StartArray))
            {
                return true;
            }

            previous = token;
        }

        return false;
    }

    private static int MaxDepth(Utf8JsonWriter writer) =>
        writer.Options.MaxDepth == 0 ? DefaultMaxDepth : writer.Options.MaxDepth;

    /// <summary>
    /// Whether <paramref name="node"/>, written as the next value of <paramref name="writer"/>, has content.
    /// </summary>
    /// <exception cref="InvalidOperationException">The node nests deeper than the writer's maximum depth.</exception>
    public static bool HasContent(Utf8JsonWriter writer, [NotNullWhen(true)] JsonNode? node) =>
        HasContent(node, depth: writer.CurrentDepth + 1, MaxDepth(writer));

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
        if (HasContent(writer, item))
        {
            Write(writer, item, options);
        }
        else if (companion is not null && index < companion.Count && HasContent(writer, companion[index]))
        {
            writer.WriteNullValue();
        }
    }

    // depth is the writer depth the node would open at, so the check matches the writer's own limit.
    private static bool HasContent(JsonNode? node, int depth, int maxDepth)
    {
        if (node is JsonObject or JsonArray && depth > maxDepth)
        {
            throw new InvalidOperationException(
                $"The JSON tree is deeper than the maximum allowed depth of {maxDepth}.");
        }

        switch (node)
        {
            case null:
                return false;
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    if (HasContent(property.Value, depth + 1, maxDepth))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    if (HasContent(item, depth + 1, maxDepth))
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
            if (!HasContent(writer, value))
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
