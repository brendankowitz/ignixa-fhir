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
    /// <remarks>
    /// <para>
    /// One of three overlapping reserved-name lists that answer different questions - do not merge them.
    /// This one is "<c>defineVariable()</c> may not claim this name".
    /// <c>Ignixa.SqlOnFhir.Evaluation.SqlOnFhirEvaluator.EngineManagedVariableNames</c> is "a caller may
    /// not supply this as a variable", and <c>ViewDefinitionExpressionParser</c>'s method-local
    /// <c>predefinedVariables</c> is "needs no constant declaration in a ViewDefinition". A name added to
    /// one usually belongs in none of the others; check all three anyway.
    /// </para>
    /// <para>
    /// Ordinal, so the guard is exactly as wide as the thing it guards. The engine resolves these names
    /// with a case-sensitive switch and <c>StartsWith(StringComparison.Ordinal)</c>
    /// (<see cref="Evaluation.EvaluationContext.TryGetEnvironmentVariable"/>), so <c>%Context</c> never
    /// reaches the system binding and <c>defineVariable('Context', …)</c> collides with nothing. Rejecting
    /// it anyway would refuse a legal name on the strength of a collision that cannot happen.
    /// </para>
    /// </remarks>
    public static readonly FrozenSet<string> ReservedVariableNames = new[]
    {
        "context", "resource", "rootResource", "this", "index", "total", "ucum", "sct", "loinc"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Reports whether an earlier link of this invocation chain already defines <paramref name="variableName"/>
    /// (official test <c>dvRedefiningVariableThrowsError</c>:
    /// <c>defineVariable('v1').defineVariable('v1').select(%v1)</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test to make here is "already defined <i>in this scope</i>", and asking the runtime variable store
    /// whether it holds the name still cannot express it, even now that <see cref="Evaluation.VariableScope"/>
    /// gives each per-item argument and each <c>|</c> operand a frame of its own. Scope frames make loop
    /// re-execution safe - <c>dvConceptMapExample</c> re-runs <c>defineVariable('grp')</c> once per
    /// <c>group</c>, each time in a fresh frame - but sibling arguments of one call deliberately share a
    /// frame, because a variable defined in one argument has to be readable in the next
    /// (<c>defineVariable19</c>). A presence check would therefore still fire on <c>dvParametersDontColide</c>,
    /// which defines the same name in two sibling arguments of one <c>replace()</c> and is named for it.
    /// </para>
    /// <para>
    /// Walking the focus chain instead asks a question the AST can answer outright, and is what
    /// <c>dvRedefiningVariableThrowsError</c> actually describes: the same invocation chain, not the same
    /// dictionary. The rule is deliberately narrow - it never reports a redefinition that is really a sibling
    /// argument or a re-execution, and it says nothing about a name defined dynamically or in an enclosing
    /// expression, which stay permissive.
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

    // The variable name is compared ordinally because %v and %V are distinct variables, so defining both
    // is not the redefinition dvRedefiningVariableThrowsError describes. The function name keeps the
    // lenient comparison it has always had, to stay consistent with how this engine dispatches every other
    // function name; tightening that is a separate change with a far wider blast radius.
    private static bool DefinesVariable(FunctionCallExpression call, string variableName)
        => call.FunctionName.Equals("defineVariable", StringComparison.OrdinalIgnoreCase)
           && call.Arguments.Count > 0
           && call.Arguments[0] is ConstantExpression { Value: string definedName }
           && definedName.Equals(variableName, StringComparison.Ordinal);
}
