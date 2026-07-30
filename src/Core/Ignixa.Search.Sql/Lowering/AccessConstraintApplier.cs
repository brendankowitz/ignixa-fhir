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
/// of what it produced and what the constraint admits, so the restriction survives later set operations.
/// Enforced on every row-producing stage (match set, include/:iterate, chain target), not just the match.
/// </summary>
internal sealed class AccessConstraintApplier
{
    private readonly IReadOnlyDictionary<string, AccessConstraint> _byType;

    /// <summary>
    /// Indexes the constraints by resource type. A duplicate type throws rather than last-wins, which would
    /// silently drop half of an authorization rule. Uses <see cref="NotSupportedException"/> (not
    /// <see cref="ArgumentException"/>, which <c>SearchSqlCompiler</c>'s catch filter excludes) so a duplicate
    /// is recorded as a SearchCompilationFailure instead of escaping as a 500.
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
                throw new NotSupportedException(
                    $"Duplicate access constraint for resource type '{constraint.ResourceType}'. At most one " +
                    "constraint per type is allowed -- combine the predicates into a single constraint before " +
                    "compiling. Silently keeping one would drop the other half of an authorization rule.");
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
    /// Applies constraints to a match set spanning several types. A plain intersect would drop every other
    /// type, so for each constrained type this keeps the non-matching rows unioned with the admitted ones.
    /// Iterates the applier's own constrained types so a wildcard/compartment match cannot skip a constraint.
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
    /// apply. A wildcard stage (<paramref name="outputTypeIds"/> is <see langword="null"/>) binds every
    /// constraint conservatively, failing closed since its produced types are unknown at compile time.
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
                    // Unreachable through SearchSqlCompiler (Resolve records a resolver miss as the
                    // unmatchable sentinel, so TryGetResourceTypeId always succeeds); this guard fails closed
                    // for direct callers of Lower.Run that bypass Resolve.
                    throw new NotSupportedException(
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
