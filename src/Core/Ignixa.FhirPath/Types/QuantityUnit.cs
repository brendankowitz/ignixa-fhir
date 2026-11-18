/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * UCUM unit conversion implementation using Fhir.Metrics.
 * Provides full UCUM support for FhirPath Quantity operations.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using Fhir.Metrics;
using Ignixa.Abstractions;

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

            // Exponential has an explicit conversion to decimal, but it scales with (decimal)Math.Pow, which
            // is inexact on .NET 11 (see DecimalConversion).
            if (TryGetExactFactor(fromMetric, toMetric, divide: true, out var numerator, out var denominator))
            {
                return PreferExact(ratio, sourceValue * numerator / denominator);
            }

            return DecimalConversion.ToDecimal(ratio);
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
    public FhirQuantity? Multiply(FhirQuantity left, FhirQuantity right)
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
            var value = TryGetExactFactor(leftMetric, rightMetric, divide: false, out var numerator, out var denominator)
                ? PreferExact(result.Value, leftQty.Value * rightQty.Value * numerator / denominator)
                : DecimalConversion.ToDecimal(result.Value);

            var unit = result.Symbols;

            // Clean up unit representation
            if (string.IsNullOrEmpty(unit))
                unit = "1";

            return new FhirQuantity(value, unit);
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
    public FhirQuantity? Divide(FhirQuantity left, FhirQuantity right)
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
            var value = TryGetExactFactor(leftMetric, rightMetric, divide: true, out var numerator, out var denominator)
                ? PreferExact(result.Value, leftQty.Value * numerator / (rightQty.Value * denominator))
                : DecimalConversion.ToDecimal(result.Value);

            var unit = result.Symbols;

            // Handle dimensionless result (same units cancel out)
            if (string.IsNullOrEmpty(unit) || result.IsDimless)
            {
                unit = "1"; // UCUM dimensionless unit
            }

            return new FhirQuantity(value, unit);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// On runtimes where Fhir.Metrics computes unit factors inexactly (.NET 11+), computes the exact factor that takes
    /// the combined operand values of an operation to the result unit: the ratio (<paramref name="divide"/>) or the
    /// product of the factors of the two units, as <paramref name="numerator"/> / <paramref name="denominator"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fhir.Metrics raises unit and prefix factors to their exponent with <c>Exponential.Power</c>, which goes through
    /// <c>(decimal)Math.Pow</c>. On .NET 11 that cast is correctly rounded and keeps the binary error of the double
    /// (up to a few 1e-15 relative for compound units), so the canonical factors of equivalent units no longer cancel:
    /// <c>1 'mg/kg/min'</c> converts to <c>1000.000000000000066 'ug/kg/min'</c> and is not equal to
    /// <c>1000 'ug/kg/min'</c>.
    /// </para>
    /// <para>
    /// The factors themselves do not go through <c>Math.Pow</c>: each prefix factor, and the value of one unit in the
    /// units of its UCUM definition, which Fhir.Metrics evaluates with decimal multiplication and division only. This
    /// method walks both unit expressions down to base units, collects every such factor with its net exponent, so that
    /// factors shared by both sides (the <c>min</c> and <c>kg</c> of <c>mg/kg/min</c> and <c>ug/kg/min</c>) cancel
    /// exactly instead of numerically, and multiplies what remains with decimal arithmetic. The caller applies the
    /// factor with a single final division, so the quotient of <c>1 'm' / 3 'm'</c> keeps all 28 digits. Factors that
    /// remain after cancellation are genuine, e.g. the 3.6 of <c>km/h</c> to <c>m/s</c>, so those results stay long.
    /// </para>
    /// <para>
    /// Exact here means exact for the table Fhir.Metrics loads, with two known limits. A definition that itself divides
    /// (<c>[min_us]</c> is <c>[fdr_us]/60</c>) yields a factor rounded to 28 significant digits, so
    /// <c>1 '[foz_us]'</c> converts to <c>479.9999999999999999999999999 '[min_us]'</c>; Fhir.Metrics keeps only the
    /// compiled definition, not its formula, so the division cannot be deferred. And Fhir.Metrics truncates definition
    /// values to 16 characters (<c>[pi]</c> is 3.14159265358979), which the exact walk reproduces faithfully.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <see langword="false"/> before .NET 11, where the Fhir.Metrics result is kept unchanged, and when a unit has no
    /// factor to walk (a special unit such as <c>Cel</c>), in which case the Fhir.Metrics result is used as well.
    /// </returns>
    private bool TryGetExactFactor(Metric left, Metric right, bool divide, out Exponential numerator, out Exponential denominator)
    {
        numerator = Exponential.One;
        denominator = Exponential.One;
        if (!DecimalConversion.UnitFactorsCarryNoise)
        {
            return false;
        }

        var factors = new Dictionary<string, (Exponential Value, int Exponent)>(StringComparer.Ordinal);
        if (!CollectFactors(left, 1, factors, 0) || !CollectFactors(right, divide ? -1 : 1, factors, 0))
        {
            return false;
        }

        foreach (var (value, exponent) in factors.Values)
        {
            for (var i = 0; i < Math.Abs(exponent); i++)
            {
                if (exponent > 0)
                {
                    numerator *= value;
                }
                else
                {
                    denominator *= value;
                }
            }
        }

        return true;
    }

    // Keeps the Fhir.Metrics result, trailing zeros included, when it already has the exact value. Otherwise the exact
    // value is used with its trailing zeros dropped: their number depends on the arithmetic path, not on the operands,
    // and does not match what .NET 10 produced from the noisy factors either (2.0 'cm' * 2.0 'm' = 0.040000 'm2').
    private static decimal PreferExact(Exponential fhirMetricsResult, Exponential exact)
    {
        var exactResult = DecimalConversion.ToDecimal(exact, dropTrailingZeros: true);

        // Above this magnitude only scale zero is representable, so there is no legacy scale to preserve.
        // Do not narrow the noisy legacy result here: it can exceed decimal range even when the exact value fits.
        if (Math.Abs(exactResult) > decimal.MaxValue / 10m)
        {
            return exactResult;
        }

        var legacyResult = DecimalConversion.ToDecimal(fhirMetricsResult);
        return exactResult == legacyResult ? legacyResult : exactResult;
    }

    // UCUM definitions are a few levels deep ([mi_i] -> [ft_i] -> [in_i] -> cm); the bound only stops a cyclic table.
    private const int MaxDefinitionDepth = 32;

    private bool CollectFactors(Metric metric, int exponent, Dictionary<string, (Exponential Value, int Exponent)> factors, int depth)
    {
        if (depth > MaxDefinitionDepth)
        {
            return false;
        }

        foreach (var axis in metric.Axes)
        {
            var axisExponent = axis.Exponent * exponent;
            if (axis.Prefix is { } prefix)
            {
                AddFactor(factors, "p:" + prefix.Symbol, prefix.Factor, axisExponent);
            }

            if (axis.Unit.IsBase)
            {
                continue;
            }

            var definition = _ucum.Conversions.Find(axis.Unit);
            if (definition is null)
            {
                return false;
            }

            AddFactor(factors, "u:" + axis.Unit.Symbol, definition.Convert(Exponential.One).Value, axisExponent);
            if (!CollectFactors(definition.To, axisExponent, factors, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddFactor(Dictionary<string, (Exponential Value, int Exponent)> factors, string key, Exponential value, int exponent)
    {
        factors[key] = factors.TryGetValue(key, out var existing)
            ? (existing.Value, existing.Exponent + exponent)
            : (value, exponent);
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
