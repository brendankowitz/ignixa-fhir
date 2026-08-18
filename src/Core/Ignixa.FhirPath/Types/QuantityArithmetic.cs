/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit-aware arithmetic over FhirQuantity, kept with the evaluator rather than with the value type.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Types;

#nullable enable

/// <summary>
/// Unit-aware arithmetic over <see cref="FhirQuantity"/>.
/// </summary>
/// <remarks>
/// These are extension methods rather than members of <see cref="FhirQuantity"/> because each one
/// either needs an <see cref="IQuantityUnitConverter"/> or encodes a FHIRPath rule about what an
/// impossible operation yields — a <see langword="null"/> the evaluator turns into an empty
/// collection. Both are evaluation concerns, and keeping them out of <c>Ignixa.Abstractions</c> is
/// what lets the value type be shared without sharing an opinion on unit algebra.
/// </remarks>
public static class QuantityArithmetic
{
    /// <summary>
    /// Adds two quantities with compatible units.
    /// </summary>
    /// <param name="quantity">The left operand, whose unit the result is expressed in.</param>
    /// <param name="other">The quantity to add.</param>
    /// <param name="unitConverter">Unit converter for validation and conversion.</param>
    /// <returns>The sum, or <see langword="null"/> if the units are incompatible.</returns>
    public static FhirQuantity? Add(this FhirQuantity quantity, FhirQuantity other, IQuantityUnitConverter unitConverter)
    {
        var converted = ConvertOperand(quantity, other, unitConverter);

        return converted is null ? null : new FhirQuantity(quantity.Value + converted.Value, quantity.Unit);
    }

    /// <summary>
    /// Subtracts a quantity with a compatible unit.
    /// </summary>
    /// <param name="quantity">The left operand, whose unit the result is expressed in.</param>
    /// <param name="other">The quantity to subtract.</param>
    /// <param name="unitConverter">Unit converter for validation and conversion.</param>
    /// <returns>The difference, or <see langword="null"/> if the units are incompatible.</returns>
    public static FhirQuantity? Subtract(this FhirQuantity quantity, FhirQuantity other, IQuantityUnitConverter unitConverter)
    {
        var converted = ConvertOperand(quantity, other, unitConverter);

        return converted is null ? null : new FhirQuantity(quantity.Value - converted.Value, quantity.Unit);
    }

    /// <summary>
    /// Scales a quantity, leaving its unit unchanged.
    /// </summary>
    /// <param name="quantity">The quantity to scale.</param>
    /// <param name="scalar">The scalar multiplier.</param>
    /// <returns>The scaled quantity.</returns>
    public static FhirQuantity Multiply(this FhirQuantity quantity, decimal scalar)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        return new FhirQuantity(quantity.Value * scalar, quantity.Unit);
    }

    /// <summary>
    /// Divides a quantity by a scalar, leaving its unit unchanged.
    /// </summary>
    /// <param name="quantity">The quantity to divide.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>The scaled quantity, or <see langword="null"/> when <paramref name="scalar"/> is zero.</returns>
    public static FhirQuantity? DivideByScalar(this FhirQuantity quantity, decimal scalar)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        return scalar == 0 ? null : new FhirQuantity(quantity.Value / scalar, quantity.Unit);
    }

    /// <summary>
    /// Converts a quantity to a different unit.
    /// </summary>
    /// <param name="quantity">The quantity to convert.</param>
    /// <param name="targetUnit">The target UCUM unit.</param>
    /// <param name="unitConverter">Unit converter.</param>
    /// <returns>The converted quantity, or <see langword="null"/> if conversion is not possible.</returns>
    public static FhirQuantity? ConvertTo(this FhirQuantity quantity, string targetUnit, IQuantityUnitConverter unitConverter)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(targetUnit);
        ArgumentNullException.ThrowIfNull(unitConverter);

        var converted = unitConverter.Convert(quantity.Value, quantity.Unit, targetUnit);

        return converted is null ? null : new FhirQuantity(converted.Value, targetUnit);
    }

    private static decimal? ConvertOperand(FhirQuantity quantity, FhirQuantity other, IQuantityUnitConverter unitConverter)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(unitConverter);

        return unitConverter.Convert(other.Value, other.Unit, quantity.Unit);
    }
}
