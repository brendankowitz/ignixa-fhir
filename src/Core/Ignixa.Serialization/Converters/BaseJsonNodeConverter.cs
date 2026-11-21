// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Converters;

/// <summary>
/// Generic JSON converter for BaseJsonNode subclasses that uses internal JsonObject storage.
/// Serialization writes the internal JsonObject, omitting null properties and empty objects or arrays;
/// escaping follows the caller's <see cref="JsonSerializerOptions.Encoder"/>.
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
    /// Writes the node's JSON, omitting null properties and empty objects or arrays.
    /// </summary>
    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        var internalNode = value.MutableNode;
        FhirJsonNodeWriter.Write(writer, internalNode, options);
    }
}
