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
    /// Creates an IElement representing a boolean value.
    /// </summary>
    public static IElement CreateBoolean(bool value) => new PrimitiveElement(value, "boolean");

    /// <summary>
    /// Creates an IElement representing an integer value.
    /// </summary>
    public static IElement CreateInteger(int value) => new PrimitiveElement(value, "integer");

    /// <summary>
    /// Creates an IElement representing a long (64-bit integer) value.
    /// </summary>
    public static IElement CreateLong(long value) => new PrimitiveElement(value, "long");

    /// <summary>
    /// Creates an IElement representing a decimal value.
    /// </summary>
    public static IElement CreateDecimal(decimal value) => new PrimitiveElement(value, "decimal");

    /// <summary>
    /// Creates an IElement representing a string value.
    /// </summary>
    public static IElement CreateString(string value) => new PrimitiveElement(value, "string");

    /// <summary>
    /// Creates an IElement representing a date value.
    /// </summary>
    public static IElement CreateDate(string value) => new PrimitiveElement(value, "date");

    /// <summary>
    /// Creates an IElement representing a dateTime value.
    /// </summary>
    public static IElement CreateDateTime(string value) => new PrimitiveElement(value, "dateTime");

    /// <summary>
    /// Creates an IElement representing a time value.
    /// </summary>
    public static IElement CreateTime(string value) => new PrimitiveElement(value, "time");

    #endregion

    #region Boolean Helpers

    /// <summary>
    /// Checks if a collection contains a single true boolean value.
    /// </summary>
    public static bool IsTrue(IEnumerable<IElement> elements)
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
    public static IEnumerable<IElement> ReturnBoolean(bool? result)
    {
        return result.HasValue
            ? [CreateBoolean(result.Value)]
            : [];
    }

    #endregion

    #region Equality Helpers

    /// <summary>
    /// Compares two values for equality.
    /// Handles date/time literals with @ prefix normalization and numeric type coercion.
    /// </summary>
    /// <remarks>
    /// Private because it cannot see an operand's instance type, and a FHIRPath temporal literal is a
    /// plain <see cref="string"/> until the instance type says otherwise. Callers must go through
    /// <see cref="AreElementsEqual"/>, which has the types and can route temporals to
    /// <see cref="TemporalOperand"/>; reaching this method directly is what made <c>distinct()</c>,
    /// <c>in</c>, <c>contains</c>, <c>intersect</c>, <c>exclude</c> and <c>|</c> compare temporals as
    /// text while the <c>=</c> operator compared them as instants.
    /// </remarks>
    private static bool AreEqual(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        if (WireValue.AsWireString(left) is { } leftStr && WireValue.AsWireString(right) is { } rightStr)
        {
            var leftNormalized = NormalizeDateString(leftStr);
            var rightNormalized = NormalizeDateString(rightStr);
            return leftNormalized == rightNormalized;
        }

        if (TryConvertToDecimal(left, out var leftDecimal) && TryConvertToDecimal(right, out var rightDecimal))
        {
            return leftDecimal == rightDecimal;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Compares two IElements for equality (deep comparison for complex types).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single equality entry point for every collection operation - <c>distinct()</c>,
    /// <c>isDistinct()</c>, <c>|</c>, <c>in</c>, <c>contains</c>, <c>intersect</c>, <c>exclude</c> and
    /// <c>repeat()</c> - and it deliberately answers the same question the <c>=</c> operator does.
    /// </para>
    /// <para>
    /// Both typed branches collapse the operators' third state to "not the same item", because these
    /// callers have no third state to return and membership asserts that an equal item is present.
    /// Quantities reach <see cref="ValueOrdering"/> for the same reason temporals reach
    /// <see cref="TemporalOperand"/>: without it a quantity fell through to
    /// <see cref="object.Equals(object)"/> on the carrier, which compares units as text, so
    /// <c>1 'm' = 100 'cm'</c> was <see langword="true"/> as an operator and <see langword="false"/> as
    /// membership. An undecided quantity comparison falls through to the untyped paths below rather than
    /// being reported unequal here, so the branch only ever adds a decision.
    /// </para>
    /// </remarks>
    public static bool AreElementsEqual(IElement? left, IElement? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        if (TemporalOperand.IsTemporal(left.Value, left.InstanceType)
            && TemporalOperand.IsTemporal(right.Value, right.InstanceType)
            && TemporalOperand.AsTemporal(left.Value, left.InstanceType) is { } leftTemporal
            && TemporalOperand.AsTemporal(right.Value, right.InstanceType) is { } rightTemporal)
        {
            return TemporalOperand.AreSameItem(leftTemporal, rightTemporal);
        }

        if ((ValueOrdering.IsQuantity(left) || ValueOrdering.IsQuantity(right))
            && ValueOrdering.AreQuantitiesEqual(left, right) is { } quantitiesEqual)
        {
            return quantitiesEqual;
        }

        // Both have primitive values - compare values
        if (left.Value != null && right.Value != null)
        {
            return AreEqual(left.Value, right.Value);
        }

        // One has value, one doesn't - not equal
        if (left.Value != null || right.Value != null)
        {
            return false;
        }

        // Both are complex types - compare children recursively
        var leftChildren = left.Children().OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        var rightChildren = right.Children().OrderBy(c => c.Name, StringComparer.Ordinal).ToList();

        if (leftChildren.Count != rightChildren.Count)
            return false;

        for (int i = 0; i < leftChildren.Count; i++)
        {
            // Children must have same name
            if (!string.Equals(leftChildren[i].Name, rightChildren[i].Name, StringComparison.Ordinal))
                return false;

            // Recursively compare
            if (!AreElementsEqual(leftChildren[i], rightChildren[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Normalizes date/time strings by ensuring consistent @ prefix handling.
    /// For date/time values, both "@2023-01-01" and "2023-01-01" should compare equal.
    /// </summary>
    private static string NormalizeDateString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.StartsWith('@'))
            return value.Substring(1);

        return value;
    }

    #endregion

    #region Type Validation Helpers

    /// <summary>
    /// Validates that the focus collection contains a single value with a string representation.
    /// </summary>
    /// <param name="focus">The input collection</param>
    /// <param name="functionName">The function name for error messages</param>
    /// <param name="str">The extracted string value if valid</param>
    /// <returns>True if valid, false if empty collection</returns>
    /// <exception cref="FhirPathEvaluationException">If collection has multiple items or a value with no string representation</exception>
    public static bool TryGetSingleString(IEnumerable<IElement> focus, string functionName, out string str)
    {
        str = string.Empty;
        var list = focus.ToList();

        if (list.Count == 0)
            return false;

        if (list.Count > 1)
            throw new FhirPathEvaluationException($"{functionName}() requires a single input value");

        if (WireValue.AsWireString(list[0].Value) is { } s)
        {
            str = s;
            return true;
        }

        var typeName = list[0].InstanceType ?? list[0].Value?.GetType().Name ?? "unknown";
        throw new FhirPathEvaluationException($"Function '{functionName}' is not supported on context type '{typeName}'");
    }

    /// <summary>
    /// Validates that the focus collection contains a single numeric value.
    /// </summary>
    /// <param name="focus">The input collection</param>
    /// <param name="functionName">The function name for error messages</param>
    /// <param name="value">The extracted decimal value if valid</param>
    /// <returns>True if valid, false if empty collection</returns>
    /// <exception cref="FhirPathEvaluationException">If collection has multiple items or non-numeric value</exception>
    /// <remarks>
    /// Overflow is reported as "no value" rather than as a type error. §Math requires an operation that
    /// overflows to yield empty, and a number too large for <see cref="decimal"/> is still a number - so
    /// letting it fall into the type-error path below would tell the caller its Decimal input was of an
    /// unsupported type.
    /// </remarks>
    public static bool TryGetSingleNumber(IEnumerable<IElement> focus, string functionName, out decimal value)
    {
        value = 0;
        var list = focus.ToList();

        if (list.Count == 0)
            return false;

        if (list.Count > 1)
            throw new FhirPathEvaluationException($"{functionName}() requires a single input value");

        try
        {
            if (TryConvertToDecimal(list[0].Value, out value))
                return true;
        }
        catch (OverflowException)
        {
            return false;
        }

        var typeName = list[0].InstanceType ?? list[0].Value?.GetType().Name ?? "unknown";
        throw new FhirPathEvaluationException($"Function '{functionName}' is not supported on context type '{typeName}'");
    }

    #endregion

    #region Type Conversion Helpers

    /// <summary>
    /// Converts a value to <see cref="decimal"/> when, and only when, it already is a number.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">The converted value, or zero when the value is not a number.</param>
    /// <returns><see langword="true"/> when the value is a number.</returns>
    /// <exception cref="OverflowException">
    /// The value is a number but falls outside <see cref="decimal"/>'s range.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The type list is exhaustive on purpose. This used to fall through to
    /// <c>Convert.ToDecimal(IConvertible)</c>, which both <see cref="string"/> and <see cref="bool"/>
    /// implement, so every arithmetic operator and every math function silently accepted them:
    /// <c>'5' + 1</c> answered <c>6</c>, <c>'5' - '1'</c> answered <c>4</c> and <c>1 + true</c> answered
    /// <c>2</c> where FHIRPath requires an error. String-to-Decimal and Boolean-to-Decimal are
    /// <i>explicit</i> conversions in the conversion table, reserved for <c>toDecimal()</c>; this PR made
    /// exactly that argument for <c>&amp;</c> and it applies unchanged to the six math operators.
    /// The string case gave no coverage from official <c>testMinus4</c> (<c>'a' - 'b'</c>) because
    /// <c>'a'</c> fails to parse whatever the rule is.
    /// </para>
    /// <para>
    /// The string case was also locale-dependent, which is the worse half. <c>Convert.ToDecimal</c> takes
    /// no <see cref="IFormatProvider"/>, so it parsed under <c>CurrentCulture</c>: <c>'1,5' + 1</c>
    /// answered <c>2.5</c> on a de-DE host and <c>16</c> on an en-US one, and <c>'1 234' + 1</c> answered
    /// <c>1235</c> on fr-FR and errored elsewhere. Same expression, same data, a different number per
    /// server locale, with nothing logged.
    /// </para>
    /// <para>
    /// Overflow is raised rather than reported as <see langword="false"/>. The old bare <c>catch</c>
    /// folded <see cref="OverflowException"/> into "not a number", which made
    /// <see cref="TryGetSingleNumber"/> report an out-of-range number as "not supported on context type" -
    /// a false diagnosis. §Math requires overflow to yield empty, and both callers route it there.
    /// </para>
    /// </remarks>
    public static bool TryConvertToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal d: result = d; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case short s: result = s; return true;
            case sbyte sb: result = sb; return true;
            case byte b: result = b; return true;
            case ushort us: result = us; return true;
            case uint ui: result = ui; return true;
            case ulong ul: result = ul; return true;
            case float f: result = (decimal)f; return true;
            case double dbl: result = (decimal)dbl; return true;
            default: result = 0; return false;
        }
    }

    #endregion

    #region Collection Helpers

    /// <summary>
    /// Returns the distinct elements of a collection under FHIRPath equality, preserving order.
    /// </summary>
    /// <remarks>
    /// A linear scan rather than <c>Enumerable.Distinct</c> with a comparer, because FHIRPath equality has
    /// no hash consistent with it: <c>@2012-01-01T10:00:00Z</c> and <c>@2012-01-01T20:00:00+10:00</c> are
    /// the same value with different literals, and <c>1</c> and <c>1.0</c> are the same value with
    /// different CLR types. The comparer this replaced hashed <c>Value.GetHashCode()</c> and compared with
    /// <c>Value.Equals</c>, so it could not see either equality and never even called the comparison for
    /// the first. FHIRPath collections are small enough that the quadratic scan is the right trade.
    /// </remarks>
    public static List<IElement> Distinct(IEnumerable<IElement> elements)
    {
        var result = new List<IElement>();

        foreach (var element in elements)
        {
            if (!result.Any(existing => AreElementsEqual(existing, element)))
            {
                result.Add(element);
            }
        }

        return result;
    }

    /// <summary>
    /// Union operator: Merge collections, eliminate duplicates.
    /// </summary>
    public static IEnumerable<IElement> EvaluateUnion(List<IElement> left, List<IElement> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return Distinct(left.Concat(right));
    }

    #endregion

    #region PrimitiveElement Implementation

    /// <summary>
    /// Simple implementation of IElement for primitive values produced by the evaluator.
    /// </summary>
    /// <remarks>
    /// Declares <see cref="ISystemValueElement"/>: everything built here is a System-namespace value
    /// (the <c>System.Integer</c> from <c>count()</c>, the <c>System.Boolean</c> from <c>exists()</c>,
    /// and so on), never a value read from a resource.
    /// </remarks>
    public class PrimitiveElement : ISystemValueElement
    {
        public PrimitiveElement(object value, string type, string name = "")
        {
            Value = value;
            InstanceType = type;
            Name = name;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public bool HasPrimitiveValue => true;

        // IElement members
        public IType? Type => null;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }

    #endregion

    #region QuantityElement Implementation

    /// <summary>
    /// IElement wrapper for Quantity values.
    /// Used by aggregate, math, boundary, and conversion functions.
    /// </summary>
    public sealed class QuantityElement : ISystemValueElement
    {
        private readonly FhirQuantity _quantity;

        public QuantityElement(FhirQuantity quantity)
        {
            ArgumentNullException.ThrowIfNull(quantity);
            _quantity = quantity;
        }

        public string Name => string.Empty;
        public string InstanceType => "Quantity";
        public object Value => _quantity;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => false; // Quantity is a complex type, not a primitive

        public T? Meta<T>() where T : class => null;

        /// <summary>
        /// Returns child elements for the Quantity: value, unit/code, and system.
        /// </summary>
        public IReadOnlyList<IElement> Children(string? name = null)
        {
            var children = new List<IElement>();

            if (name == null || name == "value")
                children.Add(new PrimitiveElement(_quantity.Value, "decimal", "value"));

            if (name == null || name == "unit")
                children.Add(new PrimitiveElement(_quantity.Unit, "string", "unit"));

            if (name == null || name == "code")
                children.Add(new PrimitiveElement(_quantity.Unit, "string", "code"));

            if (name == null || name == "system")
                children.Add(new PrimitiveElement("http://unitsofmeasure.org", "uri", "system"));

            return children;
        }
    }

    /// <summary>
    /// Creates an IElement wrapping a Quantity value.
    /// </summary>
    public static IElement CreateQuantity(FhirQuantity quantity) => new QuantityElement(quantity);

    #endregion
}
