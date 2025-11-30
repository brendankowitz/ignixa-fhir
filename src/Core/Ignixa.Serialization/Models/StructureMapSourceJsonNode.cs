// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a source element in a StructureMap rule.
/// </summary>
public class StructureMapSourceJsonNode : BaseJsonNode
{
    public StructureMapSourceJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapSourceJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Type or variable this rule applies to.
    /// </summary>
    [JsonIgnore]
    public string? Context
    {
        get => GetProperty<string>("context");
        set => SetProperty("context", value);
    }

    /// <summary>
    /// Specified minimum cardinality.
    /// </summary>
    [JsonIgnore]
    public int? Min
    {
        get => GetProperty<int?>("min");
        set => SetProperty("min", value);
    }

    /// <summary>
    /// Specified maximum cardinality (number or *).
    /// </summary>
    [JsonIgnore]
    public string? Max
    {
        get => GetProperty<string>("max");
        set => SetProperty("max", value);
    }

    /// <summary>
    /// Rule only applies if source has this type.
    /// </summary>
    [JsonIgnore]
    public string? Type
    {
        get => GetProperty<string>("type");
        set => SetProperty("type", value);
    }

    /// <summary>
    /// Optional field for this source.
    /// </summary>
    [JsonIgnore]
    public string? Element
    {
        get => GetProperty<string>("element");
        set => SetProperty("element", value);
    }

    /// <summary>
    /// How to handle the list mode: first | not_first | last | not_last | only_one.
    /// </summary>
    [JsonIgnore]
    public StructureMapSourceListMode? ListMode
    {
        get
        {
            var listModeStr = GetProperty<string>("listMode");
            return listModeStr != null ? EnumUtility.ParseLiteral<StructureMapSourceListMode>(listModeStr) : null;
        }
        set => SetProperty("listMode", value?.GetLiteral());
    }

    /// <summary>
    /// Named context for field, if a field is specified.
    /// </summary>
    [JsonIgnore]
    public string? Variable
    {
        get => GetProperty<string>("variable");
        set => SetProperty("variable", value);
    }

    /// <summary>
    /// FHIRPath expression - must be true for the mapping to apply.
    /// </summary>
    [JsonIgnore]
    public string? Condition
    {
        get => GetProperty<string>("condition");
        set => SetProperty("condition", value);
    }

    /// <summary>
    /// FHIRPath expression - must be true or the rule does not apply.
    /// </summary>
    [JsonIgnore]
    public string? Check
    {
        get => GetProperty<string>("check");
        set => SetProperty("check", value);
    }

    /// <summary>
    /// Message to put in log if source exists (FHIRPath).
    /// </summary>
    [JsonIgnore]
    public string? LogMessage
    {
        get => GetProperty<string>("logMessage");
        set => SetProperty("logMessage", value);
    }

    /// <summary>
    /// Gets the default value (choice type - can be any FHIR datatype).
    /// Returns the JsonNode for the first default[x] property found.
    /// </summary>
    public JsonNode? GetDefaultValue()
    {
        foreach (var property in MutableNode)
        {
            if (property.Key.StartsWith("default", StringComparison.Ordinal))
            {
                return property.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Sets a default value with the specified type suffix (e.g., "defaultString", "defaultInteger").
    /// </summary>
    public void SetDefaultValue(string suffix, JsonNode? value)
    {
        // Remove any existing default[x] properties
        var keysToRemove = MutableNode.Where(p => p.Key.StartsWith("default", StringComparison.Ordinal))
            .Select(p => p.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            MutableNode.Remove(key);
        }

        // Set new value if not null
        if (value != null)
        {
            SetProperty($"default{suffix}", value);
        }
    }
}

/// <summary>
/// FHIR StructureMapSourceListMode value set.
/// </summary>
public enum StructureMapSourceListMode
{
    [EnumLiteral("first")]
    First,

    [EnumLiteral("not_first")]
    NotFirst,

    [EnumLiteral("last")]
    Last,

    [EnumLiteral("not_last")]
    NotLast,

    [EnumLiteral("only_one")]
    OnlyOne,
}
