// <copyright file="FhirPrimitiveValidator.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Validation.Checks;

/// <summary>
/// FHIR-strict primitive value validation shared across checks.
/// Validates that a parsed element carries a JSON value whose <em>kind</em> and
/// <em>value rules</em> match the declared FHIR primitive type, using the raw
/// <see cref="JsonNode"/> so System.Text.Json coercions cannot mask a type mismatch.
/// </summary>
/// <remarks>
/// Examples of values this rejects (which loose parsing would accept):
/// boolean given as <c>0</c>/<c>1</c>/<c>"true"</c>; integer given as <c>3.1</c>,
/// <c>"42"</c>, out-of-int32-range, or <c>true</c>; unsignedInt given as <c>-1</c>.
/// </remarks>
public static class FhirPrimitiveValidator
{
    /// <summary>
    /// FHIR primitive type names. Anything not in this set is a complex type and is
    /// validated structurally elsewhere (nested-type checks), not here.
    /// </summary>
    private static readonly HashSet<string> PrimitiveTypes = new(StringComparer.Ordinal)
    {
        "boolean", "integer", "integer64", "string", "decimal",
        "uri", "url", "canonical", "base64Binary", "instant",
        "date", "dateTime", "time", "code", "oid", "id",
        "markdown", "unsignedInt", "positiveInt", "uuid",
    };

    // FHIR R4 primitive format regexes (with month/day/time range constraints, so
    // "2000-13", "2000-00", "0000", "201" are all rejected, unlike a loose \d{4} pattern).
    private static readonly Regex DateRegex = new(
        @"^([0-9]([0-9]([0-9][1-9]|[1-9]0)|[1-9]00)|[1-9]000)(-(0[1-9]|1[0-2])(-(0[1-9]|[1-2][0-9]|3[0-1]))?)?$",
        RegexOptions.Compiled);

    private static readonly Regex DateTimeRegex = new(
        @"^([0-9]([0-9]([0-9][1-9]|[1-9]0)|[1-9]00)|[1-9]000)(-(0[1-9]|1[0-2])(-(0[1-9]|[1-2][0-9]|3[0-1])(T([01][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)(\.[0-9]{1,9})?(Z|(\+|-)((0[0-9]|1[0-3]):[0-5][0-9]|14:00)))?)?)?$",
        RegexOptions.Compiled);

    private static readonly Regex TimeRegex = new(
        @"^([01][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)(\.[0-9]{1,9})?$",
        RegexOptions.Compiled);

    private static readonly Regex InstantRegex = new(
        @"^([0-9]([0-9]([0-9][1-9]|[1-9]0)|[1-9]00)|[1-9]000)-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])T([01][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)(\.[0-9]{1,9})?(Z|(\+|-)((0[0-9]|1[0-3]):[0-5][0-9]|14:00))$",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns true if <paramref name="fhirType"/> is a FHIR primitive type.
    /// </summary>
    public static bool IsPrimitiveType(string fhirType) => PrimitiveTypes.Contains(fhirType);

    /// <summary>
    /// Validates the primitive value carried by <paramref name="element"/> against
    /// <paramref name="fhirType"/>. Non-primitive types and absent/null values return
    /// success (shape/cardinality are enforced by other checks).
    /// </summary>
    /// <param name="element">The element carrying the primitive value.</param>
    /// <param name="fhirType">The declared FHIR primitive type (e.g. "boolean", "integer").</param>
    /// <param name="reason">On failure, a human-readable explanation.</param>
    /// <returns>True if valid (or not applicable); false if the value violates the type rules.</returns>
    public static bool TryValidate(IElement element, string fhirType, out string? reason)
        => TryValidate(element, fhirType, enforceStrictDateFormat: true, out reason);

    /// <summary>
    /// Validates the primitive value, with control over the strict date-family <em>format</em> regex.
    /// </summary>
    /// <param name="element">The element carrying the primitive value.</param>
    /// <param name="fhirType">The declared FHIR primitive type.</param>
    /// <param name="enforceStrictDateFormat">
    /// When false, the strict date/dateTime/time/instant <em>format</em> regex is skipped while the
    /// depth-independent rules (empty-string rejection, calendar-date validity, numeric kind/range)
    /// still apply. Lets a caller that already applies its own depth-graded date format (e.g.
    /// <see cref="TypeCheck"/>'s dateTime leniency, bug #210-4) add the always-invalid checks without
    /// re-imposing strict format.
    /// </param>
    /// <param name="reason">On failure, a human-readable explanation.</param>
    /// <returns>True if valid (or not applicable); false if the value violates the type rules.</returns>
    public static bool TryValidate(IElement element, string fhirType, bool enforceStrictDateFormat, out string? reason)
    {
        reason = null;
        if (!PrimitiveTypes.Contains(fhirType))
        {
            return true;
        }

        var node = element.Meta<JsonNode>();
        if (node is null)
        {
            // No raw JSON available (e.g. synthetic element); fall back to permissive.
            return true;
        }

        // When a primitive carries BOTH a value and a "_value" shadow (extensions/id),
        // Meta<JsonNode>() returns the SHADOW object, not the value. Inspecting the shadow's
        // kind would spuriously reject a valid resource (object where a string/number/bool is
        // expected). Re-bind to the actual primitive VALUE node so we validate the value's
        // real JSON kind and rules (this still rejects a malformed value such as 0 for boolean).
        if (element.HasPrimitiveValue
            && node.GetValueKind() is JsonValueKind.Object
            && element.Meta<JsonPrimitiveValueNode>() is { Value: var valueNode })
        {
            node = valueNode;
        }

        var kind = node.GetValueKind();

        // A primitive element may legitimately be an object that carries ONLY the
        // shadow ("_value") extension/id content with no primitive value of its own.
        if (kind is JsonValueKind.Object && !element.HasPrimitiveValue)
        {
            return true;
        }

        switch (fhirType)
        {
            case "boolean":
                if (kind is JsonValueKind.True or JsonValueKind.False)
                {
                    return true;
                }
                reason = $"expected a JSON boolean (true/false) for FHIR type 'boolean', but got {Describe(kind, node)}";
                return false;

            case "integer":
            case "unsignedInt":
            case "positiveInt":
            case "integer64":
                return ValidateInteger(node, kind, fhirType, out reason);

            case "decimal":
                if (kind is JsonValueKind.Number)
                {
                    return true;
                }
                reason = $"expected a JSON number for FHIR type 'decimal', but got {Describe(kind, node)}";
                return false;

            default:
                // All remaining primitives are JSON strings in FHIR JSON.
                if (kind is not JsonValueKind.String)
                {
                    reason = $"expected a JSON string for FHIR type '{fhirType}', but got {Describe(kind, node)}";
                    return false;
                }

                var text = node.GetValue<string>();

                // Every FHIR string-family primitive requires at least one character;
                // the empty string violates the base 'string' regex (and code/uri/id/...).
                if (text.Length == 0)
                {
                    reason = $"value must not be empty for FHIR type '{fhirType}'";
                    return false;
                }

                return ValidateStringFormat(text, fhirType, enforceStrictDateFormat, out reason);
        }
    }

    private static bool ValidateStringFormat(string text, string fhirType, bool enforceStrictDateFormat, out string? reason)
    {
        reason = null;
        var pattern = fhirType switch
        {
            "date" => DateRegex,
            "dateTime" => DateTimeRegex,
            "time" => TimeRegex,
            "instant" => InstantRegex,
            _ => null,
        };

        if (enforceStrictDateFormat && pattern is not null && !pattern.IsMatch(text))
        {
            reason = $"value '{text}' is not a valid FHIR {fhirType}";
            return false;
        }

        if (fhirType is "date" or "dateTime" or "instant" && !IsCalendarDateValid(text, out reason))
        {
            return false;
        }

        return true;
    }

    private static bool IsCalendarDateValid(string text, out string? reason)
    {
        reason = null;

        // Values shorter than 10 chars are year-only or year-month — no day to validate.
        if (text.Length < 10 || text[7] != '-')
        {
            return true;
        }

        // Parse just the leading yyyy-MM-dd; DateOnly rejects impossible dates like Feb 31.
        // InvariantCulture/None so a non-Gregorian default culture can't mis-parse a FHIR date.
        if (!DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            reason = $"value '{text}' contains an invalid calendar date";
            return false;
        }

        return true;
    }

    private static bool ValidateInteger(JsonNode node, JsonValueKind kind, string fhirType, out string? reason)
    {
        reason = null;
        if (kind is not JsonValueKind.Number)
        {
            reason = $"expected a JSON integer for FHIR type '{fhirType}', but got {Describe(kind, node)}";
            return false;
        }

        // Read the numeric token from its canonical JSON text so the result is independent of
        // the JsonValue's CLR backing. TryGetValue<long> only widens for JsonElement-backed
        // (parsed) nodes, so a programmatically-built JsonValue<int> would otherwise be misread
        // as having a fractional part.
        var raw = node.ToJsonString();
        if (raw.Contains('.', StringComparison.Ordinal)
            || raw.Contains('e', StringComparison.Ordinal)
            || raw.Contains('E', StringComparison.Ordinal))
        {
            reason = $"expected a whole number for FHIR type '{fhirType}', but the value has a fractional or exponent part";
            return false;
        }

        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            reason = $"value '{raw}' is not a valid integer for FHIR type '{fhirType}'";
            return false;
        }

        // FHIR integer/unsignedInt/positiveInt are 32-bit; integer64 is 64-bit.
        if (fhirType != "integer64" && (value < int.MinValue || value > int.MaxValue))
        {
            reason = $"value {value} is outside the 32-bit range allowed for FHIR type '{fhirType}'";
            return false;
        }

        if (fhirType == "unsignedInt" && value < 0)
        {
            reason = $"value {value} is negative but FHIR type 'unsignedInt' requires a value >= 0";
            return false;
        }

        if (fhirType == "positiveInt" && value < 1)
        {
            reason = $"value {value} is not positive but FHIR type 'positiveInt' requires a value >= 1";
            return false;
        }

        return true;
    }

    private static string Describe(JsonValueKind kind, JsonNode node) =>
        kind switch
        {
            JsonValueKind.Number => $"the number {node.ToJsonString()}",
            JsonValueKind.String => "a string",
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            JsonValueKind.Object => "an object",
            JsonValueKind.Array => "an array",
            _ => kind.ToString(),
        };
}
