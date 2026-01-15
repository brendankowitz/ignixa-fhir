/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * UCUM unit conversion implementation using Fhir.Metrics.
 * Provides full UCUM support for FhirPath Quantity operations.
 */

using System;
using Fhir.Metrics;

namespace Ignixa.FhirPath.Types;

#nullable enable

/// <summary>
/// UCUM unit converter implementation using Fhir.Metrics library.
/// Supports full UCUM unit conversion and compatibility checking.
/// </summary>
public class QuantityUnitConverter : IQuantityUnitConverter
{
    private readonly SystemOfUnits _ucum;

    /// <summary>
    /// Initializes a new instance of the QuantityUnitConverter.
    /// </summary>
    public QuantityUnitConverter()
    {
        _ucum = UCUM.Load();
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

        // For fixed-duration units and all other UCUM units, use standard UCUM compatibility
        try
        {
            // Get metrics for both units
            var metric1 = _ucum.Metric(ucum1);
            var metric2 = _ucum.Metric(ucum2);

            if (metric1 == null || metric2 == null)
                return false;

            // Create test quantities and convert to canonical form
            var q1 = new Fhir.Metrics.Quantity(1, metric1);
            var q2 = new Fhir.Metrics.Quantity(1, metric2);

            var canonical1 = _ucum.Canonical(q1);
            var canonical2 = _ucum.Canonical(q2);

            if (canonical1 == null || canonical2 == null)
                return false;

            // Check if both have the same canonical form metric (same dimensionality)
            return AreSameDimension(canonical1, canonical2);
        }
        catch
        {
            // If parsing or conversion fails, units are not compatible
            return false;
        }
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
        try
        {
            // Get metrics
            var fromMetric = _ucum.Metric(ucumFrom);
            var toMetric = _ucum.Metric(ucumTo);

            if (fromMetric == null || toMetric == null)
                return null;

            // Create source quantity using Exponential implicit conversion from decimal
            var sourceValue = new Exponential((decimal)value);
            var sourceQty = new Fhir.Metrics.Quantity(sourceValue, fromMetric);

            // Convert to canonical form
            var canonical = _ucum.Canonical(sourceQty);
            if (canonical == null)
                return null;

            // Create target quantity in canonical form and convert back
            var targetValue = new Exponential(1m);
            var targetCanonical = new Fhir.Metrics.Quantity(targetValue, toMetric);
            var targetInCanonical = _ucum.Canonical(targetCanonical);

            if (targetInCanonical == null)
                return null;

            // Check dimensions match
            if (!AreSameDimension(canonical, targetInCanonical))
                return null;

            // Calculate conversion factor
            // canonical value is in base units
            // We need: (source_canonical / target_canonical_for_unit_1) = result_in_target_units
            var ratio = canonical.Value / targetInCanonical.Value;

            // Exponential has explicit conversion to decimal
            return (decimal)ratio;
        }
        catch
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

        try
        {
            var metric = _ucum.Metric(unit);
            if (metric == null)
                return null;

            // Get canonical form to determine dimension
            var quantity = new Fhir.Metrics.Quantity(1, metric);
            var canonical = _ucum.Canonical(quantity);

            return canonical?.Symbols;
        }
        catch
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
    public Quantity? Multiply(Quantity left, Quantity right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

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

            return new Quantity(value, unit);
        }
        catch
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
    public Quantity? Divide(Quantity left, Quantity right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        // Division by zero check
        if (right.Value == 0)
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

            return new Quantity(value, unit);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if two quantities have the same dimensionality.
    /// </summary>
    private bool AreSameDimension(Fhir.Metrics.Quantity q1, Fhir.Metrics.Quantity q2)
    {
        // Two quantities have the same dimension if their canonical symbols match
        return string.Equals(q1.Symbols, q2.Symbols, StringComparison.Ordinal);
    }

    /// <summary>
    /// Singleton instance for reuse across FhirPath evaluations.
    /// </summary>
    public static QuantityUnitConverter Instance { get; } = new QuantityUnitConverter();
}
