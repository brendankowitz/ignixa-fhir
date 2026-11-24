// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
/// A node is serialized once, unchanged, so a lazily parsed tree is never materialized. Emptiness is
/// decided from those bytes alone, so a <see cref="JsonValue"/> that serializes to a null or an empty
/// container is omitted like any other. When the bytes hold a null or an empty container, one reader
/// pass records each container's content, and pruning copies clean branches by byte range.
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
        PooledByteBufferWriter json = Serialize(node, writerOptions, depth: 0, options, out _);
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
                    WriteSerialized(writer, json);
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

    /// <summary>
    /// Writes <paramref name="node"/> as the next value of <paramref name="writer"/>. A node without
    /// content is still written, as an empty container, because a value is required there.
    /// </summary>
    public static void Write(Utf8JsonWriter writer, JsonNode node, JsonSerializerOptions options)
    {
        using PooledByteBufferWriter json = Serialize(node, writer.Options, writer.CurrentDepth, options, out _);
        WriteSerialized(writer, json);
    }

    /// <summary>
    /// Serializes <paramref name="node"/> as it will be written inside <paramref name="depth"/> open
    /// containers of <paramref name="writer"/>. Returns null when nothing with content remains;
    /// otherwise the caller disposes the buffer.
    /// </summary>
    /// <exception cref="InvalidOperationException">The node nests deeper than the writer's maximum depth.</exception>
    public static PooledByteBufferWriter? SerializeContent(Utf8JsonWriter writer, JsonNode? node, int depth, JsonSerializerOptions options)
    {
        if (node is null)
        {
            return null;
        }

        PooledByteBufferWriter json = Serialize(node, writer.Options, depth, options, out bool hasContent);
        if (hasContent)
        {
            return json;
        }

        json.Dispose();
        return null;
    }

    /// <summary>Writes bytes from <see cref="SerializeContent"/> as the next value of <paramref name="writer"/>.</summary>
    public static void WriteSerialized(Utf8JsonWriter writer, PooledByteBufferWriter json)
    {
        if (!writer.Options.Indented)
        {
            writer.WriteRawValue(json.WrittenSpan, skipInputValidation: true);
            return;
        }

        // WriteRawValue does not indent, so indented output is rewritten from the compact bytes; the
        // writer's encoder escapes strings exactly as before. The bytes were written within the writer's
        // remaining depth, so the document's own limit only has to be at least that.
        using JsonDocument document = JsonDocument.Parse(json.WrittenMemory, new JsonDocumentOptions { MaxDepth = MaxDepth(writer.Options) });
        document.RootElement.WriteTo(writer);
    }

    /// <summary>Gets the name paired with <paramref name="name"/>: <c>_x</c> for <c>x</c>, and <c>x</c> for <c>_x</c>.</summary>
    public static string CompanionName(string name) => name.StartsWith("_", StringComparison.Ordinal) ? name[1..] : "_" + name;

    private static int MaxDepth(JsonWriterOptions options) => options.MaxDepth == 0 ? DefaultMaxDepth : options.MaxDepth;

    // Copied bytes are not depth-checked by the destination writer, so the compact writer gets only the
    // depth that remains below the containers already open around the node.
    private static PooledByteBufferWriter Serialize(JsonNode node, JsonWriterOptions writerOptions, int depth, JsonSerializerOptions options, out bool hasContent)
    {
        int maxDepth = MaxDepth(writerOptions);
        int remaining = maxDepth - depth;
        if (remaining > 0)
        {
            return WriteCompact(node, writerOptions with { Indented = false, MaxDepth = remaining }, options, out hasContent);
        }

        // No container may open here, and a MaxDepth of 0 would mean the default, so one level is
        // allowed and then rejected if the node used it.
        if (node is not (JsonObject or JsonArray))
        {
            PooledByteBufferWriter json = WriteCompact(node, writerOptions with { Indented = false, MaxDepth = 1 }, options, out hasContent);
            if (json.WrittenSpan[0] is not ((byte)'{' or (byte)'['))
            {
                return json;
            }

            json.Dispose();
        }

        throw DepthExceeded(depth, maxDepth);
    }

    /// <summary>
    /// Throws when a container opened inside <paramref name="depth"/> open containers would exceed the maximum
    /// depth of <paramref name="options"/>, as the writer would if the container were written.
    /// </summary>
    /// <exception cref="InvalidOperationException">The container does not fit within the maximum depth.</exception>
    public static void ThrowIfContainerExceedsDepth(JsonWriterOptions options, int depth)
    {
        int maxDepth = MaxDepth(options);
        if (depth >= maxDepth)
        {
            throw DepthExceeded(depth, maxDepth);
        }
    }

    private static InvalidOperationException DepthExceeded(int depth, int maxDepth) =>
        new($"CurrentDepth ({depth}) is equal to or larger than the maximum allowed depth of {maxDepth}.");

    /// <summary>Writes <paramref name="node"/> compactly, pruning only when its bytes hold a null or an empty container.</summary>
    private static PooledByteBufferWriter WriteCompact(JsonNode node, JsonWriterOptions compactOptions, JsonSerializerOptions options, out bool hasContent)
    {
        PooledByteBufferWriter json = WriteToNewBuffer(compactOptions, writer => node.WriteTo(writer, options));
        bool isReturned = false;
        try
        {
            ReadOnlySpan<byte> bytes = json.WrittenSpan;
            if (!MayNeedPruning(bytes))
            {
                hasContent = !(bytes.SequenceEqual("{}"u8) || bytes.SequenceEqual("[]"u8) || bytes.SequenceEqual("null"u8));
                isReturned = true;
                return json;
            }

            List<Value> values = Analyze(bytes, compactOptions.MaxDepth);
            hasContent = values[0].HasContent;
            if (values[0].IsClean)
            {
                isReturned = true;
                return json;
            }

            ReadOnlyMemory<byte> source = json.WrittenMemory;
            var pruner = new JsonPruner(values);
            return WriteToNewBuffer(compactOptions, writer => pruner.WriteValue(writer, source.Span, index: 0));
        }
        finally
        {
            if (!isReturned)
            {
                json.Dispose();
            }
        }
    }

    // Utf8JsonWriter writes an empty container as {} or [], and in compact output a value always follows
    // ':', '[' or ',' and precedes ',', ']' or '}'. Text inside a string can still match, so a hit is
    // only a maybe. The root itself is not matched: an empty root is written as it is.
    private static bool MayNeedPruning(ReadOnlySpan<byte> json) =>
        HasValueToken(json, "{}"u8) || HasValueToken(json, "[]"u8) || HasValueToken(json, "null"u8);

    private static bool HasValueToken(ReadOnlySpan<byte> json, ReadOnlySpan<byte> token)
    {
        int offset = 0;
        while (true)
        {
            int found = json[offset..].IndexOf(token);
            if (found < 0)
            {
                return false;
            }

            int start = offset + found;
            int end = start + token.Length;
            if (start > 0
                && json[start - 1] is (byte)':' or (byte)'[' or (byte)','
                && end < json.Length
                && json[end] is (byte)',' or (byte)']' or (byte)'}')
            {
                return true;
            }

            offset = start + 1;
        }
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

        // The bytes came from a writer limited to maxDepth, so the reader only needs to allow as much.
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = maxDepth });
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
                    escapedName = null;
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
                    escapedName = null;
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

                // A clean array has no empty item, so it needs no placeholder and is copied whole.
                if (property.Token == JsonTokenType.StartArray && !property.IsClean)
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
}
