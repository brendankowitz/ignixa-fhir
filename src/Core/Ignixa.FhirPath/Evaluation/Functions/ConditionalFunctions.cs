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
    // Force rebuild
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

        // For iif(), $this should refer to the focus collection
        // If focus is a single element, $this resolves to that element
        // Per spec: When iif is the primary scoped function, $index = 0.
        // But when nested inside another scoped function (like select), preserve outer $index.
        var focusList = focus.ToList();
        var innerContext = focusList.Count == 1
            ? context.PushThis(focusList[0])
            : context;
        // Only push index if not already in a scoped context (i.e., index stack is empty)
        if (context.GetIndex() is null)
        {
            innerContext = innerContext.PushIndex(0);
        }

        var criterion = evaluateExpression(focus, arguments[0], innerContext).ToList();

        // Evaluate condition using FHIRPath boolean semantics
        // Per FHIRPath spec: If criterion is false OR an empty collection, return otherwise-result
        bool? condition;

        if (criterion.Count == 0)
        {
            // Empty collection is treated as false for iif
            condition = false;
        }
        else if (criterion.Count == 1 && criterion[0].Value is bool b)
        {
            condition = b;
        }
        else
        {
            // Non-empty collection (including non-boolean values) is truthy per FHIRPath
            condition = true;
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
