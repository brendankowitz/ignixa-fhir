// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.SourceNodeSerialization.Converters;

/// <summary>
/// Generic JSON converter for BaseJsonNode subclasses that uses internal JsonObject storage.
/// Serialization writes the internal JsonObject directly (no extra conversion).
/// Deserialization parses into JsonObject first, then wraps in the target type.
/// </summary>
/// <typeparam name="T">The BaseJsonNode subclass type.</typeparam>
public class BaseJsonNodeConverter<T> : JsonConverter<T>
    where T : BaseJsonNode
{
    /// <summary>
    /// Reads JSON and creates instance with internal JsonObject storage.
    /// </summary>
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Parse JSON to JsonObject (mutable)
        var jsonObject = JsonSerializer.Deserialize<JsonObject>(ref reader, options);

        if (jsonObject == null)
        {
            throw new JsonException($"Failed to parse JSON into JsonObject for {typeof(T).Name}");
        }

        // Create instance with internal storage using Activator
        // This requires a constructor that accepts JsonObject
        try
        {
            var instance = (T)Activator.CreateInstance(typeof(T), jsonObject);
            return instance;
        }
        catch (MissingMethodException)
        {
            throw new JsonException(
                $"{typeof(T).Name} must have a constructor that accepts JsonObject parameter for deserialization");
        }
    }

    /// <summary>
    /// Writes BaseJsonNode by serializing its internal JsonObject directly.
    /// </summary>
    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        // Serialize the internal JsonObject directly (no conversion needed)
        var internalNode = value.MutableNode;
        JsonSerializer.Serialize(writer, internalNode, options);
    }
}
