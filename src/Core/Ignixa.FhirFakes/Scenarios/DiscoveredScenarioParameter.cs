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
    public bool TryParseValue(string rawValue, out object? value)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        var underlyingType = Nullable.GetUnderlyingType(Type) ?? Type;

        if (underlyingType == typeof(int) && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            if (!IsWithinRange(intValue))
            {
                value = null;
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

        if (underlyingType.IsEnum
            && (rawValue.Length == 0 || !char.IsDigit(rawValue[0]))
            && Enum.TryParse(underlyingType, rawValue, ignoreCase: true, out var enumValue)
            && Enum.IsDefined(underlyingType, enumValue!))
        {
            value = enumValue;
            return true;
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
}
