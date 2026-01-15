/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath math function implementations.
 * Implements round(), power(), floor(), ceiling(), truncate(), abs(), sqrt(), exp(), ln(), log().
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Math function implementations for FhirPath expressions.
/// </summary>
internal static class MathFunctions
{
    /// <summary>
    /// round() - Rounds to the nearest integer or to the specified precision.
    /// </summary>
    [FhirPathFunction("round",
        SupportedContexts = "number-number",
        ReturnType = "context",
        MinArguments = 0,
        MaxArguments = 1,
        Category = "Math",
        Description = "Rounds to the nearest integer or to the specified precision")]
    public static IEnumerable<IElement> Round(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        int precision = 0;
        if (arguments.Count > 0)
        {
            var precisionResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
            if (precisionResult?.Value is int p)
                precision = p;
            else
                return [];
        }

        var rounded = Math.Round(value, precision, MidpointRounding.AwayFromZero);

        if (precision == 0 && list[0].Value is int)
            return [FunctionHelpers.CreateInteger((int)rounded)];

        return [FunctionHelpers.CreateDecimal(rounded)];
    }

    /// <summary>
    /// power() - Raises a number to the specified exponent.
    /// </summary>
    [FhirPathFunction("power",
        SupportedContexts = "number-number",
        ReturnType = "decimal",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Math",
        Description = "Raises a number to the specified exponent")]
    public static IEnumerable<IElement> Power(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("power() requires an exponent argument");

        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var baseValue))
            return [];

        var exponentResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (!FunctionHelpers.TryConvertToDecimal(exponentResult?.Value, out var exponent))
            return [];

        var result = Math.Pow((double)baseValue, (double)exponent);
        // Return empty for NaN (e.g., (-1)^0.5 which is imaginary) or Infinity
        if (double.IsNaN(result) || double.IsInfinity(result))
            return [];
        return [FunctionHelpers.CreateDecimal((decimal)result)];
    }

    /// <summary>
    /// floor() - Rounds down to the nearest integer.
    /// </summary>
    [FhirPathFunction("floor",
        SupportedContexts = "number-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Math",
        Description = "Rounds down to the nearest integer")]
    public static IEnumerable<IElement> Floor(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        var result = Math.Floor(value);
        return [FunctionHelpers.CreateInteger((int)result)];
    }

    /// <summary>
    /// ceiling() - Rounds up to the nearest integer.
    /// </summary>
    [FhirPathFunction("ceiling",
        SupportedContexts = "number-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Math",
        Description = "Rounds up to the nearest integer")]
    public static IEnumerable<IElement> Ceiling(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        var result = Math.Ceiling(value);
        return [FunctionHelpers.CreateInteger((int)result)];
    }

    /// <summary>
    /// truncate() - Removes the decimal part of a number.
    /// </summary>
    [FhirPathFunction("truncate",
        SupportedContexts = "number-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Math",
        Description = "Removes the decimal part of a number")]
    public static IEnumerable<IElement> Truncate(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        var result = Math.Truncate(value);
        return [FunctionHelpers.CreateInteger((int)result)];
    }

    /// <summary>
    /// abs() - Returns the absolute value of a number or quantity.
    /// </summary>
    [FhirPathFunction("abs",
        SupportedContexts = "number-number",
        ReturnType = "context",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Math",
        Description = "Returns the absolute value of a number or quantity")]
    public static IEnumerable<IElement> Abs(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        // Handle Quantity types
        if (list[0].Value is Types.Quantity qty)
        {
            var absQty = new Types.Quantity(Math.Abs(qty.Value), qty.Unit);
            return [new QuantityElement(absQty)];
        }

        if (list[0].Value is int intValue)
            return [FunctionHelpers.CreateInteger(Math.Abs(intValue))];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        return [FunctionHelpers.CreateDecimal(Math.Abs(value))];
    }

    /// <summary>
    /// sqrt() - Returns the square root of a number.
    /// </summary>
    [FhirPathFunction("sqrt",
        SupportedContexts = "number-decimal",
        ReturnType = "decimal",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Math",
        Description = "Returns the square root of a number")]
    public static IEnumerable<IElement> Sqrt(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        if (value < 0)
            return [];

        var result = (decimal)Math.Sqrt((double)value);
        return [FunctionHelpers.CreateDecimal(result)];
    }

    /// <summary>
    /// exp() - Returns e raised to the specified power (e^x).
    /// </summary>
    [FhirPathFunction("exp",
        SupportedContexts = "number-decimal",
        ReturnType = "decimal",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Math",
        Description = "Returns e raised to the specified power")]
    public static IEnumerable<IElement> Exp(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        var result = (decimal)Math.Exp((double)value);
        return [FunctionHelpers.CreateDecimal(result)];
    }

    /// <summary>
    /// ln() - Returns the natural logarithm (base e) of a number.
    /// </summary>
    [FhirPathFunction("ln",
        SupportedContexts = "number-decimal",
        ReturnType = "decimal",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Math",
        Description = "Returns the natural logarithm of a number")]
    public static IEnumerable<IElement> Ln(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        if (value <= 0)
            return [];

        var result = (decimal)Math.Log((double)value);
        return [FunctionHelpers.CreateDecimal(result)];
    }

    /// <summary>
    /// log() - Returns the logarithm of a number with the specified base.
    /// </summary>
    [FhirPathFunction("log",
        SupportedContexts = "number-decimal",
        ReturnType = "decimal",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Math",
        Description = "Returns the logarithm with specified base")]
    public static IEnumerable<IElement> Log(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("log() requires a base argument");

        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        if (!FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [];

        var baseResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (!FunctionHelpers.TryConvertToDecimal(baseResult?.Value, out var baseValue))
            return [];

        if (value <= 0 || baseValue <= 0 || baseValue == 1)
            return [];

        var result = (decimal)Math.Log((double)value, (double)baseValue);
        return [FunctionHelpers.CreateDecimal(result)];
    }

    /// <summary>
    /// Simple IElement wrapper for Quantity values.
    /// </summary>
    private class QuantityElement : IElement
    {
        private readonly Types.Quantity _quantity;

        public QuantityElement(Types.Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(quantity);
            _quantity = quantity;
        }

        public string Name => string.Empty;
        public string InstanceType => "Quantity";
        public object Value => _quantity;
        public string Location => string.Empty;
        public IType? Type => null;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
