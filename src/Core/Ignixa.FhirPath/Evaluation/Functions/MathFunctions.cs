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
        if (!FunctionHelpers.TryGetSingleNumber(focus, "round", out var value))
            return [];

        int precision = 0;
        if (arguments.Count > 0)
        {
            // Non-scoped function: evaluate argument in outer context (don't change $this)
            var precisionResult = evaluateExpression(context.Focus, arguments[0], context).SingleOrDefault();
            if (precisionResult?.Value is int p)
                precision = p;
            else
                return [];
        }

        var rounded = Math.Round(value, precision, MidpointRounding.AwayFromZero);

        var firstElement = focus.First();
        if (precision == 0 && firstElement.Value is int)
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

        if (!FunctionHelpers.TryGetSingleNumber(focus, "power", out var baseValue))
            return [];

        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var exponentResult = evaluateExpression(context.Focus, arguments[0], context).SingleOrDefault();
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
        if (!FunctionHelpers.TryGetSingleNumber(focus, "floor", out var value))
            return [];

        var result = Math.Floor(value);
        if (result < int.MinValue || result > int.MaxValue)
            return [];
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
        if (!FunctionHelpers.TryGetSingleNumber(focus, "ceiling", out var value))
            return [];

        var result = Math.Ceiling(value);
        if (result < int.MinValue || result > int.MaxValue)
            return [];
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
        if (!FunctionHelpers.TryGetSingleNumber(focus, "truncate", out var value))
            return [];

        var result = Math.Truncate(value);
        if (result < int.MinValue || result > int.MaxValue)
            return [];
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
        if (list.Count == 0)
            return [];

        if (list.Count > 1)
            throw new InvalidOperationException("abs() requires a single input value");

        // Handle Quantity types
        if (list[0].Value is Types.Quantity qty)
        {
            var absQty = new Types.Quantity(Math.Abs(qty.Value), qty.Unit);
            return [FunctionHelpers.CreateQuantity(absQty)];
        }

        if (list[0].Value is int intValue)
            return [FunctionHelpers.CreateInteger(Math.Abs(intValue))];

        if (FunctionHelpers.TryConvertToDecimal(list[0].Value, out var value))
            return [FunctionHelpers.CreateDecimal(Math.Abs(value))];

        var typeName = list[0].InstanceType ?? list[0].Value?.GetType().Name ?? "unknown";
        throw new InvalidOperationException($"Function 'abs' is not supported on context type '{typeName}'");
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
        if (!FunctionHelpers.TryGetSingleNumber(focus, "sqrt", out var value))
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
        if (!FunctionHelpers.TryGetSingleNumber(focus, "exp", out var value))
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
        if (!FunctionHelpers.TryGetSingleNumber(focus, "ln", out var value))
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

        if (!FunctionHelpers.TryGetSingleNumber(focus, "log", out var value))
            return [];

        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var baseResult = evaluateExpression(context.Focus, arguments[0], context).SingleOrDefault();
        if (!FunctionHelpers.TryConvertToDecimal(baseResult?.Value, out var baseValue))
            return [];

        if (value <= 0 || baseValue <= 0 || baseValue == 1)
            return [];

        var result = (decimal)Math.Log((double)value, (double)baseValue);
        return [FunctionHelpers.CreateDecimal(result)];
    }
}
