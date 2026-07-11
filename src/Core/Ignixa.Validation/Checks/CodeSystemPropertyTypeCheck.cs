// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Enforces that a <c>CodeSystem.concept.property</c> value uses the datatype its
/// <c>CodeSystem.property</c> declaration states. When a property declared as (say) <c>dateTime</c>
/// is populated with a <c>valueBoolean</c>, the reference validator rejects it: "The property 'X'
/// has the invalid type 'boolean', when it is defined to have the type 'dateTime'." Closed-world —
/// only the resource's own property declarations and concept values are consulted; undeclared
/// properties are ignored here (they draw a separate "unknown property" warning upstream).
/// </summary>
public sealed class CodeSystemPropertyTypeCheck : IValidationCheck, ISingletonCheck, ICompatibilityConformanceCheck
{
    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (element.Meta<JsonNode>() is not JsonObject root
            || root["concept"] is not JsonArray concepts)
        {
            return ValidationResult.Success();
        }

        var declaredTypes = BuildDeclaredTypes(root["property"] as JsonArray);
        if (declaredTypes.Count == 0)
        {
            return ValidationResult.Success();
        }

        var issues = new List<ValidationIssue>();
        WalkConcepts(concepts, "CodeSystem.concept", declaredTypes, issues);

        return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
    }

    private static Dictionary<string, string> BuildDeclaredTypes(JsonArray? properties)
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        if (properties is null)
        {
            return declared;
        }

        foreach (var property in properties)
        {
            if (property is JsonObject obj
                && (obj["code"] as JsonValue)?.ToString() is { Length: > 0 } code
                && (obj["type"] as JsonValue)?.ToString() is { Length: > 0 } type)
            {
                // First declaration wins, matching the reference validator's resolution order.
                declared.TryAdd(code, type);
            }
        }

        return declared;
    }

    private static void WalkConcepts(
        JsonArray concepts,
        string path,
        Dictionary<string, string> declaredTypes,
        List<ValidationIssue> issues)
    {
        for (var i = 0; i < concepts.Count; i++)
        {
            if (concepts[i] is not JsonObject concept)
            {
                continue;
            }

            var conceptPath = $"{path}[{i}]";
            CheckConceptProperties(concept["property"] as JsonArray, conceptPath, declaredTypes, issues);

            if (concept["concept"] is JsonArray nested)
            {
                WalkConcepts(nested, $"{conceptPath}.concept", declaredTypes, issues);
            }
        }
    }

    private static void CheckConceptProperties(
        JsonArray? properties,
        string conceptPath,
        Dictionary<string, string> declaredTypes,
        List<ValidationIssue> issues)
    {
        if (properties is null)
        {
            return;
        }

        for (var i = 0; i < properties.Count; i++)
        {
            if (properties[i] is not JsonObject property
                || (property["code"] as JsonValue)?.ToString() is not { Length: > 0 } code
                || !declaredTypes.TryGetValue(code, out var declaredType)
                || ValueType(property) is not { } actualType)
            {
                continue;
            }

            if (!string.Equals(actualType, declaredType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "csp-1",
                    $"The property '{code}' has the invalid type '{actualType}', when it is defined to have the type '{declaredType}'",
                    $"{conceptPath}.property[{i}]"));
            }
        }
    }

    /// <summary>
    /// Extracts the FHIR datatype from a <c>concept.property</c>'s <c>value[x]</c> element: the type
    /// suffix of the single <c>value*</c> key, lower-cased on the first character to match FHIR type
    /// codes (e.g. <c>valueDateTime</c> =&gt; <c>dateTime</c>). Returns null when no value is present.
    /// </summary>
    private static string? ValueType(JsonObject property)
    {
        foreach (var (key, _) in property)
        {
            if (key.Length > "value".Length && key.StartsWith("value", StringComparison.Ordinal))
            {
                var suffix = key["value".Length..];
                return char.ToLowerInvariant(suffix[0]) + suffix[1..];
            }
        }

        return null;
    }
}
