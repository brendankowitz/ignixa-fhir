// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
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
    public StructureMapSourceJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
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
    /// Gets the default value as a string (R5+ only).
    /// In R5+, defaultValue is always a simple string FHIRPath expression.
    /// For R4/R4B, use GetDefaultValue() to access typed defaultValue[x].
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when accessed in FHIR versions prior to R5.</exception>
    [JsonIgnore]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1721:Property names should not match get methods", Justification = "DefaultValue property is R5+ only; GetDefaultValue() method is R4/R4B only. Version-specific by design.")]
    public string? DefaultValue
    {
        get
        {
            if (FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"DefaultValue (string) is not supported in {FhirVersion}. In R4/R4B, use GetDefaultValue() or SetDefaultValue(suffix, value) for typed defaultValue[x] properties.");
            }
            return GetProperty<string>("defaultValue");
        }
        set
        {
            if (FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"DefaultValue (string) is not supported in {FhirVersion}. In R4/R4B, use SetDefaultValue(suffix, value) for typed defaultValue[x] properties.");
            }
            SetProperty("defaultValue", value);
        }
    }

    /// <summary>
    /// Gets the default value (R4/R4B choice type - can be any of 23 FHIR datatypes).
    /// Returns the JsonNode for the first defaultValue[x] property found.
    /// In R5+, this returns null (use DefaultValue property instead).
    /// </summary>
    /// <remarks>
    /// R4/R4B supports: defaultValueBase64Binary, defaultValueBoolean, defaultValueCanonical,
    /// defaultValueCode, defaultValueDate, defaultValueDateTime, defaultValueDecimal,
    /// defaultValueId, defaultValueInstant, defaultValueInteger, defaultValueMarkdown,
    /// defaultValueOid, defaultValuePositiveInt, defaultValueString, defaultValueTime,
    /// defaultValueUnsignedInt, defaultValueUri, defaultValueUrl, defaultValueUuid,
    /// defaultValueAddress, defaultValueAge, defaultValueAnnotation, defaultValueAttachment, etc.
    /// </remarks>
    public JsonNode? GetDefaultValue()
    {
        if (FhirVersion.HasValue && FhirVersion >= Ignixa.Abstractions.FhirVersion.R5)
        {
            // R5: defaultValue is just a string, not a choice type
            var stringValue = GetProperty<string>("defaultValue");
            return stringValue != null ? JsonValue.Create(stringValue) : null;
        }

        // R4/R4B: search for defaultValue[x]
        foreach (var property in MutableNode)
        {
            if (property.Key.StartsWith("defaultValue", StringComparison.Ordinal))
            {
                return property.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Sets a default value with the specified type suffix (R4/R4B only).
    /// Examples: SetDefaultValue("String", "value"), SetDefaultValue("Integer", 42).
    /// In R5+, use DefaultValue property instead.
    /// </summary>
    /// <param name="suffix">Type suffix (e.g., "String", "Integer", "Boolean").</param>
    /// <param name="value">The typed value to set.</param>
    /// <exception cref="NotSupportedException">Thrown when called in FHIR R5 or later.</exception>
    public void SetDefaultValue(string suffix, JsonNode? value)
    {
        if (FhirVersion.HasValue && FhirVersion >= Ignixa.Abstractions.FhirVersion.R5)
        {
            throw new NotSupportedException(
                $"SetDefaultValue(suffix, value) is not supported in {FhirVersion}. In R5+, use the DefaultValue property (string) instead.");
        }

        // Remove any existing defaultValue[x] properties
        var keysToRemove = MutableNode.Where(p => p.Key.StartsWith("defaultValue", StringComparison.Ordinal))
            .Select(p => p.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            MutableNode.Remove(key);
        }

        // Set new value if not null
        if (value != null)
        {
            SetProperty($"defaultValue{suffix}", value);
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
