// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a parameter to a transform in a StructureMap target.
/// </summary>
public class StructureMapParameterJsonNode : BaseJsonNode
{
    public StructureMapParameterJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapParameterJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Gets the parameter value (choice type - can be valueId, valueString, valueBoolean, valueInteger, valueDecimal).
    /// Returns the JsonNode for the first value[x] property found.
    /// </summary>
    public JsonNode? GetValue()
    {
        foreach (var property in MutableNode)
        {
            if (property.Key.StartsWith("value", StringComparison.Ordinal))
            {
                return property.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the value as a specific type.
    /// </summary>
    public T? GetValueAs<T>()
    {
        var valueNode = GetValue();
        if (valueNode == null)
        {
            return default;
        }

        try
        {
            if (valueNode is JsonValue jsonValue)
            {
                return jsonValue.GetValue<T>();
            }

            return JsonSerializer.Deserialize<T>(valueNode.ToJsonString());
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Sets a value with the specified type suffix (e.g., "valueString", "valueInteger").
    /// </summary>
    public void SetValue(string suffix, JsonNode? value)
    {
        // Remove any existing value[x] properties
        var keysToRemove = MutableNode.Where(p => p.Key.StartsWith("value", StringComparison.Ordinal))
            .Select(p => p.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            MutableNode.Remove(key);
        }

        // Set new value if not null
        if (value != null)
        {
            SetProperty($"value{suffix}", value);
        }
    }

    /// <summary>
    /// Sets a typed value using common FHIR datatypes.
    /// </summary>
    public void SetTypedValue<T>(T value)
    {
        if (value == null)
        {
            return;
        }

        switch (value)
        {
            case string s:
                SetValue("String", JsonValue.Create(s));
                break;
            case int i:
                SetValue("Integer", JsonValue.Create(i));
                break;
            case bool b:
                SetValue("Boolean", JsonValue.Create(b));
                break;
            case decimal d:
                SetValue("Decimal", JsonValue.Create(d));
                break;
            default:
                // For complex types, serialize to JsonNode
                var json = JsonSerializer.Serialize(value);
                var node = JsonNode.Parse(json);
                SetValue(typeof(T).Name, node);
                break;
        }
    }
}
