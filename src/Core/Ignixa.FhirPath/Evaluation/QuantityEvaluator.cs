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

        // Handle quantity + quantity, quantity - quantity, quantity * quantity, quantity / quantity
        if (leftValue is Quantity leftQty && rightValue is Quantity rightQty)
        {
            return op switch
            {
                "+" => EvaluateQuantityAddition(leftQty, rightQty),
                "-" => EvaluateQuantitySubtraction(leftQty, rightQty),
                "*" => EvaluateQuantityMultiplication(leftQty, rightQty),
                "/" => EvaluateQuantityDivision(leftQty, rightQty),
                _ => []
            };
        }

        // Handle quantity * scalar, scalar * quantity
        if (leftValue is Quantity leftQuantity && IsScalar(rightValue) && rightValue != null)
        {
            if (op == "*")
                return EvaluateQuantityScalarMultiply(leftQuantity, ToDecimal(rightValue));
            if (op == "/")
                return EvaluateQuantityScalarDivide(leftQuantity, ToDecimal(rightValue));
        }

        if (IsScalar(leftValue) && leftValue != null && rightValue is Quantity rightQuantity)
        {
            if (op == "*")
                return EvaluateQuantityScalarMultiply(rightQuantity, ToDecimal(leftValue));
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

        // Try to extract Quantity from elements (handles both FhirPath literals and FHIR Quantity elements)
        var leftQty = ExtractQuantity(left[0]);
        var rightQty = ExtractQuantity(right[0]);

        // Both must be quantities
        if (leftQty == null || rightQty == null)
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

    /// <summary>
    /// Extracts a Quantity from an IElement, handling both FhirPath Quantity literals
    /// and FHIR Quantity elements (which have value/unit/code children).
    /// </summary>
    private static Quantity? ExtractQuantity(IElement element)
    {
        // If the value is already a Quantity (FhirPath literal), return it directly
        if (element.Value is Quantity qty)
            return qty;

        // If it's a FHIR Quantity element, extract value and unit from children
#pragma warning disable CA1308 // Normalize strings to uppercase - FHIR type names are case-insensitive
        var instanceType = element.InstanceType?.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
        if (instanceType == "quantity" || instanceType == "age" || instanceType == "distance" || 
            instanceType == "duration" || instanceType == "count" || instanceType == "simplequantity" ||
            instanceType == "moneyquantity")
        {
            return ExtractQuantityFromFhirElement(element);
        }

        return null;
    }

    /// <summary>
    /// Extracts value and unit from a FHIR Quantity element's children.
    /// </summary>
    private static Quantity? ExtractQuantityFromFhirElement(IElement element)
    {
        decimal? value = null;
        string? unit = null;

        var children = element.Children();
        foreach (var child in children)
        {
            if (child.Name == "value" && child.Value != null)
            {
                if (child.Value is decimal d)
                    value = d;
                else if (child.Value is int i)
                    value = i;
                else if (child.Value is long l)
                    value = l;
                else if (child.Value is double dbl)
                    value = (decimal)dbl;
                else if (child.Value is string s && decimal.TryParse(s, out var parsed))
                    value = parsed;
            }
            else if (child.Name == "code" && child.Value is string code)
            {
                // Prefer 'code' over 'unit' as it's the UCUM code
                unit = code;
            }
            else if (child.Name == "unit" && unit == null && child.Value is string unitStr)
            {
                // Fall back to 'unit' if 'code' is not present
                unit = unitStr;
            }
        }

        if (value.HasValue && !string.IsNullOrEmpty(unit))
        {
            return new Quantity(value.Value, unit);
        }

        return null;
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

    private static IEnumerable<IElement> EvaluateQuantityMultiplication(Quantity left, Quantity right)
    {
        var result = UnitConverter.Multiply(left, right);
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
        var result = UnitConverter.Divide(left, right);
        return result != null
            ? [new QuantityElement(result)]
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
