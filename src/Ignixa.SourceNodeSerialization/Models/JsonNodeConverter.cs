// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.SourceNodeSerialization.Models;

/// <summary>
/// Custom JSON converter for BaseJsonNode that uses internal JsonObject storage.
/// Intercepts deserialization to parse into JsonObject first, then wraps in BaseJsonNode.
/// Serialization writes the internal JsonObject directly (no extra conversion).
/// </summary>
public class JsonNodeConverter<TJsonNodeType> : JsonConverter<TJsonNodeType>
 where TJsonNodeType : BaseJsonNode
{
    /// <summary>
    /// Reads JSON and creates ResourceJsonNode with internal JsonObject storage.
    /// </summary>
    public override TJsonNodeType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Parse JSON to JsonObject (mutable)
        var jsonObject = JsonSerializer.Deserialize<JsonObject>(ref reader, options);

        if (jsonObject == null)
        {
            throw new JsonException("Failed to parse JSON into JsonObject for ResourceJsonNode");
        }

        // Create ResourceJsonNode with internal storage using the 1-parameter constructor (JsonObject)
        // BindingFlags must include Instance to find instance constructors
        return (TJsonNodeType)Activator.CreateInstance(
            typeToConvert,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [jsonObject],
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes ResourceJsonNode by serializing its internal JsonObject directly.
    /// </summary>
    public override void Write(
        Utf8JsonWriter writer,
        TJsonNodeType value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        // Serialize the internal JsonObject directly (no conversion needed)
        JsonObject internalNode = value.MutableNode;
        JsonSerializer.Serialize(writer, internalNode, options);
    }
}
