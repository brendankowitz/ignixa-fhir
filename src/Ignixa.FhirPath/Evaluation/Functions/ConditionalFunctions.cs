/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath conditional function implementations.
 * Implements iif() (if-then-else conditional).
 */

using Ignixa.Abstractions;
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
    public static IEnumerable<ITypedElement> Iif(
        IEnumerable<ITypedElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<ITypedElement>, Expression, EvaluationContext, IEnumerable<ITypedElement>> evaluateExpression)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("iif() requires at least criterion and true-result arguments");

        var criterion = evaluateExpression(focus, arguments[0], context).ToList();

        // Empty condition returns empty
        if (criterion.Count == 0)
            return Enumerable.Empty<ITypedElement>();

        // True condition returns true branch
        if (FunctionHelpers.IsTrue(criterion))
        {
            return evaluateExpression(focus, arguments[1], context);
        }

        // False condition returns false branch (if provided)
        if (arguments.Count > 2)
        {
            return evaluateExpression(focus, arguments[2], context);
        }

        return Enumerable.Empty<ITypedElement>();
    }
}
