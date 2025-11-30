// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Encoder = JavaScriptEncoder.Default,
        Converters = { new JsonNodeConverterFactory() }
    };

    public static TResource Parse<TResource>(string json)
        where TResource : ResourceJsonNode
    {
        TResource resource = JsonSerializer.Deserialize<TResource>(json, _jsonSerializerOptions);
        return resource;
    }

    public static async ValueTask<T> ParseAsync<T>(Stream jsonReader, CancellationToken cancellationToken)
        where T : ResourceJsonNode
    {
        T resource = await JsonSerializer.DeserializeAsync<T>(jsonReader, _jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return resource;
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
        TResource resource = JsonSerializer.Deserialize<TResource>(ref reader, _jsonSerializerOptions);
        return resource;
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

    public static string SerializeToString(this ResourceJsonNode resource)
    {
        return resource.MutableNode.ToJsonString(_jsonSerializerOptions);
    }

    public static void SerializeToStream(this ResourceJsonNode resource, Stream outStream)
    {
        JsonSerializer.Serialize(outStream, resource.MutableNode, _jsonSerializerOptions);
    }
    
    public static ReadOnlyMemory<byte> SerializeToBytes(this ResourceJsonNode resource)
    {
        return JsonSerializer.SerializeToUtf8Bytes(resource.MutableNode, _jsonSerializerOptions);
    }
}
