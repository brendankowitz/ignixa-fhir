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
/// This reproduces the <em>structural compartment traversal</em> of the shipping engine -- the patient
/// plus every resource in the patient compartment, with optional <c>_since</c> and <c>_type</c> narrowing.
/// Several behaviours of the shipping <c>PatientEverythingQueryGenerator</c> are deliberately <em>not</em>
/// reproduced here, and each is a disclosed gap rather than a hidden one:
/// <list type="bullet">
/// <item><description>
/// <b>Paging.</b> The <c>row-number</c> window, the <c>Row &lt; @p</c> correlation, and the
/// <c>_count</c>/IsPartial windowing are a presentation concern layered over the same row set and are left
/// to the plan's own paging (<see cref="Ast.PageSpec"/>).
/// </description></item>
/// <item><description>
/// <b>Referenced resources.</b> <see cref="PatientEverythingExpression.IncludeReferencedResources"/>
/// defaults to <c>true</c>, and the shipping engine unions in resources referenced from the compartment
/// that live <em>outside</em> it -- Practitioner, Organization, Location, Medication (per the FHIR spec,
/// servers SHOULD include these). This lowering unions only the patient row(s) and their compartment
/// members; it emits no referenced-resource union, so those resources are dropped.
/// </description></item>
/// <item><description>
/// <b>Clinical-date filtering.</b> <see cref="PatientEverythingExpression.StartDate"/> and
/// <see cref="PatientEverythingExpression.EndDate"/> bound compartment members by their clinical date in the
/// shipping engine. This lowering ignores both -- the expression carries them, but no date predicate reaches
/// the plan.
/// </description></item>
/// </list>
/// One asymmetry is intentional, not a gap: <c>_since</c> bounds the compartment members (via
/// <see cref="StructuralContext.LowerPatientCompartment"/>'s surrogate-id bound) but <em>not</em> the seed
/// patient row, so the Patient is returned regardless of <c>_since</c>. That matches the shipping engine's
/// own captured SQL, whose seed row carries no <c>_since</c> bound, and the convention that the operation's
/// defining resource -- the compartment root -- is always included. The same reason keeps the patient row
/// even when a <c>_type</c> filter omits Patient: narrowing it away would drop the resource the operation is
/// named for.
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
