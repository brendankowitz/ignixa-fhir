/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath conditional function implementations.
 * Implements iif() (if-then-else conditional).
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Conditional function implementations for FhirPath expressions.
/// </summary>
internal static class ConditionalFunctions
{
    /// <summary>
    /// iif() - Conditional expression (if-then-else).
    /// Syntax: iif(criterion, true-result [, false-result])
    /// </summary>
    [FhirPathFunction("iif",
        SupportedContexts = "any-any",
        ReturnType = "fromArgument",
        MinArguments = 2,
        MaxArguments = 3,
        TakesExpressionArguments = true,
        Category = "Conditional",
        Description = "Conditional expression (if-then-else)")]
    public static IEnumerable<IElement> Iif(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("iif() requires at least criterion and true-result arguments");

        var focusList = focus.ToList();

        if (focusList.Count > 1)
        {
            throw new InvalidOperationException(
                $"iif() cannot be invoked on a collection with {focusList.Count} items - it requires empty or single item focus");
        }

        var innerContext = focusList.Count == 1
            ? context.PushThis(focusList[0])
            : context;
        // Only push index if not already in a scoped context (i.e., index stack is empty)
        if (context.GetIndex() is null)
        {
            innerContext = innerContext.PushIndex(0);
        }

        var criterion = evaluateExpression(focus, arguments[0], innerContext).ToList();

        bool? condition;

        if (criterion.Count == 0)
        {
            condition = false;
        }
        else if (criterion.Count == 1 && criterion[0].Value is bool b)
        {
            condition = b;
        }
        else if (criterion.Count == 1)
        {
            throw new InvalidOperationException(
                $"iif() condition must evaluate to a Boolean, but got a single {criterion[0].InstanceType ?? "unknown"} value");
        }
        else
        {
            throw new InvalidOperationException(
                $"iif() condition must evaluate to a single Boolean, but got a collection with {criterion.Count} items");
        }

        // True condition returns true branch
        if (condition == true)
        {
            return evaluateExpression(focus, arguments[1], innerContext);
        }

        // False condition (or empty) returns false branch (if provided)
        if (arguments.Count > 2)
        {
            return evaluateExpression(focus, arguments[2], innerContext);
        }

        return [];
    }
}
