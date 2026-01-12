// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Converters;

public class ResourceConverter : JsonConverter<ResourceJsonNode>
{
    private const string Searchparameter = "SearchParameter";

    public override ResourceJsonNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        var node = JsonNode.Parse(ref reader);
        if (node == null || node is not JsonObject jsonObject)
        {
            throw new JsonException();
        }

        var type = jsonObject["resourceType"]?.GetValue<string>();
        if (type == Searchparameter)
        {
            // Directly construct SearchParameterJsonNode from JsonObject (not via Deserialize)
            return new SearchParameterJsonNode(jsonObject);
        }

        // Directly construct ResourceJsonNode from the JsonObject to avoid infinite recursion
        return new ResourceJsonNode(jsonObject);
    }

    public override void Write(Utf8JsonWriter writer, ResourceJsonNode value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
