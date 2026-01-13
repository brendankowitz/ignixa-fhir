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
        var focusList = focus.ToList();
        var innerContext = focusList.Count == 1
            ? context.PushThis(focusList[0])
            : context;

        var criterion = evaluateExpression(focus, arguments[0], innerContext).ToList();

        // Empty condition returns empty
        if (criterion.Count == 0)
            return [];

        // True condition returns true branch
        if (FunctionHelpers.IsTrue(criterion))
        {
            return evaluateExpression(focus, arguments[1], innerContext);
        }

        // False condition returns false branch (if provided)
        if (arguments.Count > 2)
        {
            return evaluateExpression(focus, arguments[2], innerContext);
        }

        return [];
    }
}
