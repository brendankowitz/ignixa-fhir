// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignixa.SourceNodeSerialization.SourceNodes.Models;

/// <summary>
/// Strongly-typed model for FHIR Parameters resource.
/// Used for parsing FHIRPath Patch operations.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
public class ParametersJsonNode : ResourceJsonNode
{
    public ParametersJsonNode()
    {
        ResourceType = "Parameters";
    }

    [JsonPropertyName("parameter")]
    public IList<ParameterJsonNode> Parameter { get; set; }

    /// <summary>
    /// Parse a JSON string into a ParametersJsonNode.
    /// </summary>
    public new static ParametersJsonNode Parse(string json)
    {
        return JsonSourceNodeFactory.Parse<ParametersJsonNode>(json);
    }
}

/// <summary>
/// Represents a single parameter in Parameters.parameter[].
/// Can contain either a value[x] or nested part[] array.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
public class ParameterJsonNode : IExtensionData
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    // Value[x] fields - FHIR allows value[Type] where Type can be many things
    // For FHIRPath Patch, we commonly see: valueCode, valueString, valueInteger, etc.

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valueCode")]
    public string ValueCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valueString")]
    public string ValueString { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valueInteger")]
    public int? ValueInteger { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valueBoolean")]
    public bool? ValueBoolean { get; set; }

    /// <summary>
    /// Nested parameters (for operation parts).
    /// This allows Parameters to have a recursive structure.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("part")]
    [SuppressMessage("Naming", "CA1721:Property names should not match method names", Justification = "FHIR specification uses 'part'")]
    public IList<ParameterJsonNode> Part { get; set; }

    /// <summary>
    /// Captures any value[x] or other fields not explicitly modeled.
    /// Useful for valueHumanName, valueContactPoint, valueAddress, etc.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }

    /// <summary>
    /// Get a part by name.
    /// </summary>
    public ParameterJsonNode GetPart(string name)
    {
        if (Part == null)
        {
            return null;
        }

        foreach (var part in Part)
        {
            if (part.Name == name)
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>
    /// Get the first value[x] field that is not null.
    /// Returns the raw JsonElement for complex types.
    /// </summary>
    public object GetValue()
    {
        // Check explicit properties first
        if (ValueCode != null) return ValueCode;
        if (ValueString != null) return ValueString;
        if (ValueInteger != null) return ValueInteger.Value;
        if (ValueBoolean != null) return ValueBoolean.Value;

        // Check extension data for other value[x] fields
        if (ExtensionData != null)
        {
            foreach (var kvp in ExtensionData)
            {
                if (kvp.Key.StartsWith("value", StringComparison.Ordinal))
                {
                    return kvp.Value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Deserialize a value[x] field from ExtensionData to a specific type.
    /// Useful for complex FHIR types like HumanName, ContactPoint, etc.
    /// </summary>
    public T GetValueAs<T>(string valueName) where T : class
    {
        if (ExtensionData == null || !ExtensionData.TryGetValue(valueName, out var element))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }
        catch
        {
            return null;
        }
    }
}
