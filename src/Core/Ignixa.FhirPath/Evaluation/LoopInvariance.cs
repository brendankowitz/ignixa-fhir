// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Recognises subexpressions whose value is fixed for a whole evaluation, so the evaluator can compute them once.
/// </summary>
/// <remarks>
/// <para>
/// The case that needs it is <c>dom-3</c>: <c>contained.where('#'+id in (%resource.descendants().reference | ...))</c>
/// walks the whole resource four times for every contained resource. Charged honestly, that is quadratic in the
/// size of a Questionnaire with many contained ValueSets, and the step budget would fail a legitimate resource.
/// The right operand does not depend on the contained resource being tested, so it is the same collection on
/// every iteration.
/// </para>
/// <para>
/// The test is deliberately narrow, because a subexpression wrongly judged invariant would return a stale value. An
/// expression is invariant when it is rooted at <c>%resource</c>, <c>%rootResource</c> or <c>%context</c> - which are
/// fixed for an evaluation and cannot be redefined - and continues only through child navigation, parentheses,
/// <c>|</c> of invariant operands, and function calls with no arguments or with a type name as their only argument
/// (<c>as</c>, <c>ofType</c>, <c>is</c>). Any other argument could read the enclosing iteration's focus, <c>$this</c>
/// or a variable, so it disqualifies the expression. <c>resolve()</c> is excluded because it calls host code.
/// </para>
/// </remarks>
internal static class LoopInvariance
{
    private static readonly ConditionalWeakTable<Expression, StrongBox<bool>> Verdicts = new();

    /// <summary>
    /// Whether <paramref name="expression"/> is invariant and does enough work to be worth remembering: a bare
    /// variable reference is invariant but costs nothing to re-read.
    /// </summary>
    public static bool IsWorthMemoising(Expression expression) =>
        expression is not VariableRefExpression && Verdicts.GetValue(expression, e => new StrongBox<bool>(IsInvariant(e))).Value;

    private static bool IsInvariant(Expression expression)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        return expression switch
        {
            VariableRefExpression variable => variable.Name is "resource" or "rootResource" or "context",
            ParenthesizedExpression parenthesized => IsInvariant(parenthesized.InnerExpression),
            BinaryExpression binary => binary.Operator == "|" && IsInvariant(binary.Left) && IsInvariant(binary.Right),
            ChildExpression child => child.Focus is { } focus && IsInvariant(focus),
            PropertyAccessExpression property => property.Focus is { } focus && IsInvariant(focus),
            IndexerExpression or UnaryExpression => false,
            FunctionCallExpression call => call.Focus is { } focus && IsInvariant(focus) && HasInvariantArguments(call),
            _ => false,
        };
    }

    private static bool HasInvariantArguments(FunctionCallExpression call)
    {
        if (call.FunctionName.Equals("resolve", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (call.Arguments.Count == 0)
        {
            return true;
        }

        return call.Arguments.Count == 1
            && call.Arguments[0] is IdentifierExpression
            && (call.FunctionName.Equals("as", StringComparison.OrdinalIgnoreCase)
                || call.FunctionName.Equals("ofType", StringComparison.OrdinalIgnoreCase)
                || call.FunctionName.Equals("is", StringComparison.OrdinalIgnoreCase));
    }
}
