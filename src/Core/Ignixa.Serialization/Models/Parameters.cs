// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Models;

public partial class Parameters
{
    /// <summary>
    /// Finds a parameter by name.
    /// </summary>
    public ParametersParameter FindParameter(string name)
    {
        return Parameter.FirstOrDefault(p => p.Name == name);
    }
}

public partial class ParametersParameter
{
    /// <summary>
    /// Finds a part by name.
    /// </summary>
    public ParametersParameter FindPart(string name)
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
