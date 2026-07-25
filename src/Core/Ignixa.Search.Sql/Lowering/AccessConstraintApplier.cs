// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Binds <see cref="AccessConstraint"/>s to the CTE graph. A constrained stage becomes the intersection
/// of what it produced and what the constraint admits, so the restriction survives every later set
/// operation rather than being a filter a subsequent union could widen back out.
/// </summary>
/// <remarks>
/// Enforcement runs on every stage that produces rows — the match set (single- or multi-type), each
/// include/:iterate stage, and each chain target — not only the top-level match. Applying a constraint
/// at the match set alone would let an _include or a chain reach a resource the caller may not see, which
/// is the failure mode an expression-rewriting approach is prone to and the reason this type exists.
/// <para>
/// Adding a new row-producing stage? Pick the enforcement method by what the stage produces — choosing
/// wrong is an authorization bypass, so this contract is explicit:
/// <list type="bullet">
/// <item><description>Rows of one statically known type (a single-type match, a chain target) — call
/// <see cref="Apply"/>: it intersects directly with that type's constraint.</description></item>
/// <item><description>A set spanning several types, or an unknown mix (a multi-<c>_type</c> or wildcard
/// compartment match) — call <see cref="ApplyToTypes"/>: it narrows each constrained type without
/// dropping the others.</description></item>
/// <item><description>A stage filtered post-hoc rather than intersected in place (an
/// include/:iterate stage the emitter guards with an EXISTS) — call <see cref="BindIncludeStage"/>: it
/// records per-stage bindings and fails closed on a wildcard whose output types are unknown.</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class AccessConstraintApplier
{
    private readonly IReadOnlyDictionary<string, AccessConstraint> _byType;

    /// <summary>
    /// Indexes the constraints by resource type. A duplicate type is a caller error, not something to
    /// silently collapse: two constraints for one type means the claim-translation layer failed to combine
    /// them, and keeping only the first (what a plain last-wins dictionary build would do) would silently
    /// drop half of a security rule. We throw instead. Combining them with AND would also be defensible,
    /// but throwing keeps the one-constraint-per-type contract the compiler relies on visible at the seam
    /// where the mistake was made.
    /// </summary>
    public AccessConstraintApplier(IReadOnlyList<AccessConstraint>? constraints)
    {
        if (constraints is not { Count: > 0 })
        {
            _byType = new Dictionary<string, AccessConstraint>(StringComparer.Ordinal);
            return;
        }

        var byType = new Dictionary<string, AccessConstraint>(StringComparer.Ordinal);
        foreach (var constraint in constraints)
        {
            if (!byType.TryAdd(constraint.ResourceType, constraint))
            {
                throw new ArgumentException(
                    $"Duplicate access constraint for resource type '{constraint.ResourceType}'. At most one " +
                    "constraint per type is allowed -- combine the predicates into a single constraint before " +
                    "compiling. Silently keeping one would drop the other half of an authorization rule.",
                    nameof(constraints));
            }
        }

        _byType = byType;
    }

    /// <summary>Whether there are no constraints. Every enforcement site short-circuits on this so an
    /// unconstrained plan is byte-identical to one compiled before access constraints existed.</summary>
    public bool IsEmpty => _byType.Count == 0;

    /// <summary>
    /// Intersects <paramref name="stage"/> with the constraint for <paramref name="resourceType"/>, or
    /// returns it unchanged when that type is unconstrained. Used for a single-type match set and for each
    /// chain target, where every row the stage produces is known to be of one type.
    /// </summary>
    public CteRef Apply(CteRef stage, string resourceType, StructuralContext context, Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lowerNode);

        if (!_byType.TryGetValue(resourceType, out var constraint))
        {
            return stage;
        }

        return context.Intersect(stage, lowerNode(constraint.Predicate, context, resourceType));
    }

    /// <summary>
    /// Applies constraints to a match set that spans several types (a system-wide, multi-<c>_type</c>, or
    /// compartment search where no single target type scopes the rows). A plain intersect would be wrong
    /// here: the constraint CTE for one type holds only that type's rows, so intersecting would drop every
    /// other type. Instead, for each constrained type the result keeps all rows that are not of that type,
    /// unioned with the rows of that type the constraint admits — narrowing the constrained type without
    /// touching the others.
    /// <para>
    /// It iterates the applier's own constrained types rather than a caller-supplied type list, so a match
    /// whose produced types are not enumerable up front (a compartment search across every type) is still
    /// guarded: a constraint whose type the match never produces is a harmless no-op (the Except removes
    /// nothing and the Intersect admits nothing), while a constraint whose type the match does produce is
    /// enforced. Enumerating the caller's list instead would silently skip a constraint on a type the list
    /// omitted — a fail-open this avoids.
    /// </para>
    /// </summary>
    public CteRef ApplyToTypes(CteRef stage, StructuralContext context, Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lowerNode);

        var result = stage;
        foreach (var constraint in _byType.Values)
        {
            var admitted = context.Intersect(result, lowerNode(constraint.Predicate, context, constraint.ResourceType));
            var otherTypes = context.Except(result, context.LowerResourceSource(constraint.ResourceType));
            result = context.Union([otherTypes, admitted]);
        }

        return result;
    }

    /// <summary>
    /// Lowers the constraints that could bind to an include/:iterate stage into CTEs and returns the
    /// bindings the emitter turns into type-guarded EXISTS filters, or <see langword="null"/> when none
    /// apply. For a stage with known output types, only constraints on those types can bind. For a wildcard
    /// stage (<paramref name="outputTypeIds"/> is <see langword="null"/>) the produced types are unknown at
    /// compile time, so every constraint is bound conservatively — failing closed, because letting a
    /// wildcard include skip a constraint would be a way to read a resource the caller may not see.
    /// </summary>
    public IReadOnlyList<IncludeConstraint>? BindIncludeStage(
        IReadOnlyList<short>? outputTypeIds,
        SymbolTable symbols,
        StructuralContext context,
        Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lowerNode);

        if (IsEmpty)
        {
            return null;
        }

        List<IncludeConstraint>? bindings = null;
        foreach (var constraint in _byType.Values)
        {
            if (!symbols.TryGetResourceTypeId(constraint.ResourceType, out var typeId))
            {
                if (outputTypeIds is null)
                {
                    // Wildcard stage whose produced types are unknown, and a constraint whose type was never
                    // resolved: we cannot emit a guard for a type id we do not have, and we cannot prove the
                    // wildcard will not produce that type. Refuse to compile rather than fail open. In the
                    // real pipeline constraint symbols are resolved alongside the search, so this never fires.
                    throw new InvalidOperationException(
                        $"Cannot enforce the access constraint for resource type '{constraint.ResourceType}' on a " +
                        "wildcard include: the type was never resolved, so no guard can be emitted and closure " +
                        "cannot be guaranteed. Resolve the constraint's symbols with the search, or narrow the " +
                        "include away from a wildcard.");
                }

                // A typed stage produces only its declared output types; an unresolved constraint type can
                // never be among them, so it cannot be produced here and there is nothing to guard.
                continue;
            }

            if (outputTypeIds is { Count: > 0 } && !outputTypeIds.Contains(typeId))
            {
                continue;
            }

            var constraintCte = lowerNode(constraint.Predicate, context, constraint.ResourceType);
            (bindings ??= []).Add(new IncludeConstraint(typeId, constraintCte.Index));
        }

        return bindings;
    }
}
