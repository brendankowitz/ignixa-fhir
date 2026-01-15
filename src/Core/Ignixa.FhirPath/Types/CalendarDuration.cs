/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Calendar duration support for FhirPath.
 * Handles calendar-based time units (year, month, day, hour, etc.).
 */

using System;
using System.Collections.Generic;

namespace Ignixa.FhirPath.Types;

#nullable enable

/// <summary>
/// Provides support for FhirPath calendar duration keywords.
/// Converts calendar keywords (year, month, day, etc.) into Quantity objects with UCUM units.
/// </summary>
public static class CalendarDuration
{
    /// <summary>
    /// Mapping of FhirPath calendar keywords to UCUM units.
    /// Reference: FHIRPath specification section on calendar durations.
    /// </summary>
    private static readonly Dictionary<string, string> CalendarKeywordToUcum = new(StringComparer.OrdinalIgnoreCase)
    {
        // Singular forms
        { "year", "a" },        // annum (year)
        { "month", "mo" },      // month
        { "week", "wk" },       // week
        { "day", "d" },         // day
        { "hour", "h" },        // hour
        { "minute", "min" },    // minute
        { "second", "s" },      // second
        { "millisecond", "ms" }, // millisecond

        // Plural forms
        { "years", "a" },
        { "months", "mo" },
        { "weeks", "wk" },
        { "days", "d" },
        { "hours", "h" },
        { "minutes", "min" },
        { "seconds", "s" },
        { "milliseconds", "ms" }
    };

    /// <summary>
    /// Set of calendar duration unit names for fast lookup.
    /// </summary>
    private static readonly HashSet<string> CalendarKeywords = new(
        CalendarKeywordToUcum.Keys,
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a string is a recognized calendar duration keyword.
    /// </summary>
    /// <param name="keyword">The keyword to check (e.g., "year", "days")</param>
    /// <returns>True if the keyword is a calendar duration</returns>
    public static bool IsCalendarKeyword(string keyword)
    {
        if (string.IsNullOrEmpty(keyword))
            return false;

        return CalendarKeywords.Contains(keyword);
    }

    /// <summary>
    /// Parses a calendar duration keyword into a Quantity.
    /// </summary>
    /// <param name="value">The numeric value</param>
    /// <param name="keyword">The calendar keyword (e.g., "year", "days")</param>
    /// <returns>A Quantity with the appropriate UCUM unit, or null if keyword is not recognized</returns>
    public static Quantity? Parse(decimal value, string keyword)
    {
        if (string.IsNullOrEmpty(keyword))
            return null;

        if (!CalendarKeywordToUcum.TryGetValue(keyword, out var ucumUnit))
            return null;

        return new Quantity(value, ucumUnit);
    }

    /// <summary>
    /// Gets the UCUM unit code for a calendar duration keyword.
    /// </summary>
    /// <param name="keyword">The calendar keyword (e.g., "year", "days")</param>
    /// <returns>The UCUM unit code, or null if not recognized</returns>
    public static string? GetUcumUnit(string keyword)
    {
        if (string.IsNullOrEmpty(keyword))
            return null;

        return CalendarKeywordToUcum.TryGetValue(keyword, out var unit) ? unit : null;
    }

    /// <summary>
    /// Normalizes a unit to its UCUM form if it's a calendar keyword.
    /// Returns the original unit if it's already UCUM or unknown.
    /// </summary>
    /// <param name="unit">The unit (keyword or UCUM form)</param>
    /// <returns>The normalized UCUM unit code</returns>
    public static string NormalizeToUcum(string unit)
    {
        if (string.IsNullOrEmpty(unit))
            return unit;

        // Try to convert from keyword to UCUM
        var ucum = GetUcumUnit(unit);
        return ucum ?? unit; // Return original if not a keyword
    }

    /// <summary>
    /// Gets the canonical keyword for a UCUM unit (reverse lookup).
    /// </summary>
    /// <param name="ucumUnit">The UCUM unit (e.g., "wk", "a")</param>
    /// <returns>The canonical keyword (e.g., "week", "year"), or null if not a calendar duration</returns>
    public static string? GetKeywordFromUcum(string ucumUnit)
    {
        if (string.IsNullOrEmpty(ucumUnit))
            return null;

        return ucumUnit switch
        {
            "a" => "year",
            "mo" => "month",
            "wk" => "week",
            "d" => "day",
            "h" => "hour",
            "min" => "minute",
            "s" => "second",
            "ms" => "millisecond",
            _ => null
        };
    }

    /// <summary>
    /// Gets all supported calendar duration keywords.
    /// </summary>
    public static IReadOnlyCollection<string> SupportedKeywords => CalendarKeywords;

    /// <summary>
    /// Determines if two calendar durations are compatible for arithmetic.
    /// </summary>
    /// <param name="keyword1">First calendar keyword</param>
    /// <param name="keyword2">Second calendar keyword</param>
    /// <returns>True if the durations can be combined</returns>
    /// <remarks>
    /// Calendar durations follow FHIRPath rules:
    /// - Same units can be combined (e.g., days + days)
    /// - Different calendar units generally cannot be combined without context
    ///   (e.g., months + days is ambiguous because months have variable lengths)
    /// </remarks>
    public static bool AreCompatible(string keyword1, string keyword2)
    {
        if (string.IsNullOrEmpty(keyword1) || string.IsNullOrEmpty(keyword2))
            return false;

        var unit1 = GetUcumUnit(keyword1);
        var unit2 = GetUcumUnit(keyword2);

        if (unit1 == null || unit2 == null)
            return false;

        // For calendar durations, only exact unit matches are compatible
        // because calendar units have variable lengths (e.g., months can be 28-31 days)
        return unit1.Equals(unit2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a calendar keyword to its canonical singular form.
    /// </summary>
    /// <param name="keyword">The calendar keyword</param>
    /// <returns>The normalized singular form, or null if not recognized</returns>
    public static string? NormalizeKeyword(string keyword)
    {
        if (string.IsNullOrEmpty(keyword))
            return null;

        if (!CalendarKeywordToUcum.TryGetValue(keyword, out var ucumUnit))
            return null;

        // Return the singular form for each UCUM unit
        return ucumUnit switch
        {
            "a" => "year",
            "mo" => "month",
            "wk" => "week",
            "d" => "day",
            "h" => "hour",
            "min" => "minute",
            "s" => "second",
            "ms" => "millisecond",
            _ => null
        };
    }

    /// <summary>
    /// Checks if a unit is a calendar duration unit (any time unit from FHIRPath keywords).
    /// Calendar duration units cannot be mixed with different units in arithmetic operations.
    /// </summary>
    /// <param name="ucumUnit">The UCUM unit code</param>
    /// <returns>True if the unit is a calendar duration unit</returns>
    /// <remarks>
    /// Per FHIRPath spec, calendar duration keywords (year, month, week, day, hour, minute,
    /// second, millisecond) can only be combined with the EXACT same unit. This is because:
    /// 1. Years and months have variable lengths (28-31 days)
    /// 2. Different time scales should not be mixed without explicit context
    /// </remarks>
    public static bool IsCalendarDurationUnit(string ucumUnit)
    {
        if (string.IsNullOrEmpty(ucumUnit))
            return false;

        // All calendar duration keyword units
        return ucumUnit switch
        {
            "a" or "mo" or "wk" or "d" or "h" or "min" or "s" or "ms" => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a unit is a calendar-precision unit (years, months).
    /// These units have variable lengths and require special handling.
    /// </summary>
    /// <param name="ucumUnit">The UCUM unit code</param>
    /// <returns>True if the unit is calendar-precision</returns>
    public static bool IsCalendarPrecisionUnit(string ucumUnit)
    {
        if (string.IsNullOrEmpty(ucumUnit))
            return false;

        // Years and months have variable lengths depending on the calendar
        return ucumUnit switch
        {
            "a" or "mo" => true, // year or month
            _ => false
        };
    }

    /// <summary>
    /// Checks if a unit is a time-based duration (fixed-length).
    /// </summary>
    /// <param name="ucumUnit">The UCUM unit code</param>
    /// <returns>True if the unit is a fixed-length time duration</returns>
    public static bool IsFixedLengthDuration(string ucumUnit)
    {
        if (string.IsNullOrEmpty(ucumUnit))
            return false;

        // Fixed-length units can be reliably converted
        return ucumUnit switch
        {
            "wk" or "d" or "h" or "min" or "s" or "ms" => true,
            _ => false
        };
    }
}
