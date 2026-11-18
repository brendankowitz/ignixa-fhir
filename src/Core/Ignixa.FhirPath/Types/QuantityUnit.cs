/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * UCUM unit conversion implementation using Fhir.Metrics.
 * Provides full UCUM support for FhirPath Quantity operations.
 */

using System;
using System.Globalization;
using Fhir.Metrics;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.FhirPath.Types;

#nullable enable

/// <summary>
/// UCUM unit converter implementation using Fhir.Metrics library.
/// Supports full UCUM unit conversion and compatibility checking.
/// </summary>
public class QuantityUnitConverter : IQuantityUnitConverter
{
    /// <summary>
    /// The longest unit string, in characters, this converter hands to UCUM. A longer unit is treated as one
    /// UCUM cannot read: compatible and convertible only with the identical string.
    /// </summary>
    /// <remarks>
    /// Reading a unit costs time in proportion to the number of symbols it contains - up to about two
    /// milliseconds a symbol for a derived unit such as <c>H</c> - and the string can come from resource data,
    /// so its length is bounded before UCUM sees it. Real UCUM codes, annotations included, are far shorter.
    /// </remarks>
    public const int MaxUnitLength = 128;

    /// <summary>
    /// Distinct unit strings whose readings are kept. Readings are a pure function of the string, so dropping one
    /// costs a re-read, never a different answer.
    /// </summary>
    private const int ReadingCacheCapacity = 4096;

    private readonly SystemOfUnits _ucum;
    private readonly BoundedExpressionCache<UnitReading?> _readings = new(ReadingCacheCapacity);

    /// <summary>
    /// Initializes a new instance of the QuantityUnitConverter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UCUM.Load"/> reads the UCUM essence table, whose unit expressions carry exponents
    /// written as plain ASCII (<c>-1</c>, <c>2</c>), and parses them under
    /// <see cref="CultureInfo.CurrentCulture"/>. On a host whose negative sign is not ASCII
    /// <c>'-'</c> - ar-SA writes U+061C U+002D - that throws <see cref="FormatException"/> on
    /// <c>"-1"</c>. The table is invariant data, so it is read under the invariant culture.
    /// </para>
    /// <para>
    /// This matters more than a one-off failure because <see cref="Instance"/> is a static field: a
    /// throw here becomes a <see cref="TypeInitializationException"/> that the runtime caches, so
    /// every quantity comparison, sort, aggregate and equality in the process fails permanently
    /// afterwards, on any culture, from one unlucky first touch.
    /// </para>
    /// </remarks>
    public QuantityUnitConverter()
    {
        var ambient = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            _ucum = UCUM.Load();
        }
        finally
        {
            CultureInfo.CurrentCulture = ambient;
        }
    }

    /// <summary>
    /// Checks if two units are compatible for comparison.
    /// Fixed-duration units (wk/week, d/day, h/hour, etc.) are compatible with each other via UCUM conversion.
    /// Variable-duration units (a/year, mo/month) are ONLY compatible within their own form.
    /// </summary>
    /// <remarks>
    /// Per FHIRPath tests:
    /// - testStringQuantityWeekLiteralToQuantity: '1 \'wk\''.toQuantity() = 1 week → true (compatible)
    /// - testStringQuantityMonthLiteralToQuantity: '1 \'mo\''.toQuantity() = 1 month → empty (incompatible)
    /// - testStringQuantityYearLiteralToQuantity: '1 \'a\''.toQuantity() = 1 year → empty (incompatible)
    /// - testQuantity5: 7 days = 1 week → true (compatible via UCUM conversion)
    /// </remarks>
    public bool IsCompatible(string unit1, string unit2)
    {
        ArgumentNullException.ThrowIfNull(unit1);
        ArgumentNullException.ThrowIfNull(unit2);

        // Same unit is always compatible (exact string match)
        if (string.Equals(unit1, unit2, StringComparison.Ordinal))
            return true;

        // Check if units are calendar duration related
        var isKeyword1 = CalendarDuration.IsCalendarKeyword(unit1);
        var isKeyword2 = CalendarDuration.IsCalendarKeyword(unit2);
        var isUcumDuration1 = CalendarDuration.IsCalendarDurationUnit(unit1);
        var isUcumDuration2 = CalendarDuration.IsCalendarDurationUnit(unit2);

        // Normalize both to UCUM form for comparison
        var ucum1 = isKeyword1 ? (CalendarDuration.GetUcumUnit(unit1) ?? unit1) : unit1;
        var ucum2 = isKeyword2 ? (CalendarDuration.GetUcumUnit(unit2) ?? unit2) : unit2;

        // Check for variable-duration calendar units (year/a, month/mo)
        // These cannot be mixed between keyword and UCUM forms
        var isVariableDuration1 = ucum1 == "a" || ucum1 == "mo";
        var isVariableDuration2 = ucum2 == "a" || ucum2 == "mo";

        if (isVariableDuration1 || isVariableDuration2)
        {
            // Variable-duration units can only match exact UCUM codes
            // AND must be same form (both keywords or both UCUM)
            if (ucum1 != ucum2)
                return false; // Different units (e.g., month vs year)
            
            // Same UCUM code - check if crossing keyword↔UCUM boundary
            var isCrossing = (isKeyword1 && isUcumDuration2) || (isKeyword2 && isUcumDuration1);
            if (isCrossing)
                return false; // Cannot cross keyword↔UCUM for variable durations
            
            return true; // Same form, same unit
        }

        // For fixed-duration units and all other UCUM units, use standard UCUM compatibility: the same
        // canonical dimension.
        return Read(ucum1) is { } reading1
            && Read(ucum2) is { } reading2
            && string.Equals(reading1.Dimension, reading2.Dimension, StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts a value from one unit to another.
    /// For fixed-duration units (wk/week, d/day, etc.), allows conversion via UCUM.
    /// For variable-duration units (a/year, mo/month), only allows exact form matches.
    /// </summary>
    public decimal? Convert(decimal value, string fromUnit, string toUnit)
    {
        ArgumentNullException.ThrowIfNull(fromUnit);
        ArgumentNullException.ThrowIfNull(toUnit);

        // Same unit - no conversion needed (exact string match)
        if (string.Equals(fromUnit, toUnit, StringComparison.Ordinal))
            return value;

        // Check if units are calendar duration related
        var isKeywordFrom = CalendarDuration.IsCalendarKeyword(fromUnit);
        var isKeywordTo = CalendarDuration.IsCalendarKeyword(toUnit);

        // Normalize both to UCUM for comparison
        var ucumFrom = CalendarDuration.GetUcumUnit(fromUnit) ?? fromUnit;
        var ucumTo = CalendarDuration.GetUcumUnit(toUnit) ?? toUnit;

        // Same normalized UCUM code - 1:1 conversion (e.g., week → wk)
        if (string.Equals(ucumFrom, ucumTo, StringComparison.Ordinal))
        {
            // Variable-duration units (a, mo) can only convert if same form
            if (ucumFrom == "a" || ucumFrom == "mo")
            {
                var isUcumDurationFrom = CalendarDuration.IsCalendarDurationUnit(fromUnit);
                var isUcumDurationTo = CalendarDuration.IsCalendarDurationUnit(toUnit);
                // Both must be same form for variable durations (keyword↔keyword or UCUM↔UCUM)
                var isCrossing = (isKeywordFrom && isUcumDurationTo) || (isKeywordTo && !isKeywordFrom && CalendarDuration.IsCalendarDurationUnit(fromUnit));
                if ((isKeywordFrom || isUcumDurationFrom) && (isKeywordTo || isUcumDurationTo))
                {
                    if (isKeywordFrom != isKeywordTo)
                        return null;
                }
            }
            // For fixed-duration units, conversion is 1:1
            return value;
        }

        // Calendar-precision units (a, mo) cannot convert to DIFFERENT units
        if (ucumFrom == "a" || ucumFrom == "mo" || ucumTo == "a" || ucumTo == "mo")
            return null;

        // Fixed-length durations can be converted via UCUM
        if (Read(ucumFrom) is not { } from
            || Read(ucumTo) is not { } to
            || !string.Equals(from.Dimension, to.Dimension, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            // The source is canonicalised at its own value, not scaled from a cached one-unit reading: the two
            // differ in the decimal scale of the result, which equivalence (~) reads as precision.
            var fromMetric = _ucum.Metric(ucumFrom);
            var canonical = fromMetric is null
                ? null
                : _ucum.Canonical(new Fhir.Metrics.Quantity(new Exponential((decimal)value), fromMetric));

            if (canonical == null)
                return null;

            // (source in canonical units) / (one target unit in canonical units) = source in target units
            var ratio = canonical.Value / to.CanonicalOne.Value;

            // Exponential has explicit conversion to decimal
            return (decimal)ratio;
        }
        catch (Exception ex) when (IsUnitAlgebraFailure(ex))
        {
            // Conversion failed (incompatible units or invalid unit codes)
            return null;
        }
    }

    /// <summary>
    /// Gets the dimensionality category of a UCUM unit.
    /// </summary>
    public string? GetDimensionality(string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Read(unit)?.Dimension;
    }

    /// <summary>
    /// Reads a unit once: its canonical dimension and the canonical form of one of it, or <see langword="null"/>
    /// when UCUM cannot read it or it is longer than <see cref="MaxUnitLength"/>.
    /// </summary>
    private UnitReading? Read(string unit) =>
        unit.Length > MaxUnitLength ? null : _readings.GetOrAdd(unit, ReadUncached);

    private UnitReading? ReadUncached(string unit)
    {
        try
        {
            var metric = _ucum.Metric(unit);
            if (metric == null)
                return null;

            var canonical = _ucum.Canonical(new Fhir.Metrics.Quantity(new Exponential(1m), metric));
            return canonical?.Symbols is { } symbols ? new UnitReading(symbols, canonical) : null;
        }
        catch (Exception ex) when (IsUnitAlgebraFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Multiplies two quantities using UCUM unit algebra.
    /// </summary>
    /// <param name="left">The left quantity</param>
    /// <param name="right">The right quantity</param>
    /// <returns>The resulting quantity with combined units, or null if operation fails</returns>
    public FhirQuantity? Multiply(FhirQuantity left, FhirQuantity right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Unit.Length > MaxUnitLength || right.Unit.Length > MaxUnitLength)
            return null;

        try
        {
            // Get metrics for both units
            var leftMetric = _ucum.Metric(left.Unit);
            var rightMetric = _ucum.Metric(right.Unit);

            if (leftMetric == null || rightMetric == null)
            {
                return null;
            }

            // Create Fhir.Metrics quantities
            var leftQty = new Fhir.Metrics.Quantity(new Exponential((decimal)left.Value), leftMetric);
            var rightQty = new Fhir.Metrics.Quantity(new Exponential((decimal)right.Value), rightMetric);

            // Convert both to canonical form (base units) before multiplying
            // This handles cases like cm * m by converting cm to m first
            var leftCanonical = _ucum.Canonical(leftQty);
            var rightCanonical = _ucum.Canonical(rightQty);
            
            if (leftCanonical == null || rightCanonical == null)
                return null;

            // Multiply the canonical forms
            var result = Fhir.Metrics.Quantity.Multiply(leftCanonical, rightCanonical);

            // Extract value and unit
            var value = (decimal)result.Value;
            var unit = result.Symbols;

            // Clean up unit representation
            if (string.IsNullOrEmpty(unit))
                unit = "1";

            return new FhirQuantity(value, unit);
        }
        catch (Exception ex) when (IsUnitAlgebraFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Divides two quantities using UCUM unit algebra.
    /// </summary>
    /// <param name="left">The left quantity (numerator)</param>
    /// <param name="right">The right quantity (denominator)</param>
    /// <returns>The resulting quantity with divided units, or null if division fails</returns>
    public FhirQuantity? Divide(FhirQuantity left, FhirQuantity right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        // Division by zero check
        if (right.Value == 0)
            return null;

        if (left.Unit.Length > MaxUnitLength || right.Unit.Length > MaxUnitLength)
            return null;

        try
        {
            // Get metrics for both units
            var leftMetric = _ucum.Metric(left.Unit);
            var rightMetric = _ucum.Metric(right.Unit);

            if (leftMetric == null || rightMetric == null)
                return null;

            // Create Fhir.Metrics quantities
            var leftQty = new Fhir.Metrics.Quantity(new Exponential((decimal)left.Value), leftMetric);
            var rightQty = new Fhir.Metrics.Quantity(new Exponential((decimal)right.Value), rightMetric);

            // Convert both to canonical form (base units) before dividing
            // This handles cases like kg / g by converting both to base units
            var leftCanonical = _ucum.Canonical(leftQty);
            var rightCanonical = _ucum.Canonical(rightQty);
            
            if (leftCanonical == null || rightCanonical == null)
                return null;

            // Divide the canonical forms
            var result = Fhir.Metrics.Quantity.Divide(leftCanonical, rightCanonical);

            // Extract value and unit
            var value = (decimal)result.Value;
            var unit = result.Symbols;

            // Handle dimensionless result (same units cancel out)
            if (string.IsNullOrEmpty(unit) || result.IsDimless)
            {
                unit = "1"; // UCUM dimensionless unit
            }

            return new FhirQuantity(value, unit);
        }
        catch (Exception ex) when (IsUnitAlgebraFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// The exceptions Fhir.Metrics 1.3.1 raises for a unit it cannot parse or a value it cannot represent,
    /// each of which this converter answers with "not compatible" / <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Established from the decompiled package and by experiment, not assumed:
    /// <list type="bullet">
    /// <item><see cref="ArgumentException"/>: unknown unit or prefix (<c>xyz</c>, <c>[foo]</c>), whitespace
    /// (<c>m s</c>), malformed annotation (<c>m{a}b</c>).</item>
    /// <item><see cref="InvalidCastException"/>: a unit with no path to base units (<c>Cel</c>, <c>[degF]</c>,
    /// <c>[pH]</c>), from <c>Conversions.Canonical</c>.</item>
    /// <item><see cref="FormatException"/>: an exponent written in non-ASCII digits (<c>m&#x0662;</c>), which the
    /// tokenizer's <c>\d</c> accepts and <c>Convert.ToInt16</c> then rejects.</item>
    /// <item><see cref="OverflowException"/>: an exponent beyond <see cref="short"/> (<c>m40000</c>), or a magnitude
    /// beyond <see cref="decimal"/> (<c>km30000</c>, or multiplying two large canonical values).</item>
    /// </list>
    /// <see cref="ArgumentNullException"/> and <see cref="ArgumentOutOfRangeException"/> are excluded: the package
    /// throws neither for a bad unit (no explicit throw in the decompiled source, and none in 20,000 fuzzed
    /// unit strings), so either one here is a defect. Anything else - cancellation, memory or stack
    /// exhaustion, a defect - propagates too.
    /// </remarks>
    private static bool IsUnitAlgebraFailure(Exception ex) =>
        ex is (ArgumentException and not (ArgumentNullException or ArgumentOutOfRangeException))
            or InvalidCastException or FormatException or OverflowException;

    /// <summary>
    /// Singleton instance for reuse across FhirPath evaluations.
    /// </summary>
    public static QuantityUnitConverter Instance { get; } = new QuantityUnitConverter();
}
