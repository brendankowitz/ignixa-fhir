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
    /// Checks if two UCUM units are compatible for conversion.
    /// Units are compatible if they have the same dimensionality.
    /// </summary>
    /// <remarks>
    /// Special handling for calendar duration units: Different calendar precision units
    /// (years, months) cannot be combined with time-based units (days, hours, etc.)
    /// because calendar units have variable lengths. Per FHIRPath spec, only exact unit
    /// matches are allowed for calendar durations.
    /// </remarks>
    public bool IsCompatible(string unit1, string unit2)
    {
        ArgumentNullException.ThrowIfNull(unit1);
        ArgumentNullException.ThrowIfNull(unit2);

        // Same unit is always compatible
        if (string.Equals(unit1, unit2, StringComparison.Ordinal))
            return true;

        // Calendar duration units (year, month, week, day, hour, minute, second, millisecond)
        // can only be combined with the EXACT same unit per FHIRPath specification.
        // This is because calendar units have variable lengths or represent different time scales.
        var isCalendarDuration1 = CalendarDuration.IsCalendarDurationUnit(unit1);
        var isCalendarDuration2 = CalendarDuration.IsCalendarDurationUnit(unit2);

        // If one is a calendar duration and the other is not, they are incompatible
        if (isCalendarDuration1 != isCalendarDuration2)
            return false;

        // If both are calendar duration units but different, incompatible
        // (e.g., 1 year + 5 days, 2 hours + 3 minutes are not allowed)
        if (isCalendarDuration1 && isCalendarDuration2)
            return false; // Already checked for exact match above

        try
        {
            // Get metrics for both units
            var metric1 = _ucum.Metric(unit1);
            var metric2 = _ucum.Metric(unit2);

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
    /// Converts a value from one UCUM unit to another.
    /// </summary>
    public decimal? Convert(decimal value, string fromUnit, string toUnit)
    {
        ArgumentNullException.ThrowIfNull(fromUnit);
        ArgumentNullException.ThrowIfNull(toUnit);

        // Same unit - no conversion needed
        if (string.Equals(fromUnit, toUnit, StringComparison.Ordinal))
            return value;

        // Calendar duration units can only be combined with the exact same unit
        var isCalendarDuration1 = CalendarDuration.IsCalendarDurationUnit(fromUnit);
        var isCalendarDuration2 = CalendarDuration.IsCalendarDurationUnit(toUnit);

        // If one is a calendar duration and the other is not, incompatible
        if (isCalendarDuration1 != isCalendarDuration2)
            return null;

        // If both are calendar duration units but different, incompatible
        if (isCalendarDuration1 && isCalendarDuration2)
            return null; // Already checked for exact match above

        try
        {
            // Get metrics
            var fromMetric = _ucum.Metric(fromUnit);
            var toMetric = _ucum.Metric(toUnit);

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
