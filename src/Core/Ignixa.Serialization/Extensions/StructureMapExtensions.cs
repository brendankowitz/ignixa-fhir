// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Version-agnostic extension methods for StructureMap operations over the generated
/// <see cref="Ignixa.Models"/> facades. Covers elements the generator's classifier excludes from the
/// shared base because R4 and R5 genuinely diverge in shape (choice-type unions, list-vs-scalar,
/// R4-only/R5-only fields) -- callers here (the FML parser/builder) cannot reference the opt-in
/// Ignixa.Models.R4/R5 packages, so these operate directly on the underlying JSON via the same
/// low-level mechanism <c>Extension.SetValueChoiceRaw</c> uses (see
/// docs/features/typed-models/investigations/consolidate-handwritten-facades.md).
/// </summary>
public static class StructureMapExtensions
{
    /// <summary>
    /// Gets dependent variables regardless of FHIR version. Detects the wire shape actually present
    /// (rather than trusting <see cref="BaseJsonNode.FhirVersion"/> alone) since a node parsed without an
    /// explicit version still carries real R4 <c>variable</c> or R5+ <c>parameter</c> data.
    /// R4/R4B: Reads the <c>variable</c> string array.
    /// R5+: Extracts string values from the <c>parameter</c> array.
    /// </summary>
    public static IEnumerable<string> GetDependentVariables(this StructureMapGroupRuleDependent dependent)
    {
        ArgumentNullException.ThrowIfNull(dependent);

        if (dependent.MutableNode["parameter"] is JsonNode parameterNode)
        {
            return parameterNode.AsArray()
                .Select(p => new StructureMapGroupRuleTargetParameter((JsonObject)p!, dependent.FhirVersion).GetValueAs<string>())
                .Where(v => v != null)
                .Select(v => v!);
        }

        return dependent.MutableNode["variable"]?.AsArray()
            .Select(v => v?.GetValue<string>())
            .Where(v => v != null)
            .Select(v => v!)
            ?? Enumerable.Empty<string>();
    }

    /// <summary>
    /// Adds a dependent variable using version-appropriate format.
    /// R4/R4B: Appends to the <c>variable</c> string array.
    /// R5+: Appends a parameter with <c>valueString</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when FhirVersion is not set.</exception>
    public static void AddDependentVariable(this StructureMapGroupRuleDependent dependent, string variable)
    {
        ArgumentNullException.ThrowIfNull(dependent);
        ArgumentNullException.ThrowIfNull(variable);

        if (dependent.FhirVersion is not { } version)
        {
            throw new InvalidOperationException(
                "FhirVersion must be set on the StructureMap before adding dependent variables.");
        }

        if (version >= FhirVersion.R5)
        {
            var param = new JsonObject { ["valueString"] = variable };
            (dependent.MutableNode["parameter"] ??= new JsonArray()).AsArray().Add(param);
        }
        else
        {
            (dependent.MutableNode["variable"] ??= new JsonArray()).AsArray().Add(variable);
        }
    }

    /// <summary>
    /// Gets the raw node of whichever <c>defaultValue[x]</c> variant is present (R4/R4B), or the raw
    /// <c>defaultValue</c> node (R5+), or null. Callers that need to preserve the value's original type
    /// (e.g. an int stays an int) should use this over <see cref="GetDefaultValueString"/>.
    /// </summary>
    public static JsonNode? GetDefaultValue(this StructureMapGroupRuleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.FhirVersion is { } version && version >= FhirVersion.R5)
        {
            return source.MutableNode["defaultValue"];
        }

        foreach (var property in source.MutableNode)
        {
            if (property.Key.StartsWith("defaultValue", StringComparison.Ordinal))
            {
                return property.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the default value as a string regardless of version.
    /// R4/R4B: Reads whichever <c>defaultValue[x]</c> choice-type key is present.
    /// R5+: Reads <c>defaultValue</c> directly (always a plain string).
    /// </summary>
    public static string? GetDefaultValueString(this StructureMapGroupRuleSource source)
    {
        return source.GetDefaultValue() switch
        {
            JsonValue jsonValue => jsonValue.TryGetValue<string>(out var stringValue) ? stringValue : jsonValue.ToString(),
            _ => null,
        };
    }

    /// <summary>
    /// Sets the default value as a string using version-appropriate format.
    /// R4/R4B: Sets <c>defaultValueString</c>, clearing any other <c>defaultValue[x]</c> key.
    /// R5+: Sets <c>defaultValue</c> directly.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when FhirVersion is not set.</exception>
    public static void SetDefaultValueString(this StructureMapGroupRuleSource source, string? value)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.FhirVersion is not { } version)
        {
            throw new InvalidOperationException(
                "FhirVersion must be set on the StructureMap before setting a default value.");
        }

        if (version >= FhirVersion.R5)
        {
            source.SetProperty("defaultValue", value is null ? null : JsonValue.Create(value));
            return;
        }

        foreach (var key in source.MutableNode
            .Where(p => p.Key.StartsWith("defaultValue", StringComparison.Ordinal))
            .Select(p => p.Key)
            .ToList())
        {
            source.MutableNode.Remove(key);
        }

        if (value is not null)
        {
            source.SetProperty("defaultValueString", JsonValue.Create(value));
        }
    }

    /// <summary>
    /// Gets <c>target.context</c>. Present on both R4 and R5 with identical shape, but the generator's
    /// classifier keeps it off the shared base (R4 additionally carries a sibling <c>contextType</c> the
    /// classifier folds into its structural signature) -- read directly rather than requiring a
    /// version-specific subclass, since the wire shape genuinely doesn't differ.
    /// </summary>
    public static string? GetContext(this StructureMapGroupRuleTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.MutableNode["context"]?.GetValue<string>();
    }

    /// <summary>Sets <c>target.context</c>. See <see cref="GetContext"/>.</summary>
    public static void SetContext(this StructureMapGroupRuleTarget target, string? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetProperty("context", value is null ? null : JsonValue.Create(value));
    }

    /// <summary>
    /// Gets the raw node of whichever <c>value[x]</c> variant is present on a target/dependent
    /// parameter, or null. R4 supports 5 variants (id/string/boolean/integer/decimal); R5 adds 3 more
    /// (date/time/dateTime) this codebase doesn't use -- both are read identically here since the
    /// overlap is byte-for-byte the same.
    /// </summary>
    public static JsonNode? GetValue(this StructureMapGroupRuleTargetParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        foreach (var property in parameter.MutableNode)
        {
            if (property.Key.StartsWith("value", StringComparison.Ordinal))
            {
                return property.Value;
            }
        }

        return null;
    }

    /// <summary>Gets the parameter's value[x] coerced to <typeparamref name="T"/>, or default if absent/unparseable.</summary>
    public static T? GetValueAs<T>(this StructureMapGroupRuleTargetParameter parameter)
    {
        if (parameter.GetValue() is not JsonValue jsonValue)
        {
            return default;
        }

        return jsonValue.TryGetValue<T>(out var value) ? value : default;
    }

    /// <summary>
    /// Sets a parameter's <c>value[x]</c> using the given type suffix (e.g. <c>"String"</c>,
    /// <c>"Integer"</c>), clearing any other <c>value[x]</c> key first -- the same clear-then-set
    /// discipline <c>Extension.SetValueChoiceRaw</c> uses.
    /// </summary>
    public static void SetValue(this StructureMapGroupRuleTargetParameter parameter, string suffix, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(suffix);

        foreach (var key in parameter.MutableNode
            .Where(p => p.Key.StartsWith("value", StringComparison.Ordinal))
            .Select(p => p.Key)
            .ToList())
        {
            parameter.MutableNode.Remove(key);
        }

        if (value is not null)
        {
            parameter.SetProperty($"value{suffix}", value);
        }
    }

    /// <summary>Checks if constants (R5+ only) are supported for this StructureMap's FHIR version.</summary>
    public static bool SupportsConstants(this StructureMap structureMap)
    {
        ArgumentNullException.ThrowIfNull(structureMap);
        return structureMap.FhirVersion is { } version && version >= FhirVersion.R5;
    }

    /// <summary>Safely gets constants if supported, empty list otherwise (avoids a version-mismatch surprise for R4/R4B callers).</summary>
    public static IEnumerable<StructureMapConst> GetConstantsOrEmpty(this StructureMap structureMap)
    {
        ArgumentNullException.ThrowIfNull(structureMap);

        if (!structureMap.SupportsConstants())
        {
            return Enumerable.Empty<StructureMapConst>();
        }

        return structureMap.MutableNode["const"]?.AsArray()
            .Select(n => new StructureMapConst((JsonObject)n!, structureMap.FhirVersion))
            ?? Enumerable.Empty<StructureMapConst>();
    }
}
