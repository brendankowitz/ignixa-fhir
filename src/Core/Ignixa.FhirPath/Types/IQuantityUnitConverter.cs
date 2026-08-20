/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit conversion contract for FhirPath Quantity operations.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Types;

#nullable enable

/// <summary>
/// Interface for unit conversion operations.
/// Abstraction allows different implementations (e.g., Fhir.Metrics, custom converters).
/// </summary>
/// <remarks>
/// This lives here rather than beside <see cref="FhirQuantity"/> in <c>Ignixa.Abstractions</c>
/// deliberately. Unit algebra is FHIRPath evaluation policy, and its only implementation is
/// UCUM-shaped; putting the contract on the lowest assembly would freeze a service interface there
/// for the benefit of a single consumer. <see cref="FhirQuantity"/> has to be shared because it is
/// a value that crosses assembly boundaries inside <c>IElement.Value</c>; this does not.
/// </remarks>
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
    FhirQuantity? Multiply(FhirQuantity left, FhirQuantity right);

    /// <summary>
    /// Divides two quantities using UCUM unit algebra.
    /// </summary>
    /// <param name="left">The left quantity (numerator)</param>
    /// <param name="right">The right quantity (denominator)</param>
    /// <returns>The resulting quantity with divided units, or null if division fails</returns>
    FhirQuantity? Divide(FhirQuantity left, FhirQuantity right);
}
