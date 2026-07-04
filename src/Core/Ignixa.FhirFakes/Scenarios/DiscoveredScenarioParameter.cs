// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Metadata describing one parameter of a <see cref="DiscoveredScenario"/> factory method, as produced
/// by <see cref="ScenarioCatalog"/>.
/// </summary>
public sealed class DiscoveredScenarioParameter
{
    /// <summary>
    /// The parameter name, matching the factory method's parameter name exactly (used as the key for
    /// <see cref="ScenarioCatalog.Invoke"/> parameter overrides).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The parameter's CLR type.
    /// </summary>
    public required Type Type { get; init; }

    /// <summary>
    /// The parameter's own default value, if it has one. Null when <see cref="HasDefaultValue"/> is false.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// True if the factory method parameter declares a default value.
    /// </summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>
    /// Minimum value hint from <see cref="ScenarioParameterAttribute.Min"/>, or null if unset/unannotated.
    /// </summary>
    public double? Min { get; init; }

    /// <summary>
    /// Maximum value hint from <see cref="ScenarioParameterAttribute.Max"/>, or null if unset/unannotated.
    /// </summary>
    public double? Max { get; init; }

    /// <summary>
    /// One-line description from <see cref="ScenarioParameterAttribute.Description"/>, or null if unset.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Parses a raw string (e.g. a CLI or form value) into this parameter's CLR type using
    /// invariant culture. Supports int, decimal, bool, string, enums, and their nullable forms.
    /// </summary>
    public bool TryParseValue(string rawValue, out object? value) =>
        TryParseValue(rawValue, out value, out _);

    /// <summary>
    /// Parses a raw string (e.g. a CLI or form value) into this parameter's CLR type using
    /// invariant culture, same as <see cref="TryParseValue(string, out object?)"/>, but also reports
    /// an actionable reason on failure (e.g. "out of range [18, 85]") instead of a generic
    /// couldn't-convert message — the type conversion itself may have succeeded even though this
    /// method returns false, so a caller shouldn't assume every failure is a type mismatch.
    /// </summary>
    public bool TryParseValue(string rawValue, out object? value, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        var underlyingType = Nullable.GetUnderlyingType(Type) ?? Type;
        failureReason = null;

        if (underlyingType == typeof(int) && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            if (!IsWithinRange(intValue))
            {
                value = null;
                failureReason = RangeFailureReason(intValue);
                return false;
            }

            value = intValue;
            return true;
        }

        if (underlyingType == typeof(decimal) && decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            // Casting to double can lose precision at extreme magnitudes; acceptable here because
            // scenario Min/Max hints describe small, human-scale values (age, severity), not
            // high-precision decimals.
            if (!IsWithinRange((double)decimalValue))
            {
                value = null;
                failureReason = RangeFailureReason((double)decimalValue);
                return false;
            }

            value = decimalValue;
            return true;
        }

        if (underlyingType == typeof(bool) && bool.TryParse(rawValue, out var boolValue))
        {
            value = boolValue;
            return true;
        }

        if (underlyingType.IsEnum)
        {
            // Trim before the digit check, not just rawValue[0] — Enum.TryParse itself trims
            // leading/trailing whitespace, so an untrimmed check here would let " 1" slip past
            // as a raw ordinal (caught only if that ordinal happens to be undefined).
            var trimmedForDigitCheck = rawValue.Trim();
            if ((trimmedForDigitCheck.Length == 0 || !char.IsDigit(trimmedForDigitCheck[0]))
                && Enum.TryParse(underlyingType, rawValue, ignoreCase: true, out var enumValue)
                && Enum.IsDefined(underlyingType, enumValue!))
            {
                value = enumValue;
                return true;
            }

            value = null;
            failureReason = $"'{rawValue}' is not a recognized {underlyingType.Name} value. Expected one of: " +
                $"{string.Join(", ", Enum.GetNames(underlyingType))}.";
            return false;
        }

        if (underlyingType == typeof(string))
        {
            value = rawValue;
            return true;
        }

        value = null;
        return false;
    }

    private bool IsWithinRange(double numericValue) =>
        (Min is null || numericValue >= Min.Value) && (Max is null || numericValue <= Max.Value);

    private string RangeFailureReason(double numericValue) =>
        $"'{numericValue.ToString(CultureInfo.InvariantCulture)}' is outside the allowed range " +
        $"[{Min?.ToString(CultureInfo.InvariantCulture) ?? "-∞"}, {Max?.ToString(CultureInfo.InvariantCulture) ?? "∞"}].";
}
