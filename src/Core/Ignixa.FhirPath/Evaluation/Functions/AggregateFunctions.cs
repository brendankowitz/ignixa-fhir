/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath aggregate function implementations (Phase 23, Week 4).
 * Implements sum(), min(), max(), and avg() according to FHIRPath 3.0.0 spec.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
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
    /// <returns>Sum as IElement, or empty if operation not possible</returns>
    [FhirPathFunction("sum",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Aggregate",
        Description = "Computes the sum of a collection of numeric values or quantities")]
    public static IEnumerable<IElement> Sum(IEnumerable<IElement> elements)
    {
        var list = elements.Where(e => e != null).ToList();

        // Per FHIRPath spec: Empty collection returns 0
        if (list.Count == 0)
            return [FunctionHelpers.CreateInteger(0)];

        // Single item returns that item
        if (list.Count == 1)
            return [list[0]];

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
    [FhirPathFunction("min",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Aggregate",
        Description = "Finds the minimum value in a collection")]
    public static IEnumerable<IElement> Min(IEnumerable<IElement> elements)
    {
        var list = elements.Where(e => e != null).ToList();

        // Empty collection returns empty
        if (list.Count == 0)
            return [];

        // Single item returns that item
        if (list.Count == 1)
            return [list[0]];

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

        // FhirTemporal carries the typed value from resource elements. time is routed to
        // MinMaxTime (ordinal HH:mm:ss comparison — no date component). date/dateTime/instant
        // are routed to MinMaxDate (parse-to-DateTime comparison).
        if (firstValue is FhirTemporal ft && ft.Kind == FhirPrimitive.Time)
        {
            return MinMaxTime(list, isMax: false);
        }

        if (firstValue is FhirTemporal)
        {
            return MinMaxDate(list, isMax: false);
        }

        // FHIRPath date/dateTime literals begin with '@'; the evaluator strips '@T' from time
        // literals so bare HH:mm:ss strings reach MinMaxString rather than this branch.
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

        return [];
    }

    /// <summary>
    /// Finds the maximum value in a collection.
    /// Supports integers, decimals, strings (lexicographic), dates, and quantities.
    /// </summary>
    /// <param name="elements">Collection to evaluate</param>
    /// <returns>Maximum element, or empty if collection is empty</returns>
    [FhirPathFunction("max",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Aggregate",
        Description = "Finds the maximum value in a collection")]
    public static IEnumerable<IElement> Max(IEnumerable<IElement> elements)
    {
        var list = elements.Where(e => e != null).ToList();

        // Empty collection returns empty
        if (list.Count == 0)
            return [];

        // Single item returns that item
        if (list.Count == 1)
            return [list[0]];

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

        // FhirTemporal carries the typed value from resource elements. time is routed to
        // MinMaxTime (ordinal HH:mm:ss comparison — no date component). date/dateTime/instant
        // are routed to MinMaxDate (parse-to-DateTime comparison).
        if (firstValue is FhirTemporal ft && ft.Kind == FhirPrimitive.Time)
        {
            return MinMaxTime(list, isMax: true);
        }

        if (firstValue is FhirTemporal)
        {
            return MinMaxDate(list, isMax: true);
        }

        // FHIRPath date/dateTime literals begin with '@'; the evaluator strips '@T' from time
        // literals so bare HH:mm:ss strings reach MinMaxString rather than this branch.
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

        return [];
    }

    /// <summary>
    /// Computes the average of a collection of numeric values or quantities.
    /// Integer collections are promoted to decimal for the result.
    /// Returns empty for empty collection or incompatible types.
    /// </summary>
    /// <param name="elements">Collection to average</param>
    /// <returns>Average as IElement, or empty if operation not possible</returns>
    [FhirPathFunction("avg",
        SupportedContexts = "any-any",
        ReturnType = "decimal",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Aggregate",
        Description = "Computes the average of a collection of numeric values or quantities")]
    public static IEnumerable<IElement> Avg(IEnumerable<IElement> elements)
    {
        var list = elements.Where(e => e != null).ToList();

        // Empty collection returns empty
        if (list.Count == 0)
            return [];

        // Single item: return as decimal for integers, otherwise return as-is
        if (list.Count == 1)
        {
            var singleValue = list[0].Value;
            if (singleValue is int i)
                return [CreateDecimal(i)];
            if (singleValue is Quantity)
                return [list[0]];
            return [list[0]];
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

    private static IEnumerable<IElement> SumQuantities(List<IElement> list)
    {
        // All quantities must have the same unit
        var quantities = list.Select(e => e.Value as Quantity).ToList();
        if (quantities.Any(q => q == null))
            return []; // Mixed types

        var firstUnit = quantities[0]!.Unit;
        if (!quantities.All(q => q!.Unit == firstUnit))
            return []; // Different units

        // Sum all values
        decimal sum = quantities.Sum(q => q!.Value);
        var resultQuantity = new Quantity(sum, firstUnit);
        return [FunctionHelpers.CreateQuantity(resultQuantity)];
    }

    private static IEnumerable<IElement> SumNumeric(List<IElement> list)
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
                return [];
            }
        }

        // If any decimal, return decimal; otherwise return integer if possible
        if (hasDecimal)
        {
            return [CreateDecimal(sum)];
        }

        // For integer-only collections, return as integer if within range
        if (sum == Math.Floor(sum) && sum >= int.MinValue && sum <= int.MaxValue)
        {
            return [CreateInteger((int)sum)];
        }

        // Overflow or fractional result - return as decimal
        return [CreateDecimal(sum)];
    }

    #endregion

    #region Min/Max Implementations

    private static IEnumerable<IElement> MinMaxQuantities(List<IElement> list, bool isMax)
    {
        // All quantities must have the same unit
        var quantities = list.Select(e => e.Value as Quantity).ToList();
        if (quantities.Any(q => q == null))
            return []; // Mixed types

        var firstUnit = quantities[0]!.Unit;
        if (!quantities.All(q => q!.Unit == firstUnit))
            return []; // Different units

        // Find min/max
        var result = isMax
            ? quantities.MaxBy(q => q!.Value)
            : quantities.MinBy(q => q!.Value);

        return result != null ? [FunctionHelpers.CreateQuantity(result)] : [];
    }

    private static IEnumerable<IElement> MinMaxNumeric(List<IElement> list, bool isMax)
    {
        IElement? result = null;
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

        return result != null ? [result] : [];
    }

    private static IEnumerable<IElement> MinMaxString(List<IElement> list, bool isMax)
    {
        IElement? result = null;
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

        return result != null ? [result] : [];
    }

    /// <summary>
    /// Selects the earliest or latest element of a date/dateTime/instant collection.
    /// </summary>
    /// <remarks>
    /// The winning <see cref="IElement"/> is returned as-is rather than reconstructed as a
    /// <see cref="FunctionHelpers.PrimitiveElement"/> over its literal. Rebuilding it re-typed a
    /// resource-backed <see cref="FhirTemporal"/> back down to a wire string, so everything
    /// downstream that dispatches on the typed value - arithmetic, ordering, boundary functions -
    /// silently fell through to its string branch. min()/max() select an item; they do not
    /// construct one, and every sibling here (MinMaxTime, MinMaxNumeric, MinMaxString,
    /// MinMaxQuantities) already returns the element it picked.
    /// </remarks>
    private static IEnumerable<IElement> MinMaxDate(List<IElement> list, bool isMax)
    {
        IElement? result = null;
        DateTime extreme = default;

        foreach (var element in list)
        {
            if (!TryGetComparableDate(element, out var parsed))
                continue;

            if (result is null || (isMax ? parsed > extreme : parsed < extreme))
            {
                extreme = parsed;
                result = element;
            }
        }

        return result is not null ? [result] : [];
    }

    private static bool TryGetComparableDate(IElement element, out DateTime parsed)
    {
        parsed = default;

        var dateString = element.Value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            FhirTemporal fhirTemporal => fhirTemporal.Literal,
            string s when s.StartsWith('@') => s.Substring(1),
            string s => s,
            _ => null
        };

        return dateString is not null && TryParseDate(dateString, out parsed);
    }

    private static IEnumerable<IElement> MinMaxTime(List<IElement> list, bool isMax)
    {
        // FHIR time values (HH:mm:ss[.fff]) sort correctly under ordinal string comparison
        // because the format is zero-padded and fixed-width per component. A time value has
        // no date component, so parsing to DateTime is semantically wrong; MinMaxDate cannot
        // be reused here.
        string? extremeValue = null;
        IElement? result = null;

        foreach (var element in list)
        {
            string? timeString = null;

            if (element.Value is FhirTemporal fhirTemporal && fhirTemporal.Kind == FhirPrimitive.Time)
            {
                timeString = fhirTemporal.Literal;
            }
            else if (element.Value is string s)
            {
                // Plain HH:mm:ss[.fff] string — occurs in mixed collections where a resource
                // time element appears alongside FhirTemporal instances.
                timeString = s;
            }

            if (timeString == null)
                continue;

            var comparison = extremeValue is null ? -1
                : string.Compare(timeString, extremeValue, StringComparison.Ordinal);

            if (extremeValue == null || (isMax && comparison > 0) || (!isMax && comparison < 0))
            {
                extremeValue = timeString;
                result = element;
            }
        }

        return result is not null ? [result] : [];
    }

    #endregion

    #region Avg Implementations

    private static IEnumerable<IElement> AvgQuantities(List<IElement> list)
    {
        // All quantities must have the same unit
        var quantities = list.Select(e => e.Value as Quantity).ToList();
        if (quantities.Any(q => q == null))
            return []; // Mixed types

        var firstUnit = quantities[0]!.Unit;
        if (!quantities.All(q => q!.Unit == firstUnit))
            return []; // Different units

        // Average all values
        decimal avg = quantities.Average(q => q!.Value);
        var resultQuantity = new Quantity(avg, firstUnit);
        return [FunctionHelpers.CreateQuantity(resultQuantity)];
    }

    private static IEnumerable<IElement> AvgNumeric(List<IElement> list)
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
                return [];
            }
        }

        if (count == 0)
            return [];

        // avg() always returns decimal, even for integer collections
        decimal avg = sum / count;
        return [CreateDecimal(avg)];
    }

    #endregion

    #region Helper Methods

    private static bool IsNumeric(object? value)
    {
        return value is int or long or decimal or double or float;
    }

    private static bool IsDateOrDateTime(IElement element)
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

    private static IElement CreateInteger(int value) => new FunctionHelpers.PrimitiveElement(value, "integer");
    private static IElement CreateDecimal(decimal value) => new FunctionHelpers.PrimitiveElement(value, "decimal");

    #endregion
}
