// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Ignixa.Serialization;

public static class JsonSourceNodeFactory
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new JsonNodeConverterFactory() },
        // JsonNode.WriteTo fails on a JsonValue wrapping a CLR object unless a resolver is set; only JsonSerializer calls fill it in.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static TResource Parse<TResource>(string json)
        where TResource : ResourceJsonNode
    {
        return JsonSerializer.Deserialize<TResource>(json, _jsonSerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON to {typeof(TResource).Name}");
    }

    public static async ValueTask<T> ParseAsync<T>(Stream jsonReader, CancellationToken cancellationToken)
        where T : ResourceJsonNode
    {
        return await JsonSerializer.DeserializeAsync<T>(jsonReader, _jsonSerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON to {typeof(T).Name}");
    }

    public static ResourceJsonNode Parse(string json)
    {
        return Parse<ResourceJsonNode>(json);
    }

    public static ValueTask<ResourceJsonNode> ParseAsync(Stream jsonReader, CancellationToken cancellationToken)
    {
        return ParseAsync<ResourceJsonNode>(jsonReader, cancellationToken);
    }

    /// <summary>
    /// Parses a ResourceJsonNode from a ReadOnlyMemory&lt;byte&gt; containing UTF-8 encoded JSON.
    /// </summary>
    /// <typeparam name="TResource">The resource type to deserialize to</typeparam>
    /// <param name="jsonBytes">UTF-8 encoded JSON bytes</param>
    /// <returns>Deserialized resource with proper converter options applied</returns>
    public static TResource Parse<TResource>(ReadOnlyMemory<byte> jsonBytes)
        where TResource : ResourceJsonNode
    {
        var reader = new Utf8JsonReader(jsonBytes.Span);
        return JsonSerializer.Deserialize<TResource>(ref reader, _jsonSerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON to {typeof(TResource).Name}");
    }

    /// <summary>
    /// Parses a ResourceJsonNode from a ReadOnlyMemory&lt;byte&gt; containing UTF-8 encoded JSON.
    /// </summary>
    /// <param name="jsonBytes">UTF-8 encoded JSON bytes</param>
    /// <returns>Deserialized resource with proper converter options applied</returns>
    public static ResourceJsonNode Parse(ReadOnlyMemory<byte> jsonBytes)
    {
        return Parse<ResourceJsonNode>(jsonBytes);
    }

    /// <summary>
    /// Parses a ResourceJsonNode directly from a JsonNode without serialization.
    /// More efficient than Parse(string) when you already have a JsonNode.
    /// </summary>
    /// <typeparam name="TResource">The resource type to deserialize to.</typeparam>
    /// <param name="jsonNode">JsonNode to convert.</param>
    /// <returns>Deserialized resource with proper converter options applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when jsonNode is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails.</exception>
    public static TResource Parse<TResource>(JsonNode jsonNode)
        where TResource : ResourceJsonNode
    {
        ArgumentNullException.ThrowIfNull(jsonNode);
        return JsonSerializer.Deserialize<TResource>(jsonNode, _jsonSerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize JsonNode to {typeof(TResource).Name}");
    }

    /// <summary>
    /// Parses a ResourceJsonNode directly from a JsonNode without serialization.
    /// More efficient than Parse(string) when you already have a JsonNode.
    /// </summary>
    /// <param name="jsonNode">JsonNode to convert.</param>
    /// <returns>Deserialized resource with proper converter options applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when jsonNode is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails.</exception>
    public static ResourceJsonNode Parse(JsonNode jsonNode)
    {
        return Parse<ResourceJsonNode>(jsonNode);
    }

    /// <inheritdoc cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="pretty">Whether to indent the output.</param>
    public static string SerializeToString(this ResourceJsonNode resource, bool pretty = false) =>
        resource.SerializeToString(JsonEscaping.Minimal, pretty);

    /// <inheritdoc cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="escaping">How strings are escaped.</param>
    /// <param name="pretty">Whether to indent the output.</param>
    public static string SerializeToString(this ResourceJsonNode resource, JsonEscaping escaping, bool pretty = false) =>
        resource.SerializeToString(WriterOptions(pretty, escaping));

    /// <summary>
    /// Serializes the resource as FHIR JSON, omitting null properties and empty objects or arrays.
    /// </summary>
    /// <remarks>
    /// Unlike System.Text.Json, a null <see cref="JsonWriterOptions.Encoder"/> selects <see cref="MinimalJsonEncoder.Instance"/>.
    /// </remarks>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="options">Writer options; use <see cref="JavaScriptEncoder.Default"/> as the encoder for HTML-safe output.</param>
    public static string SerializeToString(this ResourceJsonNode resource, JsonWriterOptions options)
    {
        using PooledByteBufferWriter buffer = WriteToBuffer(resource, options);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <inheritdoc cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>
    public static ReadOnlyMemory<byte> SerializeToBytes(this ResourceJsonNode resource, bool pretty = false) =>
        resource.SerializeToBytes(JsonEscaping.Minimal, pretty);

    /// <inheritdoc cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>
    public static ReadOnlyMemory<byte> SerializeToBytes(this ResourceJsonNode resource, JsonEscaping escaping, bool pretty = false) =>
        resource.SerializeToBytes(WriterOptions(pretty, escaping));

    /// <inheritdoc cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>
    public static ReadOnlyMemory<byte> SerializeToBytes(this ResourceJsonNode resource, JsonWriterOptions options)
    {
        using PooledByteBufferWriter buffer = WriteToBuffer(resource, options);
        return buffer.WrittenSpan.ToArray();
    }

    /// <inheritdoc cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>
    public static void SerializeToStream(this ResourceJsonNode resource, Stream outStream, bool pretty = false) =>
        resource.SerializeToStream(outStream, JsonEscaping.Minimal, pretty);

    /// <inheritdoc cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>
    public static void SerializeToStream(this ResourceJsonNode resource, Stream outStream, JsonEscaping escaping, bool pretty = false) =>
        resource.SerializeToStream(outStream, WriterOptions(pretty, escaping));

    /// <inheritdoc cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>
    public static void SerializeToStream(this ResourceJsonNode resource, Stream outStream, JsonWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(outStream);

        using PooledByteBufferWriter buffer = WriteToBuffer(resource, options);
        outStream.Write(buffer.WrittenSpan);
    }

    /// <inheritdoc cref="SerializeToStreamAsync(ResourceJsonNode, Stream, JsonWriterOptions, int, CancellationToken)"/>
    public static Task SerializeToStreamAsync(
        this ResourceJsonNode resource,
        Stream outStream,
        bool pretty = false,
        int flushThresholdBytes = DefaultStreamFlushThresholdBytes,
        CancellationToken cancellationToken = default) =>
        resource.SerializeToStreamAsync(outStream, JsonEscaping.Minimal, pretty, flushThresholdBytes, cancellationToken);

    /// <inheritdoc cref="SerializeToStreamAsync(ResourceJsonNode, Stream, JsonWriterOptions, int, CancellationToken)"/>
    public static Task SerializeToStreamAsync(
        this ResourceJsonNode resource,
        Stream outStream,
        JsonEscaping escaping,
        bool pretty = false,
        int flushThresholdBytes = DefaultStreamFlushThresholdBytes,
        CancellationToken cancellationToken = default) =>
        resource.SerializeToStreamAsync(outStream, WriterOptions(pretty, escaping), flushThresholdBytes, cancellationToken);

    /// <summary>
    /// Writes the resource to <paramref name="outStream"/> asynchronously, element by element for top-level
    /// arrays (such as Bundle.entry), flushing whenever <paramref name="flushThresholdBytes"/> are pending. The
    /// output is identical to <see cref="SerializeToString(ResourceJsonNode, JsonWriterOptions)"/>, but a large
    /// resource is never held as one string or byte array, and the stream is only written asynchronously
    /// (Kestrel disallows synchronous I/O). The exception is a resource with a top-level companion pair
    /// (<c>x</c> and <c>_x</c>) or a top-level <see cref="JsonValue"/> that wraps a CLR collection, which is
    /// serialized whole before it is written, so that every value is serialized exactly once.
    /// </summary>
    /// <remarks>
    /// Unlike System.Text.Json, a null <see cref="JsonWriterOptions.Encoder"/> selects <see cref="MinimalJsonEncoder.Instance"/>.
    /// </remarks>
    public static async Task SerializeToStreamAsync(
        this ResourceJsonNode resource,
        Stream outStream,
        JsonWriterOptions options,
        int flushThresholdBytes = DefaultStreamFlushThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(outStream);

        JsonObject root = resource.MutableNode;
        if (RequiresWholeResource(root))
        {
            using PooledByteBufferWriter buffer = WriteToBuffer(resource, options);
            await outStream.WriteAsync(buffer.WrittenMemory, cancellationToken);
            return;
        }

        await using var writer = new Utf8JsonWriter(outStream, WithDefaultEncoder(options));
        writer.WriteStartObject();
        foreach ((string name, JsonNode? value) in root)
        {
            if (value is JsonArray array)
            {
                await WriteTopLevelArrayAsync(writer, name, array, flushThresholdBytes, cancellationToken);
            }
            else
            {
                using PooledByteBufferWriter? json = FhirJsonNodeWriter.SerializeContent(writer, value, TopLevelValueDepth, _jsonSerializerOptions);
                if (json is not null)
                {
                    writer.WritePropertyName(name);
                    FhirJsonNodeWriter.WriteSerialized(writer, json);
                }
            }

            await FlushIfPendingAsync(writer, flushThresholdBytes, cancellationToken);
        }

        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    private const int TopLevelValueDepth = 1;

    // The property is held back until an item has content, because an array without content is omitted.
    // Arrays reaching here have no companion, so an empty item never needs a placeholder.
    private static async Task WriteTopLevelArrayAsync(
        Utf8JsonWriter writer,
        string name,
        JsonArray array,
        int flushThresholdBytes,
        CancellationToken cancellationToken)
    {
        // The array is only opened once an item has content, so its own depth is checked up front, as the
        // whole-resource writer checks it even when every item is empty.
        FhirJsonNodeWriter.ThrowIfContainerExceedsDepth(writer.Options, TopLevelValueDepth);

        const int itemDepth = TopLevelValueDepth + 1;
        bool isStarted = false;
        foreach (JsonNode? value in array)
        {
            using (PooledByteBufferWriter? item = FhirJsonNodeWriter.SerializeContent(writer, value, itemDepth, _jsonSerializerOptions))
            {
                if (item is not null)
                {
                    if (!isStarted)
                    {
                        writer.WritePropertyName(name);
                        writer.WriteStartArray();
                        isStarted = true;
                    }

                    FhirJsonNodeWriter.WriteSerialized(writer, item);
                }
            }

            await FlushIfPendingAsync(writer, flushThresholdBytes, cancellationToken);
        }

        if (isStarted)
        {
            writer.WriteEndArray();
        }
    }

    // Aligning a companion pair (x and _x) needs each item's content decided once for both arrays, which
    // only the whole-resource writer does. A JsonValue wrapping a CLR collection serializes as an array
    // that may be one half of a pair. Top-level pairs are small repeating primitives, so this is rare
    // and never applies to Bundle.entry.
    private static bool RequiresWholeResource(JsonObject root)
    {
        foreach ((string name, JsonNode? value) in root)
        {
            bool mayBePaired = value switch
            {
                JsonArray => FhirJsonNodeWriter.CompanionOf(root, name) is not null,
                JsonValue wrapped => MayWriteAsArray(wrapped),
                _ => false,
            };

            if (mayBePaired)
            {
                return true;
            }
        }

        return false;
    }

    // GetValueKind would serialize a CLR value to find its kind, consuming a single-use enumerable before
    // it is written, so the wrapped value is inspected instead. Every non-string enumerable is treated as
    // an array; a dictionary is then only written unstreamed, with the same output.
    private static bool MayWriteAsArray(JsonValue value)
    {
        if (value.TryGetValue(out JsonElement element))
        {
            return element.ValueKind == JsonValueKind.Array;
        }

        return value.TryGetValue(out object? clrValue) && clrValue is IEnumerable and not string;
    }

    private const int DefaultStreamFlushThresholdBytes = 256 * 1024;

    private static JsonWriterOptions WriterOptions(bool pretty, JsonEscaping escaping) => new()
    {
        Encoder = escaping switch
        {
            JsonEscaping.Minimal => MinimalJsonEncoder.Instance,
            JsonEscaping.HtmlSafe => JavaScriptEncoder.Default,
            _ => throw new ArgumentOutOfRangeException(nameof(escaping), escaping, "Unsupported JSON escaping."),
        },
        Indented = pretty,
    };

    private static JsonWriterOptions WithDefaultEncoder(JsonWriterOptions options) =>
        options.Encoder is null ? options with { Encoder = MinimalJsonEncoder.Instance } : options;

    private static PooledByteBufferWriter WriteToBuffer(ResourceJsonNode resource, JsonWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return FhirJsonNodeWriter.WriteToBuffer(resource.MutableNode, WithDefaultEncoder(options), _jsonSerializerOptions);
    }

    private static ValueTask FlushIfPendingAsync(Utf8JsonWriter writer, int flushThresholdBytes, CancellationToken cancellationToken)
        => writer.BytesPending >= flushThresholdBytes
            ? new ValueTask(writer.FlushAsync(cancellationToken))
            : ValueTask.CompletedTask;
}
