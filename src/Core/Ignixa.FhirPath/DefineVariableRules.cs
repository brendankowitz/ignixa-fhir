/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The rules defineVariable() must obey, shared by the evaluator and the static analyzer.
 */

using System.Collections.Frozen;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath;

/// <summary>
/// The two things <c>defineVariable</c> may not do, expressed once so evaluation and analysis cannot
/// drift apart.
/// </summary>
/// <remarks>
/// Static analysis being more permissive than evaluation is the failure mode this guards against: an
/// expression that passes the analyzer and then throws at runtime teaches callers that the analyzer's
/// silence means nothing. Both rules are decidable from the AST alone, so the analyzer can apply exactly
/// the check the evaluator applies rather than an approximation of it.
/// </remarks>
internal static class DefineVariableRules
{
    /// <summary>
    /// Names <c>defineVariable</c> may not claim because the engine already resolves them: the FHIRPath
    /// external constants and the implicit iteration variables (official test
    /// <c>dvCantOverwriteSystemVar</c>: <c>defineVariable('context', 'oops')</c>).
    /// </summary>
    public static readonly FrozenSet<string> ReservedVariableNames = new[]
    {
        "context", "resource", "rootResource", "this", "index", "total", "ucum", "sct", "loinc"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reports whether an earlier link of this invocation chain already defines <paramref name="variableName"/>
    /// (official test <c>dvRedefiningVariableThrowsError</c>:
    /// <c>defineVariable('v1').defineVariable('v1').select(%v1)</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test to make here is "already defined <i>in this scope</i>", and the obvious implementation -
    /// asking the runtime variable store whether it holds the name - cannot express that. That dictionary is
    /// one mutable store shared by every context derived from the root: <c>PushThis</c> and friends copy the
    /// reference, not the contents, so a name survives both loop iterations and sibling argument evaluations.
    /// Asking it produces false errors on three valid official cases - <c>dvConceptMapExample</c>
    /// (<c>defineVariable('grp')</c> re-executed once per <c>group</c>), <c>defineVariable19</c> and
    /// <c>dvParametersDontColide</c> (the same name defined in two sibling arguments of one call, which the
    /// second test is named for).
    /// </para>
    /// <para>
    /// Walking the focus chain instead asks a question the AST can actually answer, and answers it without
    /// depending on the variable store's scoping - which stays unfixed, and is why the scope-leak cases
    /// (<c>defineVariable9</c>/<c>10</c>/<c>12</c>/<c>16</c>, <c>dvUsageOutsideScopeThrows</c>) remain
    /// deferred. The rule is deliberately narrow: it never reports a redefinition that is really a sibling
    /// scope or a re-execution, and it says nothing about a name defined dynamically or in an enclosing
    /// expression, which stay permissive as before.
    /// </para>
    /// </remarks>
    public static bool IsAlreadyDefinedEarlierInSameChain(FunctionCallExpression expression, string variableName)
    {
        for (var link = FocusOf(expression); link is not null; link = FocusOf(link))
        {
            if (link is FunctionCallExpression call && DefinesVariable(call, variableName))
            {
                return true;
            }
        }

        return false;
    }

    private static Expression? FocusOf(Expression expression) => expression switch
    {
        FunctionCallExpression call => call.Focus,
        PropertyAccessExpression property => property.Focus,
        _ => null
    };

    private static bool DefinesVariable(FunctionCallExpression call, string variableName)
        => call.FunctionName.Equals("defineVariable", StringComparison.OrdinalIgnoreCase)
           && call.Arguments.Count > 0
           && call.Arguments[0] is ConstantExpression { Value: string definedName }
           && definedName.Equals(variableName, StringComparison.OrdinalIgnoreCase);
}
