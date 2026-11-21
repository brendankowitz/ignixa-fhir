// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.Serialization;

/// <summary>
/// Writes a FHIR JSON tree without the null properties and empty objects or arrays that FHIR JSON
/// forbids (https://hl7.org/fhir/json.html, invariant ele-1). The tree itself is not modified.
/// </summary>
/// <remarks>
/// <para>
/// A container whose members are all empty is itself empty, so emptiness propagates to parents.
/// Array items without content are dropped, except in a repeating primitive and its <c>_</c> companion:
/// there an empty item is written as a <c>null</c> placeholder when the other array has content at
/// the same position, keeping the two arrays aligned.
/// </para>
/// <para>
/// A node is serialized once, unchanged, so a lazily parsed tree is never materialized. When those bytes
/// contain a null or an empty container, one reader pass records each container's content, and pruning
/// copies clean branches by byte range.
/// </para>
/// </remarks>
internal static class FhirJsonNodeWriter
{
    // Utf8JsonWriter uses this when JsonWriterOptions.MaxDepth is 0.
    private const int DefaultMaxDepth = 1000;

    /// <summary>
    /// Writes <paramref name="node"/> as the root of a new pooled buffer, which the caller disposes.
    /// </summary>
    public static PooledByteBufferWriter WriteToBuffer(JsonNode node, JsonWriterOptions writerOptions, JsonSerializerOptions options)
    {
        int maxDepth = MaxDepth(writerOptions);
        PooledByteBufferWriter json = WriteCompact(node, writerOptions with { Indented = false }, maxDepth, options);
        if (!writerOptions.Indented)
        {
            return json;
        }

        using (json)
        {
            PooledByteBufferWriter? indented = null;
            try
            {
                indented = new PooledByteBufferWriter();
                using (var writer = new Utf8JsonWriter(indented, writerOptions))
                {
                    WriteIndented(writer, json.WrittenMemory, maxDepth);
                }

                PooledByteBufferWriter written = indented;
                indented = null;
                return written;
            }
            finally
            {
                indented?.Dispose();
            }
        }
    }

    public static void Write(Utf8JsonWriter writer, JsonNode node, JsonSerializerOptions options)
    {
        if (node is not (JsonObject or JsonArray))
        {
            node.WriteTo(writer, options);
            return;
        }

        // The copied bytes are not depth-checked by the writer, so the remaining budget is enforced here.
        int maxDepth = MaxDepth(writer.Options) - writer.CurrentDepth;
        if (maxDepth <= 0)
        {
            throw new InvalidOperationException(
                $"CurrentDepth ({writer.CurrentDepth}) is equal to or larger than the maximum allowed depth of {MaxDepth(writer.Options)}.");
        }

        using PooledByteBufferWriter json = WriteCompact(node, writer.Options with { Indented = false, MaxDepth = maxDepth }, maxDepth, options);
        if (writer.Options.Indented)
        {
            WriteIndented(writer, json.WrittenMemory, maxDepth);
        }
        else
        {
            writer.WriteRawValue(json.WrittenSpan, skipInputValidation: true);
        }
    }

    /// <summary>
    /// Whether <paramref name="node"/>, written as the next value of <paramref name="writer"/>, has content.
    /// </summary>
    /// <exception cref="InvalidOperationException">The node nests deeper than the writer's maximum depth.</exception>
    public static bool HasContent(Utf8JsonWriter writer, [NotNullWhen(true)] JsonNode? node) =>
        HasContent(node, depth: writer.CurrentDepth + 1, MaxDepth(writer.Options));

    /// <summary>
    /// Gets the array paired with the property <paramref name="name"/>: <c>_x</c> for <c>x</c>, and <c>x</c> for <c>_x</c>.
    /// </summary>
    public static JsonArray? CompanionOf(JsonObject parent, string name) =>
        parent.TryGetPropertyValue(CompanionName(name), out JsonNode? companion) ? companion as JsonArray : null;

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

    private static string CompanionName(string name) => name.StartsWith('_') ? name[1..] : "_" + name;

    private static int MaxDepth(JsonWriterOptions options) => options.MaxDepth == 0 ? DefaultMaxDepth : options.MaxDepth;

    /// <summary>Writes <paramref name="node"/> compactly, pruning only when its bytes contain a null or an empty container.</summary>
    private static PooledByteBufferWriter WriteCompact(JsonNode node, JsonWriterOptions compactOptions, int maxDepth, JsonSerializerOptions options)
    {
        PooledByteBufferWriter json = WriteToNewBuffer(compactOptions, writer => node.WriteTo(writer, options));
        ReadOnlySpan<byte> bytes = json.WrittenSpan;

        // Utf8JsonWriter writes an empty container as {} or [], so bytes without "{}", "[]" or "null"
        // need no pruning. Strings may contain them, so a hit is only a maybe.
        if (node is not (JsonObject or JsonArray)
            || (bytes.IndexOf("{}"u8) < 0 && bytes.IndexOf("[]"u8) < 0 && bytes.IndexOf("null"u8) < 0))
        {
            return json;
        }

        using (json)
        {
            ReadOnlyMemory<byte> source = json.WrittenMemory;
            var pruner = new JsonPruner(Analyze(source.Span, maxDepth));
            return WriteToNewBuffer(compactOptions, writer => pruner.WriteValue(writer, source.Span, index: 0));
        }
    }

    // Copied bytes are never indented, and WriteRawValue does not indent them, so indented output is
    // rewritten from the compact bytes; the writer's encoder escapes strings exactly as before.
    private static void WriteIndented(Utf8JsonWriter writer, ReadOnlyMemory<byte> compact, int maxDepth)
    {
        using JsonDocument document = JsonDocument.Parse(compact, new JsonDocumentOptions { MaxDepth = maxDepth + 1 });
        document.RootElement.WriteTo(writer);
    }

    private static PooledByteBufferWriter WriteToNewBuffer(JsonWriterOptions writerOptions, Action<Utf8JsonWriter> write)
    {
        PooledByteBufferWriter? buffer = null;
        try
        {
            buffer = new PooledByteBufferWriter();
            using (var writer = new Utf8JsonWriter(buffer, writerOptions))
            {
                write(writer);
            }

            PooledByteBufferWriter written = buffer;
            buffer = null;
            return written;
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    /// <summary>
    /// Records every value of <paramref name="json"/> in document order, in one reader pass. A value's
    /// children follow it, and <see cref="Value.Next"/> skips its subtree.
    /// </summary>
    private static List<Value> Analyze(ReadOnlySpan<byte> json, int maxDepth)
    {
        var values = new List<Value>();
        var open = new Stack<int>();
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = maxDepth + 1 });
        JsonTokenType previous = JsonTokenType.None;
        int nameStart = -1;
        int nameLength = 0;
        string? escapedName = null;
        while (reader.Read())
        {
            JsonTokenType token = reader.TokenType;
            switch (token)
            {
                case JsonTokenType.PropertyName:
                    nameStart = (int)reader.TokenStartIndex + 1;
                    nameLength = reader.ValueSpan.Length;
                    escapedName = reader.ValueIsEscaped ? reader.GetString() : null;
                    break;
                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    open.Push(values.Count);
                    values.Add(new Value((int)reader.TokenStartIndex, End: 0, Next: 0, token, nameStart, nameLength, escapedName, HasContent: false, IsClean: true));
                    nameStart = -1;
                    break;
                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    int index = open.Pop();
                    bool isEmpty = previous is JsonTokenType.StartObject or JsonTokenType.StartArray;
                    values[index] = values[index] with
                    {
                        End = (int)reader.BytesConsumed,
                        Next = values.Count,
                        IsClean = values[index].IsClean && !isEmpty,
                    };
                    AddToParent(values, open, values[index]);
                    break;
                default:
                    bool isValue = token != JsonTokenType.Null;
                    values.Add(new Value((int)reader.TokenStartIndex, (int)reader.BytesConsumed, values.Count + 1, token, nameStart, nameLength, escapedName, isValue, isValue));
                    nameStart = -1;
                    AddToParent(values, open, values[^1]);
                    break;
            }

            previous = token;
        }

        return values;
    }

    private static void AddToParent(List<Value> values, Stack<int> open, Value child)
    {
        if (open.TryPeek(out int parent))
        {
            values[parent] = values[parent] with
            {
                HasContent = values[parent].HasContent || child.HasContent,
                IsClean = values[parent].IsClean && child.IsClean,
            };
        }
    }

    /// <summary>
    /// A value in the serialized bytes, with its property name when it is an object member. Clean means
    /// it contains no null and no empty container, so its bytes are copied unchanged.
    /// </summary>
    private readonly record struct Value(
        int Start,
        int End,
        int Next,
        JsonTokenType Token,
        int NameStart,
        int NameLength,
        string? EscapedName,
        bool HasContent,
        bool IsClean);

    private sealed class JsonPruner(List<Value> values)
    {
        public void WriteValue(Utf8JsonWriter writer, ReadOnlySpan<byte> json, int index)
        {
            Value value = values[index];
            if (value.IsClean)
            {
                writer.WriteRawValue(json[value.Start..value.End], skipInputValidation: true);
            }
            else if (value.Token == JsonTokenType.StartObject)
            {
                WriteObject(writer, json, index);
            }
            else
            {
                WriteArray(writer, json, index, companion: -1);
            }
        }

        private void WriteObject(Utf8JsonWriter writer, ReadOnlySpan<byte> json, int index)
        {
            Dictionary<string, int>? arrays = null;
            writer.WriteStartObject();
            for (int child = index + 1; child < values[index].Next; child = values[child].Next)
            {
                Value property = values[child];
                if (!property.HasContent)
                {
                    continue;
                }

                if (property.EscapedName is { } name)
                {
                    writer.WritePropertyName(name);
                }
                else
                {
                    writer.WritePropertyName(json.Slice(property.NameStart, property.NameLength));
                }

                if (property.Token == JsonTokenType.StartArray)
                {
                    arrays ??= ArrayProperties(json, index);
                    WriteArray(writer, json, child, arrays.GetValueOrDefault(CompanionName(Name(json, property)), -1));
                }
                else
                {
                    WriteValue(writer, json, child);
                }
            }

            writer.WriteEndObject();
        }

        private void WriteArray(Utf8JsonWriter writer, ReadOnlySpan<byte> json, int index, int companion)
        {
            writer.WriteStartArray();
            int pair = companion < 0 ? -1 : companion + 1;
            for (int item = index + 1; item < values[index].Next; item = values[item].Next)
            {
                bool pairHasContent = pair >= 0 && pair < values[companion].Next && values[pair].HasContent;
                if (values[item].HasContent)
                {
                    WriteValue(writer, json, item);
                }
                else if (pairHasContent)
                {
                    writer.WriteNullValue();
                }

                if (pair >= 0 && pair < values[companion].Next)
                {
                    pair = values[pair].Next;
                }
            }

            writer.WriteEndArray();
        }

        // Built once per object, so resolving companions stays linear in the number of properties.
        private Dictionary<string, int> ArrayProperties(ReadOnlySpan<byte> json, int obj)
        {
            var arrays = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int child = obj + 1; child < values[obj].Next; child = values[child].Next)
            {
                if (values[child].Token == JsonTokenType.StartArray)
                {
                    arrays[Name(json, values[child])] = child;
                }
            }

            return arrays;
        }

        private static string Name(ReadOnlySpan<byte> json, Value value) =>
            value.EscapedName ?? Encoding.UTF8.GetString(json.Slice(value.NameStart, value.NameLength));
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
}