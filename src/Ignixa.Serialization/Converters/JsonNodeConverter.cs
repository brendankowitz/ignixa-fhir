// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Custom JSON converter for BaseJsonNode that uses internal JsonObject storage.
/// Intercepts deserialization to parse into JsonObject first, then wraps in BaseJsonNode.
/// Serialization writes the internal JsonObject directly (no extra conversion).
/// </summary>
public class JsonNodeConverter<TJsonNodeType> : JsonConverter<TJsonNodeType>
 where TJsonNodeType : BaseJsonNode
{
    /// <summary>
    /// Reads JSON and creates a ResourceJsonNode with internal JsonObject storage.
    ///
    /// Smart routing: If the caller requested the base ResourceJsonNode type (not a specific subclass),
    /// the converter inspects the resourceType field and creates the appropriate specific type
    /// (e.g., ParametersJsonNode, BundleJsonNode) using the factory registry.
    ///
    /// This allows polymorphic deserialization: Parse<ResourceJsonNode>(json) → creates ParametersJsonNode
    /// while maintaining backward compatibility: Parse<ParametersJsonNode>(json) → creates ParametersJsonNode
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

        Type actualType = typeToConvert;

        // Smart routing: Only for ResourceJsonNode base type
        // If the caller asked for a specific subclass (ParametersJsonNode, etc.), skip smart routing
        if (typeToConvert == typeof(ResourceJsonNode))
        {
            var resourceType = jsonObject["resourceType"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(resourceType) &&
                ResourceTypeRegistry.TryCreateInstance(resourceType, jsonObject, out var specificInstance))
            {
                // Successfully created the specific type via registry
                return (TJsonNodeType)(object)specificInstance;
            }

            // Fall back to generic ResourceJsonNode for unknown resource types
        }

        // For specific types or unknown resource types, use reflection to invoke the internal constructor
        // BindingFlags must include Instance to find instance constructors
        return (TJsonNodeType)Activator.CreateInstance(
            actualType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [jsonObject, null],
            CultureInfo.InvariantCulture)!;
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
