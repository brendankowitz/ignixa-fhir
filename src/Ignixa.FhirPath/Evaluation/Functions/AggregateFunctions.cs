/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath aggregate function implementations (Phase 23, Week 4).
 * Implements sum(), min(), max(), and avg() according to FHIRPath 3.0.0 spec.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Types;

namespace Ignixa.FhirPath.Evaluation.Functions;

#nullable enable

/// <summary>
/// Aggregate function implementations for FhirPath.
/// Supports sum, min, max, avg operations on collections of integers, decimals, quantities, strings, and dates.
/// </summary>
internal static class AggregateFunctions
{

    /// <summary>
    /// Computes the sum of a collection of numeric values or quantities.
    /// Returns empty for empty collection or incompatible types.
    /// </summary>
    /// <param name="elements">Collection to sum</param>
    /// <returns>Sum as ITypedElement, or empty if operation not possible</returns>
    public static IEnumerable<ITypedElement> Sum(IEnumerable<ITypedElement> elements)
    {
        var list = elements.Where(e => e != null).ToList();

        // Empty collection returns empty (not 0)
        if (list.Count == 0)
            return Enumerable.Empty<ITypedElement>();

        // Single item returns that item
        if (list.Count == 1)
            return new[] { list[0] };

        // Determine the type to work with
        var firstValue = list[0].Value;

        // Handle Quantity collection
        if (firstValue is Quantity)
        {
            return SumQuantities(list);
        }

        // Handle numeric collection (integers and decimals)
        return SumNumeric(list);
    }

    /// <summary>
    /// Finds the minimum value in a collection.
    /// Supports integers, decimals, strings (lexicographic), dates, and quantities.
    /// </summary>
    /// <param name="elements">Collection to evaluate</param>
    /// <returns>Minimum element, or empty if collection is empty</returns>
    public static IEnumerable<ITypedElement> Min(IEnumerable<ITypedElement> elements)
    {
        var list = elements.Where(e => e != null).ToList();

        // Empty collection returns empty
        if (list.Count == 0)
            return Enumerable.Empty<ITypedElement>();

        // Single item returns that item
        if (list.Count == 1)
            return new[] { list[0] };

        var firstValue = list[0].Value;

        // Handle Quantity collection
        if (firstValue is Quantity)
        {
            return MinMaxQuantities(list, isMax: false);
        }

        // Handle numeric types
        if (IsNumeric(firstValue))
        {
            return MinMaxNumeric(list, isMax: false);
        }

        // Handle string comparison (but check for date/datetime strings first)
        if (firstValue is string s && s.StartsWith('@'))
        {
            // Date or DateTime literal (@2024-01-10 or @2024-01-10T10:00:00Z)
            return MinMaxDate(list, isMax: false);
        }

        if (firstValue is string)
        {
            return MinMaxString(list, isMax: false);
        }

        // Handle date/dateTime comparison
        if (IsDateOrDateTime(list[0]))
        {
            return MinMaxDate(list, isMax: false);
        }

        return Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// Finds the maximum value in a collection.
    /// Supports integers, decimals, strings (lexicographic), dates, and quantities.
    /// </summary>
    /// <param name="elements">Collection to evaluate</param>
    /// <returns>Maximum element, or empty if collection is empty</returns>
    public static IEnumerable<ITypedElement> Max(IEnumerable<ITypedElement> elements)
    {
        var list = elements.Where(e => e != null).ToList();

        // Empty collection returns empty
        if (list.Count == 0)
            return Enumerable.Empty<ITypedElement>();

        // Single item returns that item
        if (list.Count == 1)
            return new[] { list[0] };

        var firstValue = list[0].Value;

        // Handle Quantity collection
        if (firstValue is Quantity)
        {
            return MinMaxQuantities(list, isMax: true);
        }

        // Handle numeric types
        if (IsNumeric(firstValue))
        {
            return MinMaxNumeric(list, isMax: true);
        }

        // Handle string comparison (but check for date/datetime strings first)
        if (firstValue is string s && s.StartsWith('@'))
        {
            // Date or DateTime literal (@2024-01-10 or @2024-01-10T10:00:00Z)
            return MinMaxDate(list, isMax: true);
        }

        if (firstValue is string)
        {
            return MinMaxString(list, isMax: true);
        }

        // Handle date/dateTime comparison
        if (IsDateOrDateTime(list[0]))
        {
            return MinMaxDate(list, isMax: true);
        }

        return Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// Computes the average of a collection of numeric values or quantities.
    /// Integer collections are promoted to decimal for the result.
    /// Returns empty for empty collection or incompatible types.
    /// </summary>
    /// <param name="elements">Collection to average</param>
    /// <returns>Average as ITypedElement, or empty if operation not possible</returns>
    public static IEnumerable<ITypedElement> Avg(IEnumerable<ITypedElement> elements)
    {
        var list = elements.Where(e => e != null).ToList();

        // Empty collection returns empty
        if (list.Count == 0)
            return Enumerable.Empty<ITypedElement>();

        // Single item: return as decimal for integers, otherwise return as-is
        if (list.Count == 1)
        {
            var singleValue = list[0].Value;
            if (singleValue is int i)
                return new[] { CreateDecimal(i) };
            if (singleValue is Quantity)
                return new[] { list[0] };
            return new[] { list[0] };
        }

        var firstValue = list[0].Value;

        // Handle Quantity collection
        if (firstValue is Quantity)
        {
            return AvgQuantities(list);
        }

        // Handle numeric collection
        return AvgNumeric(list);
    }

    #region Sum Implementations

    private static IEnumerable<ITypedElement> SumQuantities(List<ITypedElement> list)
    {
        // All quantities must have the same unit
        var quantities = list.Select(e => e.Value as Quantity).ToList();
        if (quantities.Any(q => q == null))
            return Enumerable.Empty<ITypedElement>(); // Mixed types

        var firstUnit = quantities[0]!.Unit;
        if (!quantities.All(q => q!.Unit == firstUnit))
            return Enumerable.Empty<ITypedElement>(); // Different units

        // Sum all values
        decimal sum = quantities.Sum(q => q!.Value);
        var resultQuantity = new Quantity(sum, firstUnit);
        return new[] { new QuantityElement(resultQuantity) };
    }

    private static IEnumerable<ITypedElement> SumNumeric(List<ITypedElement> list)
    {
        // Check if we have any decimals (determines return type)
        bool hasDecimal = list.Any(e => e.Value is decimal);
        decimal sum = 0;

        foreach (var element in list)
        {
            var value = element.Value;
            if (value is int i)
            {
                sum += i;
            }
            else if (value is decimal d)
            {
                sum += d;
            }
            else if (value is long l)
            {
                sum += l;
            }
            else
            {
                // Incompatible type in collection
                return Enumerable.Empty<ITypedElement>();
            }
        }

        // If any decimal, return decimal; otherwise return integer if possible
        if (hasDecimal)
        {
            return new[] { CreateDecimal(sum) };
        }

        // For integer-only collections, return as integer if within range
        if (sum == Math.Floor(sum) && sum >= int.MinValue && sum <= int.MaxValue)
        {
            return new[] { CreateInteger((int)sum) };
        }

        // Overflow or fractional result - return as decimal
        return new[] { CreateDecimal(sum) };
    }

    #endregion

    #region Min/Max Implementations

    private static IEnumerable<ITypedElement> MinMaxQuantities(List<ITypedElement> list, bool isMax)
    {
        // All quantities must have the same unit
        var quantities = list.Select(e => e.Value as Quantity).ToList();
        if (quantities.Any(q => q == null))
            return Enumerable.Empty<ITypedElement>(); // Mixed types

        var firstUnit = quantities[0]!.Unit;
        if (!quantities.All(q => q!.Unit == firstUnit))
            return Enumerable.Empty<ITypedElement>(); // Different units

        // Find min/max
        var result = isMax
            ? quantities.MaxBy(q => q!.Value)
            : quantities.MinBy(q => q!.Value);

        return result != null ? new[] { new QuantityElement(result) } : Enumerable.Empty<ITypedElement>();
    }

    private static IEnumerable<ITypedElement> MinMaxNumeric(List<ITypedElement> list, bool isMax)
    {
        ITypedElement? result = null;
        decimal? extremeValue = null;

        foreach (var element in list)
        {
            var value = element.Value;
            decimal numericValue;

            if (value is int i)
            {
                numericValue = i;
            }
            else if (value is decimal d)
            {
                numericValue = d;
            }
            else if (value is long l)
            {
                numericValue = l;
            }
            else
            {
                // Skip incompatible types
                continue;
            }

            if (extremeValue == null ||
                (isMax && numericValue > extremeValue.Value) ||
                (!isMax && numericValue < extremeValue.Value))
            {
                extremeValue = numericValue;
                result = element;
            }
        }

        return result != null ? new[] { result } : Enumerable.Empty<ITypedElement>();
    }

    private static IEnumerable<ITypedElement> MinMaxString(List<ITypedElement> list, bool isMax)
    {
        ITypedElement? result = null;
        string? extremeValue = null;

        foreach (var element in list)
        {
            if (element.Value is not string s)
                continue;

            if (extremeValue == null ||
                (isMax && string.Compare(s, extremeValue, StringComparison.Ordinal) > 0) ||
                (!isMax && string.Compare(s, extremeValue, StringComparison.Ordinal) < 0))
            {
                extremeValue = s;
                result = element;
            }
        }

        return result != null ? new[] { result } : Enumerable.Empty<ITypedElement>();
    }

    private static IEnumerable<ITypedElement> MinMaxDate(List<ITypedElement> list, bool isMax)
    {
        string? extremeValue = null;
        string? extremeType = null;

        foreach (var element in list)
        {
            string? dateString = null;
            string? inferredType = null;

            if (element.Value is DateTime dt)
            {
                dateString = dt.ToString("yyyy-MM-dd");
                inferredType = "date";
            }
            else if (element.Value is DateTimeOffset dto)
            {
                dateString = dto.ToString("yyyy-MM-ddTHH:mm:ssZ");
                inferredType = "dateTime";
            }
            else if (element.Value is string s && s.StartsWith('@'))
            {
                // Strip @ prefix
                dateString = s.Substring(1);
                // Infer type from format: if it has 'T', it's dateTime, otherwise date
                inferredType = dateString.Contains('T', StringComparison.Ordinal) ? "dateTime" : "date";
            }
            else if (element.Value is string s2)
            {
                dateString = s2;
                inferredType = element.InstanceType;
            }

            if (dateString == null || !TryParseDate(dateString, out var parsed))
                continue;

            if (extremeValue == null)
            {
                extremeValue = dateString;
                extremeType = inferredType ?? element.InstanceType;
            }
            else if (TryParseDate(extremeValue, out var currentExtreme))
            {
                if ((isMax && parsed > currentExtreme) || (!isMax && parsed < currentExtreme))
                {
                    extremeValue = dateString;
                    extremeType = inferredType ?? element.InstanceType;
                }
            }
        }

        if (extremeValue != null && extremeType != null)
        {
            return new[] { new PrimitiveElement(extremeValue, extremeType) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    #endregion

    #region Avg Implementations

    private static IEnumerable<ITypedElement> AvgQuantities(List<ITypedElement> list)
    {
        // All quantities must have the same unit
        var quantities = list.Select(e => e.Value as Quantity).ToList();
        if (quantities.Any(q => q == null))
            return Enumerable.Empty<ITypedElement>(); // Mixed types

        var firstUnit = quantities[0]!.Unit;
        if (!quantities.All(q => q!.Unit == firstUnit))
            return Enumerable.Empty<ITypedElement>(); // Different units

        // Average all values
        decimal avg = quantities.Average(q => q!.Value);
        var resultQuantity = new Quantity(avg, firstUnit);
        return new[] { new QuantityElement(resultQuantity) };
    }

    private static IEnumerable<ITypedElement> AvgNumeric(List<ITypedElement> list)
    {
        decimal sum = 0;
        int count = 0;

        foreach (var element in list)
        {
            var value = element.Value;
            if (value is int i)
            {
                sum += i;
                count++;
            }
            else if (value is decimal d)
            {
                sum += d;
                count++;
            }
            else if (value is long l)
            {
                sum += l;
                count++;
            }
            else
            {
                // Incompatible type in collection
                return Enumerable.Empty<ITypedElement>();
            }
        }

        if (count == 0)
            return Enumerable.Empty<ITypedElement>();

        // avg() always returns decimal, even for integer collections
        decimal avg = sum / count;
        return new[] { CreateDecimal(avg) };
    }

    #endregion

    #region Helper Methods

    private static bool IsNumeric(object? value)
    {
        return value is int or long or decimal or double or float;
    }

    private static bool IsDateOrDateTime(ITypedElement element)
    {
        // CA1308 suppressed: FhirPath type names are lowercase by specification
#pragma warning disable CA1308 // Normalize strings to uppercase
        var type = element.InstanceType?.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
        return type == "date" || type == "datetime";
    }

    private static bool TryParseDate(string value, out DateTime result)
    {
        // Try parsing ISO 8601 date formats
        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ss.fffzzz"
        };

        return DateTime.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
    }

    private static ITypedElement CreateInteger(int value) => new PrimitiveElement(value, "integer");
    private static ITypedElement CreateDecimal(decimal value) => new PrimitiveElement(value, "decimal");

    #endregion

    #region ITypedElement Implementations

    /// <summary>
    /// ITypedElement wrapper for Quantity values.
    /// </summary>
    private class QuantityElement : ITypedElement
    {
        private readonly Quantity _quantity;

        public QuantityElement(Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(quantity);
            _quantity = quantity;
        }

        public string Name => string.Empty;
        public string InstanceType => "Quantity";
        public object Value => _quantity;
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null)
        {
            // Quantity can have child elements: value, unit, system, code
            if (name == null || name == "value")
                yield return new PrimitiveElement(_quantity.Value, "decimal");

            if (name == null || name == "unit" || name == "code")
                yield return new PrimitiveElement(_quantity.Unit, "string");

            if (name == null || name == "system")
                yield return new PrimitiveElement("http://unitsofmeasure.org", "uri");
        }
    }

    /// <summary>
    /// ITypedElement wrapper for primitive values.
    /// </summary>
    private class PrimitiveElement : ITypedElement
    {
        public PrimitiveElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null) => Enumerable.Empty<ITypedElement>();
    }

    #endregion
}
