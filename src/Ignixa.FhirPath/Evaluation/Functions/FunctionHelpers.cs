/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Shared helper infrastructure for FhirPath function implementations.
 * Provides primitive element creation, equality comparers, and utility methods.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Shared helper methods and types for FhirPath function implementations.
/// </summary>
internal static class FunctionHelpers
{
    #region Primitive Element Creation

    /// <summary>
    /// Creates an ITypedElement representing a boolean value.
    /// </summary>
    public static ITypedElement CreateBoolean(bool value) => new PrimitiveElement(value, "boolean");

    /// <summary>
    /// Creates an ITypedElement representing an integer value.
    /// </summary>
    public static ITypedElement CreateInteger(int value) => new PrimitiveElement(value, "integer");

    /// <summary>
    /// Creates an ITypedElement representing a decimal value.
    /// </summary>
    public static ITypedElement CreateDecimal(decimal value) => new PrimitiveElement(value, "decimal");

    /// <summary>
    /// Creates an ITypedElement representing a string value.
    /// </summary>
    public static ITypedElement CreateString(string value) => new PrimitiveElement(value, "string");

    /// <summary>
    /// Creates an ITypedElement representing a date value.
    /// </summary>
    public static ITypedElement CreateDate(string value) => new PrimitiveElement(value, "date");

    /// <summary>
    /// Creates an ITypedElement representing a dateTime value.
    /// </summary>
    public static ITypedElement CreateDateTime(string value) => new PrimitiveElement(value, "dateTime");

    /// <summary>
    /// Creates an ITypedElement representing a time value.
    /// </summary>
    public static ITypedElement CreateTime(string value) => new PrimitiveElement(value, "time");

    #endregion

    #region Boolean Helpers

    /// <summary>
    /// Checks if a collection contains a single true boolean value.
    /// </summary>
    public static bool IsTrue(IEnumerable<ITypedElement> elements)
    {
        var list = elements.ToList();
        return list.Count == 1 && list[0].Value is bool b && b;
    }

    /// <summary>
    /// Converts a nullable boolean to a FhirPath result collection.
    /// Per FHIRPath spec:
    /// - true → collection with boolean true
    /// - false → collection with boolean false
    /// - null → empty collection
    /// </summary>
    public static IEnumerable<ITypedElement> ReturnBoolean(bool? result)
    {
        return result.HasValue
            ? new[] { CreateBoolean(result.Value) }
            : Enumerable.Empty<ITypedElement>();
    }

    #endregion

    #region Equality Helpers

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    public static bool AreEqual(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        return left.Equals(right);
    }

    #endregion

    #region Type Conversion Helpers

    /// <summary>
    /// Attempts to convert a value to decimal (handles Integer -> Decimal implicit conversion).
    /// </summary>
    public static bool TryConvertToDecimal(object? value, out decimal result)
    {
        result = 0;

        if (value is decimal d)
        {
            result = d;
            return true;
        }

        if (value is int i)
        {
            result = i;
            return true;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                result = Convert.ToDecimal(convertible);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    #endregion

    #region Equality Comparers

    /// <summary>
    /// Equality comparer for object values.
    /// </summary>
    public class ObjectEqualityComparer : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Equals(y);
        }

        public int GetHashCode(object? obj)
        {
            return obj?.GetHashCode() ?? 0;
        }
    }

    /// <summary>
    /// Equality comparer for ITypedElement instances based on their values.
    /// </summary>
    public class TypedElementEqualityComparer : IEqualityComparer<ITypedElement>
    {
        public bool Equals(ITypedElement? x, ITypedElement? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            if (x.Value == null && y.Value == null) return true;
            if (x.Value == null || y.Value == null) return false;
            return x.Value.Equals(y.Value);
        }

        public int GetHashCode(ITypedElement obj)
        {
            return obj.Value?.GetHashCode() ?? 0;
        }
    }

    #endregion

    #region Collection Helpers

    /// <summary>
    /// Union operator: Merge collections, eliminate duplicates.
    /// </summary>
    public static IEnumerable<ITypedElement> EvaluateUnion(List<ITypedElement> left, List<ITypedElement> right)
    {
        var result = new List<ITypedElement>();

        // Add all left elements
        foreach (var leftItem in left)
        {
            if (!result.Any(r => AreEqual(r.Value, leftItem.Value)))
            {
                result.Add(leftItem);
            }
        }

        // Add right elements that aren't duplicates
        foreach (var rightItem in right)
        {
            if (!result.Any(r => AreEqual(r.Value, rightItem.Value)))
            {
                result.Add(rightItem);
            }
        }

        return result;
    }

    #endregion

    #region PrimitiveElement Implementation

    /// <summary>
    /// Simple implementation of ITypedElement for primitive values.
    /// </summary>
    public class PrimitiveElement : ITypedElement
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
