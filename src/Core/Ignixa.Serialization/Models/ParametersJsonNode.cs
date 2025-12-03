// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Strongly-typed model for FHIR Parameters resource.
/// Used for parsing FHIRPath Patch operations.
/// Uses MutableNode for JsonObject-based storage.
/// </summary>
public class ParametersJsonNode : ResourceJsonNode
{
    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public ParametersJsonNode()
    {
        ResourceType = "Parameters";
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public ParametersJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Gets the parameter array from the internal JsonObject.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<ParameterJsonNode> Parameter => GetListProperty<ParameterJsonNode>("parameter");

    /// <summary>
    /// Finds a parameter by name.
    /// </summary>
    public ParameterJsonNode FindParameter(string name)
    {
        return Parameter.FirstOrDefault(p => p.Name == name);
    }
}

/// <summary>
/// Represents a single parameter in Parameters.parameter[].
/// Can contain either a value[x] or nested part[] array.
/// Uses MutableNode for JsonObject-based storage.
/// </summary>
public class ParameterJsonNode : BaseJsonNode
{
    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public ParameterJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public ParameterJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Gets or sets the parameter name.
    /// </summary>
    [JsonIgnore]
    public string Name
    {
        get => GetProperty<string>("name");
        set => SetProperty("name", value);
    }

    /// <summary>
    /// Gets nested parameters (part array).
    /// </summary>
    [JsonIgnore]
    [SuppressMessage("Naming", "CA1721:Property names should not match method names", Justification = "FHIR specification uses 'part'")]
    public MutableJsonList<ParameterJsonNode> Part => GetListProperty<ParameterJsonNode>("part");

    /// <summary>
    /// Gets the resource property (for Parameters.parameter.resource).
    /// </summary>
    [JsonIgnore]
    public ResourceJsonNode? Resource
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("resource", out var resourceNode) || resourceNode == null)
            {
                return null;
            }

            // Parse as ResourceJsonNode
            var json = resourceNode.ToJsonString();
            return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        }
    }

    /// <summary>
    /// Finds a part by name.
    /// </summary>
    public ParameterJsonNode FindPart(string name)
    {
        return Part.FirstOrDefault(p => p.Name == name);
    }

    /// <summary>
    /// Gets the first value[x] field that is not null.
    /// Returns the value as a JsonNode for maximum flexibility.
    /// </summary>
    public JsonNode GetValue()
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
    /// Gets a specific value[x] field by name (e.g., "valueString", "valueCode").
    /// </summary>
    public JsonNode GetValue(string valueName)
    {
        return MutableNode.TryGetPropertyValue(valueName, out var node) ? node : null;
    }

    /// <summary>
    /// Gets a value[x] field as a specific .NET type.
    /// </summary>
    public T GetValueAs<T>()
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
    /// Gets a named value[x] field as a specific .NET type.
    /// </summary>
    public T GetValueAs<T>(string valueName)
    {
        if (!MutableNode.TryGetPropertyValue(valueName, out var node) || node == null)
        {
            return default;
        }

        try
        {
            if (node is JsonValue jsonValue)
            {
                return jsonValue.GetValue<T>();
            }

            return JsonSerializer.Deserialize<T>(node.ToJsonString());
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Sets a value[x] field.
    /// </summary>
    public void SetValue(string valueName, JsonNode value)
    {
        SetProperty(valueName, value);
    }

    /// <summary>
    /// Sets a value[x] field from a .NET object.
    /// </summary>
    public void SetValue<T>(string valueName, T value)
    {
        if (value == null)
        {
            SetProperty(valueName, null);
            return;
        }

        // For primitive types, use JsonValue
        if (value is string s)
        {
            SetProperty(valueName, JsonValue.Create(s));
        }
        else if (value is int i)
        {
            SetProperty(valueName, JsonValue.Create(i));
        }
        else if (value is bool b)
        {
            SetProperty(valueName, JsonValue.Create(b));
        }
        else if (value is BaseJsonNode baseJsonNode)
        {
            // For BaseJsonNode types, use MutableNode directly
            SetProperty(valueName, baseJsonNode.MutableNode);
        }
        else
        {
            // For other complex types, serialize to JsonNode
            var json = JsonSerializer.Serialize(value);
            var node = JsonNode.Parse(json);
            SetProperty(valueName, node);
        }
    }
}
