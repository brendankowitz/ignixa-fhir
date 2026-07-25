using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers <see cref="PatientEverythingExpression"/> by expanding it into the plan primitives that already
/// exist -- the patient's own row(s) unioned with their compartment members -- rather than introducing a
/// new CteDefinition. Every emitter this reaches (ResourceSource, CompartmentSource, Union) is already
/// covered by the per-rule suites, so the operation inherits their coverage instead of needing its own
/// emitter tests.
/// </summary>
/// <remarks>
/// This reproduces the <em>structural traversal</em> of the shipping engine -- the patient plus every
/// resource in the patient compartment, with optional <c>_since</c> and <c>_type</c> narrowing -- but not
/// its paging machinery (the <c>row-number</c> window, the <c>Row &lt; @p</c> correlation, and the
/// <c>_count</c>/IsPartial windowing). Those are a presentation concern layered over the same row set and
/// are deliberately left to the plan's own paging (<see cref="Ast.PageSpec"/>). The patient row is always
/// included even when a <c>_type</c> filter omits Patient; narrowing it away would drop the operation's
/// defining resource.
/// </remarks>
internal static class EverythingLoweringRule
{
    public static CteRef Lower(PatientEverythingExpression expression, StructuralContext context, string resourceType)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(context);

        var patientRows = expression.PatientIds
            .Select(id => context.LowerResourceSourceForId(resourceType, id))
            .ToList();

        var compartment = context.LowerPatientCompartment(expression);

        return context.Union([.. patientRows, compartment]);
    }
}
