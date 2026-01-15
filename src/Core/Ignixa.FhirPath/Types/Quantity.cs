/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath Quantity type for Phase 23 implementation.
 * Represents a numeric value with a UCUM unit.
 */

using System;

namespace Ignixa.FhirPath.Types;

#nullable enable

/// <summary>
/// Represents a quantity value with a numeric value and UCUM unit.
/// Supports arithmetic operations, comparisons, and unit conversions.
/// </summary>
/// <remarks>
/// CA1036: Comparison operators are intentionally not defined because unit conversion
/// must happen first via QuantityEvaluator for FHIR/FHIRPath semantics.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1036:OverrideMethodsOnComparableTypes", Justification = "Comparison operators require unit conversion first")]
public sealed class Quantity : IEquatable<Quantity>, IComparable<Quantity>
{
    /// <summary>
    /// Creates a new Quantity with the specified value and unit.
    /// </summary>
    /// <param name="value">The numeric value</param>
    /// <param name="unit">The UCUM unit code</param>
    public Quantity(decimal value, string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        Value = value;
        Unit = unit;
    }

    /// <summary>
    /// The numeric value of this quantity.
    /// </summary>
    public decimal Value { get; }

    /// <summary>
    /// The UCUM unit code (e.g., "mg", "Cel", "mm[Hg]").
    /// </summary>
    public string Unit { get; }

    /// <summary>
    /// Optional precision information for date/time-based quantities.
    /// </summary>
    public int? Precision { get; init; }

    /// <summary>
    /// Adds two quantities with compatible units.
    /// </summary>
    /// <param name="other">The quantity to add</param>
    /// <param name="unitConverter">Unit converter for validation and conversion</param>
    /// <returns>The sum, or null if units are incompatible</returns>
    public Quantity? Add(Quantity other, IQuantityUnitConverter unitConverter)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(unitConverter);

        // Try to convert other to this unit
        var convertedValue = unitConverter.Convert(other.Value, other.Unit, Unit);
        if (convertedValue == null)
            return null; // Incompatible units

        return new Quantity(Value + convertedValue.Value, Unit);
    }

    /// <summary>
    /// Subtracts a quantity with compatible units.
    /// </summary>
    /// <param name="other">The quantity to subtract</param>
    /// <param name="unitConverter">Unit converter for validation and conversion</param>
    /// <returns>The difference, or null if units are incompatible</returns>
    public Quantity? Subtract(Quantity other, IQuantityUnitConverter unitConverter)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(unitConverter);

        // Try to convert other to this unit
        var convertedValue = unitConverter.Convert(other.Value, other.Unit, Unit);
        if (convertedValue == null)
            return null; // Incompatible units

        return new Quantity(Value - convertedValue.Value, Unit);
    }

    /// <summary>
    /// Multiplies this quantity by a scalar value.
    /// </summary>
    /// <param name="scalar">The scalar multiplier</param>
    /// <returns>The scaled quantity</returns>
    public Quantity Multiply(decimal scalar)
    {
        return new Quantity(Value * scalar, Unit);
    }

    /// <summary>
    /// Divides this quantity by another quantity with compatible units.
    /// Returns a dimensionless decimal ratio.
    /// </summary>
    /// <param name="other">The quantity to divide by</param>
    /// <param name="unitConverter">Unit converter for validation and conversion</param>
    /// <returns>The dimensionless ratio, or null if units are incompatible or division by zero</returns>
    public decimal? DivideBy(Quantity other, IQuantityUnitConverter unitConverter)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(unitConverter);

        // Try to convert other to this unit
        var convertedValue = unitConverter.Convert(other.Value, other.Unit, Unit);
        if (convertedValue == null)
            return null; // Incompatible units

        if (convertedValue.Value == 0)
            return null; // Division by zero

        return Value / convertedValue.Value;
    }

    /// <summary>
    /// Divides this quantity by a scalar value.
    /// </summary>
    /// <param name="scalar">The scalar divisor</param>
    /// <returns>The scaled quantity, or null if division by zero</returns>
    public Quantity? DivideByScalar(decimal scalar)
    {
        if (scalar == 0)
            return null; // Division by zero

        return new Quantity(Value / scalar, Unit);
    }

    /// <summary>
    /// Checks if this quantity can be combined (added/subtracted) with another.
    /// </summary>
    /// <param name="other">The other quantity</param>
    /// <param name="unitConverter">Unit converter for compatibility checking</param>
    /// <returns>True if units are compatible</returns>
    public bool CanCombineWith(Quantity other, IQuantityUnitConverter unitConverter)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(unitConverter);

        return unitConverter.IsCompatible(Unit, other.Unit);
    }

    /// <summary>
    /// Converts this quantity to a different unit.
    /// </summary>
    /// <param name="targetUnit">The target UCUM unit</param>
    /// <param name="unitConverter">Unit converter</param>
    /// <returns>The converted quantity, or null if conversion is not possible</returns>
    public Quantity? ConvertTo(string targetUnit, IQuantityUnitConverter unitConverter)
    {
        ArgumentNullException.ThrowIfNull(targetUnit);
        ArgumentNullException.ThrowIfNull(unitConverter);

        var convertedValue = unitConverter.Convert(Value, Unit, targetUnit);
        if (convertedValue == null)
            return null;

        return new Quantity(convertedValue.Value, targetUnit);
    }

    /// <summary>
    /// Compares this quantity to another for ordering.
    /// </summary>
    /// <param name="other">The quantity to compare to</param>
    /// <returns>-1 if less than, 0 if equal, 1 if greater than</returns>
    /// <remarks>Throws if units are not directly equal (caller must convert first)</remarks>
    public int CompareTo(Quantity? other)
    {
        if (other == null)
            return 1;

        // For direct comparison, units must match exactly
        if (Unit != other.Unit)
            throw new InvalidOperationException(
                $"Cannot compare quantities with different units: '{Unit}' vs '{other.Unit}'. Convert to same unit first.");

        return Value.CompareTo(other.Value);
    }

    /// <summary>
    /// Determines equality with another quantity.
    /// Does NOT normalize calendar keywords to UCUM because they represent different concepts:
    /// - FHIRPath calendar keywords (year, month, week, etc.) are calendar-aware
    /// - UCUM codes ('a', 'mo', 'wk', etc.) are scientific fixed-duration units
    /// </summary>
    public bool Equals(Quantity? other)
    {
        if (other == null)
            return false;

        // Compare values and units as-is (no normalization)
        return Value == other.Value && 
               string.Equals(Unit, other.Unit, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines equality with another object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is Quantity other && Equals(other);
    }

    /// <summary>
    /// Gets the hash code for this quantity.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Value, Unit);
    }

    /// <summary>
    /// Returns a string representation in FhirPath format.
    /// For calendar duration keywords (year, month, week, etc.), uses keyword form (e.g., "1 week").
    /// For UCUM units (including 'wk', 'a', 'mo'), uses quoted form (e.g., "1 'wk'").
    /// </summary>
    public override string ToString()
    {
        // If the unit is already a calendar keyword (year, month, week, etc.), use it directly
        if (IsCalendarKeyword(Unit))
        {
            return $"{Value} {Unit}";
        }

        // For all other units (including UCUM time units like 'wk', 'a', 'mo'), use quoted form
        return $"{Value} '{Unit}'";
    }

    /// <summary>
    /// Checks if the unit is a FHIRPath calendar duration keyword.
    /// </summary>
    private static bool IsCalendarKeyword(string unit)
    {
        return unit switch
        {
            "year" or "years" or "month" or "months" or "week" or "weeks" or
            "day" or "days" or "hour" or "hours" or "minute" or "minutes" or
            "second" or "seconds" or "millisecond" or "milliseconds" => true,
            _ => false
        };
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(Quantity? left, Quantity? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(Quantity? left, Quantity? right)
    {
        return !(left == right);
    }
}

/// <summary>
/// Interface for unit conversion operations.
/// Abstraction allows different implementations (e.g., Fhir.Metrics, custom converters).
/// </summary>
public interface IQuantityUnitConverter
{
    /// <summary>
    /// Checks if two units are compatible for conversion.
    /// </summary>
    /// <param name="unit1">First UCUM unit</param>
    /// <param name="unit2">Second UCUM unit</param>
    /// <returns>True if units can be converted between each other</returns>
    bool IsCompatible(string unit1, string unit2);

    /// <summary>
    /// Converts a value from one unit to another.
    /// </summary>
    /// <param name="value">The numeric value</param>
    /// <param name="fromUnit">The source UCUM unit</param>
    /// <param name="toUnit">The target UCUM unit</param>
    /// <returns>The converted value, or null if conversion is not possible</returns>
    decimal? Convert(decimal value, string fromUnit, string toUnit);

    /// <summary>
    /// Gets the dimensionality of a unit (e.g., "mass", "length", "time").
    /// </summary>
    /// <param name="unit">The UCUM unit</param>
    /// <returns>The dimension category, or null if unknown</returns>
    string? GetDimensionality(string unit);

    /// <summary>
    /// Multiplies two quantities using UCUM unit algebra.
    /// </summary>
    /// <param name="left">The left quantity (value and unit)</param>
    /// <param name="right">The right quantity (value and unit)</param>
    /// <returns>The resulting quantity with combined units, or null if operation fails</returns>
    Quantity? Multiply(Quantity left, Quantity right);

    /// <summary>
    /// Divides two quantities using UCUM unit algebra.
    /// </summary>
    /// <param name="left">The left quantity (numerator)</param>
    /// <param name="right">The right quantity (denominator)</param>
    /// <returns>The resulting quantity with divided units, or null if division fails</returns>
    Quantity? Divide(Quantity left, Quantity right);
}
