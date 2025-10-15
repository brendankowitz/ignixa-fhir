// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Sparky.SourceNodeSerialization.SourceNodes;
using Sparky.SourceNodeSerialization.SourceNodes.Models;

namespace Sparky.SourceNodeSerialization;

public static class JsonSourceNodeFactory
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Encoder = JavaScriptEncoder.Default,
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

    public static string SerializeToString(this ResourceJsonNode resource)
    {
        return JsonSerializer.Serialize(resource, _jsonSerializerOptions);
    }
}
