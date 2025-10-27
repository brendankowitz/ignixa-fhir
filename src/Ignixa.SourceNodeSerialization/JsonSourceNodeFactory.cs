// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Ignixa.SourceNodeSerialization.Models;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.SourceNodeSerialization;

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

    public static async ValueTask<T> Parse<T>(Stream jsonReader)
        where T : ResourceJsonNode
    {
        T resource = await JsonSerializer.DeserializeAsync<T>(jsonReader, _jsonSerializerOptions);
        return resource;
    }

    public static ResourceJsonNode Parse(string json)
    {
        return Parse<ResourceJsonNode>(json);
    }

    public static ValueTask<ResourceJsonNode> Parse(Stream jsonReader)
    {
        return Parse<ResourceJsonNode>(jsonReader);
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

    public static string SerializeToString(this ResourceJsonNode resource)
    {
        return resource.MutableNode.ToJsonString(_jsonSerializerOptions);
    }

    public static void SerializeToStream(this ResourceJsonNode resource, Stream outStream)
    {
        JsonSerializer.Serialize(outStream, resource.MutableNode, _jsonSerializerOptions);
    }
}
