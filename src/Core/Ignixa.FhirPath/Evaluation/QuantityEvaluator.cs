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
using System.Globalization;
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
        var quantity = new FhirQuantity(quantityExpr.Value, quantityExpr.Unit);

        // Wrap in a QuantityElement (IElement implementation)
        yield return Functions.FunctionHelpers.CreateQuantity(quantity);
    }

    /// <summary>
    /// Evaluates arithmetic operations where at least one operand is a Quantity.
    /// </summary>
    /// <param name="left">Left operand collection</param>
    /// <param name="op">Binary operator</param>
    /// <param name="right">Right operand collection</param>
    /// <returns>The result, or empty when the operation is defined but has no answer.</returns>
    /// <exception cref="FhirPathEvaluationException">
    /// The operator is not defined for the operand types, or an operand is not a singleton.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The distinction this method has to keep straight is between "defined but with no answer" and "not
    /// defined at all". <c>1 'mg' + 1 'm'</c> is the first: <c>+</c> is defined for two Quantities and the
    /// spec's unit-conversion rule resolves incomparable units to empty. <c>1 'mg' + 5</c> is the second:
    /// <c>+</c> on a Quantity is defined only for a Quantity operand, so an Integer is one of §Math's
    /// "incompatible items" and the evaluation must signal an error.
    /// </para>
    /// <para>
    /// Everything unimplemented used to fall out of here as empty, which collapsed those two cases onto
    /// the same answer. That is not a cosmetic difference: <c>FhirPathInvariantCheck.IsResultTrue</c> maps
    /// empty to <see langword="false"/>, so a mistyped operand in a quantity-valued invariant rejected the
    /// resource instead of reporting that the constraint could not be evaluated.
    /// </para>
    /// </remarks>
    public static IEnumerable<IElement> EvaluateArithmetic(
        IReadOnlyList<IElement> left,
        string op,
        IReadOnlyList<IElement> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count == 0 || right.Count == 0)
            return [];

        if (left.Count > 1 || right.Count > 1)
        {
            throw new FhirPathEvaluationException(
                $"Operator '{op}' requires singleton operands, but was given {left.Count} item(s) on the left and {right.Count} item(s) on the right.");
        }

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Handle quantity + quantity, quantity - quantity, quantity * quantity, quantity / quantity
        if (leftValue is FhirQuantity leftQty && rightValue is FhirQuantity rightQty)
        {
            return op switch
            {
                "+" => EvaluateQuantityAddition(leftQty, rightQty),
                "-" => EvaluateQuantitySubtraction(leftQty, rightQty),
                "*" => EvaluateQuantityMultiplication(leftQty, rightQty),
                "/" => EvaluateQuantityDivision(leftQty, rightQty),
                _ => throw FhirPathEvaluator.UndefinedForOperandTypes(left[0], right[0], op)
            };
        }

        // Handle quantity * scalar, scalar * quantity
        if (leftValue is FhirQuantity leftQuantity && IsScalar(rightValue) && rightValue != null)
        {
            if (op == "*")
                return EvaluateQuantityScalarMultiply(leftQuantity, ToDecimal(rightValue));
            if (op == "/")
                return EvaluateQuantityScalarDivide(leftQuantity, ToDecimal(rightValue));
        }

        if (IsScalar(leftValue) && leftValue != null && rightValue is FhirQuantity rightQuantity && op == "*")
        {
            return EvaluateQuantityScalarMultiply(rightQuantity, ToDecimal(leftValue));
        }

        throw FhirPathEvaluator.UndefinedForOperandTypes(left[0], right[0], op);
    }

    /// <summary>
    /// Evaluates comparison operations where at least one operand is a Quantity.
    /// </summary>
    /// <param name="left">Left operand collection</param>
    /// <param name="op">Comparison operator (=, !=, <, <=, >, >=)</param>
    /// <param name="right">Right operand collection</param>
    /// <returns>Boolean result, or null when the comparison is undecidable and must yield empty.</returns>
    /// <exception cref="FhirPathEvaluationException">
    /// An ordering operator was applied to a Quantity and an operand that no implicit conversion can make
    /// a Quantity, or an operand was not a singleton.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A bare number is a Quantity in the unity unit: the FHIRPath conversion table makes Integer and
    /// Decimal <i>implicitly</i> convertible to Quantity, so <c>1 'mg' &gt; 5</c> is <c>1 'mg'</c> against
    /// <c>5 '1'</c> and yields empty by the unit-compatibility rule below rather than by "the right
    /// operand is not a Quantity". Same answer, but for a reason that also gets <c>5 '1' = 5</c> right,
    /// and one that does not swallow the operands no conversion reaches.
    /// </para>
    /// <para>
    /// A String, Boolean or temporal operand has no such conversion, so it is a genuine type mismatch.
    /// Ordering signals an error there, exactly as <c>Observation.value.value &lt; 'test'</c> does on the
    /// non-Quantity path (official <c>testLiteralDecimalLessThanInvalid</c>); this branch sits above that
    /// check in the evaluator and used to return empty instead, which
    /// <c>FhirPathInvariantCheck.IsResultTrue</c> then reported as a failed constraint. Equality does not
    /// error: FHIRPath equality between values of different types is decidably <see langword="false"/>,
    /// not undecidable.
    /// </para>
    /// </remarks>
    public static bool? EvaluateComparison(
        IReadOnlyList<IElement> left,
        string op,
        IReadOnlyList<IElement> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count == 0 || right.Count == 0)
            return null;

        if (left.Count > 1 || right.Count > 1)
        {
            throw new FhirPathEvaluationException(
                $"Operator '{op}' requires singleton operands, but was given {left.Count} item(s) on the left and {right.Count} item(s) on the right.");
        }

        // Try to extract Quantity from elements (handles both FhirPath literals and FHIR Quantity elements)
        var leftQty = ExtractQuantity(left[0]);
        var rightQty = ExtractQuantity(right[0]);

        if (leftQty == null || rightQty == null)
        {
            return op is "=" or "!="
                ? op == "!="
                : throw FhirPathEvaluator.UndefinedForOperandTypes(left[0], right[0], op);
        }

        // Incompatible units are the spec's own empty result rather than an ordering.
        if (Functions.ValueOrdering.CompareQuantityValues(leftQty, rightQty) is not { } order)
            return null;

        return op switch
        {
            "=" => order == 0,
            "!=" => order != 0,
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            ">=" => order >= 0,
            _ => throw FhirPathEvaluator.UndefinedForOperandTypes(left[0], right[0], op)
        };
    }

    /// <summary>
    /// Extracts a Quantity from an IElement, handling FhirPath Quantity literals, FHIR Quantity elements
    /// (which have value/unit/code children), and the implicit conversion from a bare number.
    /// </summary>
    /// <remarks>
    /// The rule itself lives in <see cref="Functions.ValueOrdering.AsQuantity"/> so that the comparison
    /// operators, <c>sort()</c> and the aggregate functions cannot disagree about which operands are
    /// quantities. They previously did: this method's <see cref="IsScalar"/> admitted
    /// <see cref="double"/> and <see cref="float"/> while the ordering path did not.
    /// </remarks>
    private static FhirQuantity? ExtractQuantity(IElement element)
        => Functions.ValueOrdering.AsQuantity(element);

    /// <summary>
    /// Determines whether a declared type is one of the FHIR Quantity flavours, whose value and unit are
    /// children rather than the element's own value.
    /// </summary>
    internal static bool IsQuantityInstanceType(string? instanceType)
    {
        return instanceType is not null
            && (instanceType.Equals("quantity", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("age", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("distance", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("duration", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("count", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("simplequantity", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("moneyquantity", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts value and unit from a FHIR Quantity element's children.
    /// </summary>
    /// <remarks>
    /// A Quantity carrying a value but no unit is the unity unit, the same reading
    /// <see cref="Functions.ValueOrdering.AsQuantity"/> already gives a bare number. Returning
    /// <see langword="null"/> for it - as this did - made <c>Observation.value = Observation.value</c>
    /// answer <see langword="false"/> on such an element, because the equality path reports an operand it
    /// cannot read as a quantity as decidably unequal, while <c>~</c> answered <see langword="true"/> and
    /// <c>&lt;</c> threw. Three readings of one element, three answers.
    /// </remarks>
    internal static FhirQuantity? ExtractQuantityFromChildren(IElement element)
    {
        decimal? value = null;
        string? unit = null;

        var children = element.Children();
        foreach (var child in children)
        {
            if (child.Name == "value" && child.Value != null)
            {
                if (TryReadMagnitude(child.Value, out var magnitude))
                {
                    value = magnitude;
                }
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

        return value.HasValue
            ? new FhirQuantity(value.Value, string.IsNullOrEmpty(unit) ? "1" : unit)
            : null;
    }

    /// <summary>
    /// Reads a Quantity's <c>value</c> child as a <see cref="decimal"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider is fixed at <see cref="CultureInfo.InvariantCulture"/> because the FHIR wire format
    /// is: <c>.</c> is the decimal separator and group separators never appear. Reading the text under
    /// the host's culture made <c>"1.5"</c> a well-formed fifteen on a comma-decimal host - silently,
    /// with nothing thrown and nothing logged. <see cref="NumberStyles.Float"/> matches
    /// <c>SchemaAwareElement</c>, which is where the same text is read when the schema knows the type:
    /// it excludes group separators and admits the exponent a FHIR decimal may carry.
    /// </para>
    /// <para>
    /// A <see cref="double"/> is narrowed rather than cast. The cast throws
    /// <see cref="OverflowException"/> for a non-finite or out-of-range value, and this method sits on
    /// the path <c>=</c>, <c>~</c>, <c>&lt;</c>, <c>sort()</c> and the aggregates all share, outside the
    /// one <c>catch</c> that turns overflow into FHIRPath's empty. A refusal here leaves the element
    /// unreadable as a quantity, which those callers already answer with empty - the spec's result for
    /// arithmetic overflow - rather than by rejecting a conformant resource.
    /// </para>
    /// </remarks>
    private static bool TryReadMagnitude(object raw, out decimal value)
    {
        switch (raw)
        {
            case decimal decimalValue:
                value = decimalValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case double doubleValue:
                return Functions.ValueOrdering.TryNarrow(doubleValue, out value);
            case float floatValue:
                return Functions.ValueOrdering.TryNarrow(floatValue, out value);
            case string text:
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                value = 0m;
                return false;
        }
    }

    #region Private Helpers

    private static IEnumerable<IElement> EvaluateQuantityAddition(FhirQuantity left, FhirQuantity right)
    {
        var result = left.Add(right, UnitConverter);
        return result != null
            ? [Functions.FunctionHelpers.CreateQuantity(result)]
            : [];
    }

    private static IEnumerable<IElement> EvaluateQuantitySubtraction(FhirQuantity left, FhirQuantity right)
    {
        var result = left.Subtract(right, UnitConverter);
        return result != null
            ? [Functions.FunctionHelpers.CreateQuantity(result)]
            : [];
    }

    private static IEnumerable<IElement> EvaluateQuantityMultiplication(FhirQuantity left, FhirQuantity right)
    {
        var result = UnitConverter.Multiply(left, right);
        return result != null
            ? [Functions.FunctionHelpers.CreateQuantity(result)]
            : [];
    }

    private static IEnumerable<IElement> EvaluateQuantityScalarMultiply(FhirQuantity quantity, decimal scalar)
    {
        var result = quantity.Multiply(scalar);
        return [Functions.FunctionHelpers.CreateQuantity(result)];
    }

    private static IEnumerable<IElement> EvaluateQuantityScalarDivide(FhirQuantity quantity, decimal scalar)
    {
        var result = quantity.DivideByScalar(scalar);
        return result != null
            ? [Functions.FunctionHelpers.CreateQuantity(result)]
            : [];
    }

    private static IEnumerable<IElement> EvaluateQuantityDivision(FhirQuantity left, FhirQuantity right)
    {
        var result = UnitConverter.Divide(left, right);
        return result != null
            ? [Functions.FunctionHelpers.CreateQuantity(result)]
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

    private static IElement CreateDecimal(decimal value) => new Functions.FunctionHelpers.PrimitiveElement(value, "decimal");

    #endregion
}
