// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
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
/// The test is deliberately narrow, because a subexpression wrongly judged invariant would return a stale value or
/// skip a side effect. An expression is invariant when it is rooted at <c>%resource</c>, <c>%rootResource</c> or
/// <c>%context</c> - which are fixed for an evaluation and cannot be redefined - and continues only through child
/// navigation, parentheses, <c>|</c> of invariant operands, and calls to the built-in functions listed here: pure
/// functions of their focus taking no arguments, and the type functions taking only a type name. Any other argument
/// could read the enclosing iteration's focus, <c>$this</c> or a variable; any other function could have an effect
/// (<c>trace()</c>), read the clock (<c>now()</c>), call host code (<c>resolve()</c>) or be a host's own function.
/// </para>
/// </remarks>
internal static class LoopInvariance
{
    private const byte Unknown = 0;
    private const byte Variant = 1;
    private const byte Invariant = 2;

    private static readonly FrozenSet<string> PureFunctions = new[]
    {
        "children", "descendants",
        "count", "exists", "empty", "first", "last", "tail", "single", "distinct", "isDistinct",
        "not", "allTrue", "anyTrue", "allFalse", "anyFalse", "hasValue",
        "toString", "toInteger", "toDecimal", "toBoolean", "toQuantity", "toDate", "toDateTime", "toTime",
        "length", "upper", "lower", "trim",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TypeFunctions =
        new[] { "as", "ofType", "is" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="expression"/> is invariant and does enough work to be worth remembering: a bare
    /// variable reference is invariant but costs nothing to re-read. The verdict is cached on the node.
    /// </summary>
    public static bool IsWorthMemoising(Expression expression)
    {
        var verdict = expression._loopInvarianceVerdict;
        if (verdict == Unknown)
        {
            verdict = expression is not VariableRefExpression && IsInvariant(expression) ? Invariant : Variant;
            expression._loopInvarianceVerdict = verdict;
        }

        return verdict == Invariant;
    }

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
            FunctionCallExpression call => call.Focus is { } focus && IsPureCall(call) && IsInvariant(focus),
            _ => false,
        };
    }

    private static bool IsPureCall(FunctionCallExpression call) => call.Arguments.Count switch
    {
        0 => PureFunctions.Contains(call.FunctionName),
        1 => call.Arguments[0] is IdentifierExpression && TypeFunctions.Contains(call.FunctionName),
        _ => false,
    };
}
