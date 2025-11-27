/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath Quantity evaluation logic.
 * Handles quantity literals, arithmetic operations, and comparisons.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ignixa.FhirPath.Evaluation;

#nullable enable

/// <summary>
/// Evaluates FhirPath Quantity expressions and operations.
/// Supports calendar duration keyword parsing with strict compatibility rules.
/// </summary>
internal static class QuantityEvaluator
{
    private static readonly IQuantityUnitConverter UnitConverter = QuantityUnitConverter.Instance;

    /// <summary>
    /// Evaluates a QuantityExpression to an IElement.
    /// </summary>
    /// <param name="quantityExpr">The quantity expression</param>
    /// <returns>A single IElement representing the quantity</returns>
    public static IEnumerable<IElement> EvaluateQuantity(QuantityExpression quantityExpr)
    {
        ArgumentNullException.ThrowIfNull(quantityExpr);

        // Create a Quantity value object
        var quantity = new Quantity(quantityExpr.Value, quantityExpr.Unit);

        // Wrap in a QuantityElement (IElement implementation)
        yield return new QuantityElement(quantity);
    }

    /// <summary>
    /// Evaluates arithmetic operations on quantities.
    /// </summary>
    /// <param name="left">Left operand collection</param>
    /// <param name="op">Binary operator</param>
    /// <param name="right">Right operand collection</param>
    /// <returns>Result of the arithmetic operation, or empty if operands are incompatible</returns>
    public static IEnumerable<IElement> EvaluateArithmetic(
        IReadOnlyList<IElement> left,
        string op,
        IReadOnlyList<IElement> right)
    {
        // FhirPath arithmetic requires single values on both sides
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Handle quantity + quantity, quantity - quantity, quantity / quantity
        if (leftValue is Quantity leftQty && rightValue is Quantity rightQty)
        {
            return op switch
            {
                "+" => EvaluateQuantityAddition(leftQty, rightQty),
                "-" => EvaluateQuantitySubtraction(leftQty, rightQty),
                "/" => EvaluateQuantityDivision(leftQty, rightQty),
                _ => []
            };
        }

        // Handle quantity * scalar, scalar * quantity
        if (leftValue is Quantity qty1 && IsScalar(rightValue) && rightValue != null)
        {
            if (op == "*")
                return EvaluateQuantityScalarMultiply(qty1, ToDecimal(rightValue));
            if (op == "/")
                return EvaluateQuantityScalarDivide(qty1, ToDecimal(rightValue));
        }

        if (IsScalar(leftValue) && leftValue != null && rightValue is Quantity qty2)
        {
            if (op == "*")
                return EvaluateQuantityScalarMultiply(qty2, ToDecimal(leftValue));
        }

        return [];
    }

    /// <summary>
    /// Evaluates comparison operations on quantities.
    /// </summary>
    /// <param name="left">Left operand collection</param>
    /// <param name="op">Comparison operator (=, !=, <, <=, >, >=)</param>
    /// <param name="right">Right operand collection</param>
    /// <returns>Boolean result, or null if comparison is not possible</returns>
    public static bool? EvaluateComparison(
        IReadOnlyList<IElement> left,
        string op,
        IReadOnlyList<IElement> right)
    {
        // FhirPath comparisons require single values on both sides
        if (left.Count != 1 || right.Count != 1)
            return null;

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Both must be quantities
        if (leftValue is not Quantity leftQty || rightValue is not Quantity rightQty)
            return null;

        // Check if units are compatible (can be converted)
        if (!UnitConverter.IsCompatible(leftQty.Unit, rightQty.Unit))
            return null; // Incompatible units => empty result

        // Convert right to left's unit for comparison
        var convertedRight = rightQty.ConvertTo(leftQty.Unit, UnitConverter);
        if (convertedRight == null)
            return null;

        // Perform the comparison
        return op switch
        {
            "=" => leftQty.Equals(convertedRight),
            "!=" => !leftQty.Equals(convertedRight),
            "<" => leftQty.CompareTo(convertedRight) < 0,
            "<=" => leftQty.CompareTo(convertedRight) <= 0,
            ">" => leftQty.CompareTo(convertedRight) > 0,
            ">=" => leftQty.CompareTo(convertedRight) >= 0,
            _ => null
        };
    }

    #region Private Helpers

    private static IEnumerable<IElement> EvaluateQuantityAddition(Quantity left, Quantity right)
    {
        var result = left.Add(right, UnitConverter);
        return result != null
            ? [new QuantityElement(result)]
            : [];
    }

    private static IEnumerable<IElement> EvaluateQuantitySubtraction(Quantity left, Quantity right)
    {
        var result = left.Subtract(right, UnitConverter);
        return result != null
            ? [new QuantityElement(result)]
            : [];
    }

    private static IEnumerable<IElement> EvaluateQuantityScalarMultiply(Quantity quantity, decimal scalar)
    {
        var result = quantity.Multiply(scalar);
        return [new QuantityElement(result)];
    }

    private static IEnumerable<IElement> EvaluateQuantityScalarDivide(Quantity quantity, decimal scalar)
    {
        var result = quantity.DivideByScalar(scalar);
        return result != null
            ? [new QuantityElement(result)]
            : [];
    }

    private static IEnumerable<IElement> EvaluateQuantityDivision(Quantity left, Quantity right)
    {
        var result = left.DivideBy(right, UnitConverter);
        return result != null
            ? [CreateDecimal(result.Value)]
            : [];
    }

    private static bool IsScalar(object? value)
    {
        return value is int or long or decimal or double or float;
    }

    private static decimal ToDecimal(object value)
    {
        return value switch
        {
            decimal d => d,
            int i => i,
            long l => l,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            _ => throw new InvalidOperationException($"Cannot convert {value?.GetType().Name ?? "null"} to decimal")
        };
    }

    private static IElement CreateDecimal(decimal value) => new PrimitiveElement(value, "decimal");

    #endregion

    #region IElement Implementations

    /// <summary>
    /// IElement wrapper for Quantity values.
    /// </summary>
    private class QuantityElement : IElement
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
        public IType? Type => null;

        public T? Meta<T>() where T : class => null;

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            // Quantity can have child elements: value, unit, system, code
            var children = new List<IElement>();

            if (name == null || name == "value")
                children.Add(new PrimitiveElement(_quantity.Value, "decimal"));

            if (name == null || name == "unit" || name == "code")
                children.Add(new PrimitiveElement(_quantity.Unit, "string"));

            if (name == null || name == "system")
                children.Add(new PrimitiveElement("http://unitsofmeasure.org", "uri"));

            return children;
        }
    }

    /// <summary>
    /// IElement wrapper for primitive values.
    /// </summary>
    private class PrimitiveElement : IElement
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
        public IType? Type => null;

        public T? Meta<T>() where T : class => null;

        public IReadOnlyList<IElement> Children(string? name = null) => [];
    }

    #endregion
}
