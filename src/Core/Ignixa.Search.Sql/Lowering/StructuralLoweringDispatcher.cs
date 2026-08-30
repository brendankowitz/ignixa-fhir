using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Dispatches one structural (tier-2) expression node to its lowering path, the third dispatcher alongside
/// <see cref="Leaf.LeafLoweringDispatcher"/> and <see cref="Composite.CompositeLoweringDispatcher"/>. Every
/// node kind either lowers here or routes into <see cref="StructuralContext"/>, which owns the CTE graph.
/// </summary>
internal static class StructuralLoweringDispatcher
{
    /// <summary>Dispatches one expression node to the lowering path for its kind. A null
    /// <paramref name="resourceType"/> reaches here only under system-level search, and a chain tolerates it: a
    /// chain names its own types (a reverse chain scopes its inner expression against its referencing type and
    /// emits its target types, a forward chain the mirror image), so the ambient scope is unused and none is
    /// passed on. Every guard on those types therefore lives in <see cref="StructuralContext.LowerChain"/>,
    /// where both directions reach it. The OR and union arms below look mergeable and are not: a union leg goes
    /// through <see cref="LowerScopedExpression"/>, which can recover a per-leg type under a null scope, while
    /// an OR's operands are alternative values of one parameter and lower with the ambient scope as-is.</summary>
    internal static CteRef LowerNode(Expression expression, StructuralContext context, string? resourceType) => expression switch
    {
        SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } => throw new NotSupportedException(
            "A :not-modified predicate reached leaf dispatch directly, outside a SearchParameterExpression wrapper -- " +
            "the real binder never produces this shape (LowerSearchParameter handles :not for both the single-value " +
            "and comma-separated cases), so this is unexpected input. Throwing rather than silently lowering it as a " +
            "positive match, which is exactly the bug this guard exists to prevent."),
        SearchParameterPredicateExpression leaf => context.Lower(leaf, resourceType),
        MissingSearchParameterExpression missing => LowerMissing(missing, context, resourceType),
        SearchParameterExpression sp => LowerSearchParameter(sp, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context, resourceType)).ToList()),
        UnionExpression union => context.Union(
            union.Expressions.Select(leg => LowerScopedExpression(leg, context, resourceType)).ToList()),
        ChainedExpression chain => context.LowerChain(chain, LowerScopedExpression),
        CompartmentSearchExpression compartment => context.LowerCompartment(compartment),
        NotReferencedExpression notReferenced => context.LowerNotReferenced(notReferenced, resourceType),
        PatientEverythingExpression when resourceType is null => throw new NotSupportedException(
            "$everything is not supported in system-level search -- it is anchored on the Patient/Group type " +
            "whose compartment it expands, so it has no meaning without one. Guarding at the dispatch choke " +
            "point rather than letting the traversal run under a scope it cannot use."),
        PatientEverythingExpression everything => context.LowerPatientEverything(everything),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

    /// <summary>Lowers a wrapped search parameter, unwrapping the wrapper's own semantics first: a NotExpression or
    /// a :not-modified predicate becomes a negation, a single composite or an OR of composite alternatives
    /// becomes composite lowering, and anything else falls through to <see cref="LowerNode"/>.</summary>
    private static CteRef LowerSearchParameter(SearchParameterExpression sp, StructuralContext context, string? resourceType)
    {
        if (sp.Expression is NotExpression not)
        {
            return context.LowerNot(LowerNode(not.Expression, context, resourceType), resourceType);
        }

        if (sp.Expression is SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate)
        {
            var positiveMatch = new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value)
            {
                Span = predicate.Span,
            };
            return context.LowerNot(context.Lower(positiveMatch, resourceType, provenanceNode: predicate), resourceType);
        }

        if (sp.Expression is StringExpression { FieldName: FieldName.TokenText } text)
        {
            return context.LowerTokenText(sp.Parameter, text, resourceType, provenanceNode: sp);
        }

        if (TryGetCompositeComponents(sp.Expression, out var components))
        {
            return context.LowerComposite(sp.Parameter, components!, resourceType, provenanceNode: sp);
        }

        if (sp.Expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or
            && or.Expressions.Count > 0
            && or.Expressions.All(e => TryGetCompositeComponents(e, out _)))
        {
            var refs = or.Expressions
                .Select(e =>
                {
                    TryGetCompositeComponents(e, out var alt);
                    return context.LowerComposite(sp.Parameter, alt!, resourceType, provenanceNode: e);
                })
                .ToList();
            return context.Union(refs);
        }

        return LowerNode(sp.Expression, context, resourceType);
    }

    /// <summary>Lowers a :missing search to the parameter's presence set, negated when :missing=true.</summary>
    private static CteRef LowerMissing(MissingSearchParameterExpression missing, StructuralContext context, string? resourceType)
    {
        var presence = context.LowerParameterPresence(missing.Parameter, resourceType, provenanceNode: missing);
        return missing.IsMissing ? context.LowerNot(presence, resourceType) : presence;
    }

    /// <summary>Returns true and the components when the expression is an AND of composite components; false otherwise.</summary>
    private static bool TryGetCompositeComponents(Expression expression, out IReadOnlyList<CompositeComponentExpression>? components)
    {
        if (expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and
            && and.Expressions.Count > 0
            && and.Expressions.All(e => e is CompositeComponentExpression))
        {
            components = and.Expressions.Cast<CompositeComponentExpression>().ToList();
            return true;
        }

        components = null;
        return false;
    }

    /// <summary>Lowers an AND by intersecting its positive children, then subtracting each negated child (<c>A AND NOT B</c>
    /// is <c>A EXCEPT B</c>). Positive siblings form a smaller anchor than a bare negation would need; with no
    /// positive sibling the ResourceSource anchor is the only option (see <see cref="StructuralContext.LowerNot"/>).</summary>
    private static CteRef LowerAnd(MultiaryExpression and, StructuralContext context, string? resourceType)
    {
        var positives = new List<Expression>();
        var negated = new List<Expression>();
        foreach (var child in and.Expressions)
        {
            var inner = TryGetNegatedInner(child);
            (inner is null ? positives : negated).Add(inner ?? child);
        }

        if (negated.Count == 0)
        {
            return Intersect(positives, context, resourceType);
        }

        // The positives must be lowered first: an Except may only reference CTEs already defined above it.
        var result = positives.Count > 0
            ? Intersect(positives, context, resourceType)
            : context.LowerNegationAnchor(resourceType);

        foreach (var inner in negated)
        {
            result = context.Except(result, LowerNode(inner, context, resourceType));
        }

        return result;
    }

    private static CteRef Intersect(IReadOnlyList<Expression> expressions, StructuralContext context, string? resourceType)
    {
        var refs = expressions.Select(e => LowerNode(e, context, resourceType)).ToList();
        var result = refs[0];
        for (var i = 1; i < refs.Count; i++)
        {
            result = context.Intersect(result, refs[i]);
        }

        return result;
    }

    /// <summary>Returns the positive inner match a negated child subtracts, or null when the child is not a negation.
    /// The three negation shapes (NotExpression, :not-modified predicate, :missing=true) all reduce to an
    /// expression <see cref="LowerNode"/> already lowers positively.</summary>
    internal static Expression? TryGetNegatedInner(Expression child) => child switch
    {
        SearchParameterExpression { Expression: NotExpression not } => not.Expression,
        SearchParameterExpression { Expression: SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate } =>
            new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value) { Span = predicate.Span },
        MissingSearchParameterExpression { IsMissing: true } missing =>
            new MissingSearchParameterExpression(missing.Parameter, isMissing: false),
        _ => null,
    };

    /// <summary>Lowers a chain's target expression or a union leg within its own scope, folding any resource-column
    /// predicates into the scope's ResourceSource and intersecting with the ordinary match. <paramref name="resourceType"/>
    /// is null only for a union leg under a system-level search; that case routes to
    /// <see cref="LowerSystemLevelUnionLeg"/> so the typed path every other caller uses stays unchanged.</summary>
    internal static CteRef LowerScopedExpression(Expression expression, StructuralContext context, string? resourceType)
    {
        var (remaining, nestedPredicate) = ResourceColumnExtractor.ExtractResourceColumnPredicates(expression, context.LeafContext);

        if (resourceType is null)
        {
            return LowerSystemLevelUnionLeg(expression, remaining, nestedPredicate, context);
        }

        if (remaining is null)
        {
            return context.LowerResourceSourceWithPredicate(resourceType, nestedPredicate);
        }

        var ordinaryMatch = LowerNode(remaining, context, resourceType);
        return nestedPredicate is null
            ? ordinaryMatch
            : context.Intersect(context.LowerResourceSourceWithPredicate(resourceType, nestedPredicate), ordinaryMatch);
    }

    /// <summary>Lowers one union leg under a system-level (null) scope -- the SMART compartment expansion. A pure
    /// resource-column leg folds into an AllTypes source; a leg with a residue derives its type from its own
    /// single <c>_type Eq X</c> (see <see cref="TryDeriveSingleTypeScope"/>) then lowers as a typed leg; a leg
    /// with no derivable type lowers under null scope and lets the per-node guards decide.</summary>
    private static CteRef LowerSystemLevelUnionLeg(
        Expression leg,
        Expression? remaining,
        Predicate? nestedPredicate,
        StructuralContext context)
    {
        if (remaining is null)
        {
            return context.LowerMultiTypeResourceSourceWithPredicate(nestedPredicate);
        }

        if (TryDeriveSingleTypeScope(leg) is { } derivedType)
        {
            // With a concrete type recovered, the leg is indistinguishable from a natively typed one: scope the
            // residue to that type and intersect with the resource-column predicate, emitting the same single-type
            // ResourceSource a typed leg would. The predicate's redundant ResourceTypeId equality is harmless.
            var scopedMatch = LowerNode(remaining, context, derivedType);
            return nestedPredicate is null
                ? scopedMatch
                : context.Intersect(context.LowerResourceSourceWithPredicate(derivedType, nestedPredicate), scopedMatch);
        }

        var match = LowerNode(remaining, context, resourceType: null);
        return nestedPredicate is null
            ? match
            : context.Intersect(context.LowerMultiTypeResourceSourceWithPredicate(nestedPredicate), match);
    }

    /// <summary>Returns the type name a union leg scopes itself to via a <em>single</em> plain <c>_type Eq X</c> among its
    /// ANDed children, or null otherwise. Confined to one equality: a <c>_type=A,B</c> Or, two distinct
    /// equalities, or a modified/system-qualified <c>_type</c> all yield null rather than a guess that could drop
    /// rows -- a null result lowers the residue under a null type, where its own per-node guard decides.</summary>
    private static string? TryDeriveSingleTypeScope(Expression leg)
    {
        var children = leg is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and
            ? and.Expressions
            : [leg];

        string? found = null;
        foreach (var child in children)
        {
            if (child is not SearchParameterExpression { Expression: SearchParameterPredicateExpression predicate }
                || predicate.Parameter.Code != "_type"
                || predicate.Modifier is not null
                || predicate.Comparator != SearchComparator.Eq
                || predicate.Value is not TokenSearchValue { System: null, Code: { Length: > 0 } code })
            {
                continue;
            }

            if (found is not null)
            {
                // A second single-valued _type equality makes the scope ambiguous. Refuse to guess: null lowers
                // the residue under a null type rather than scoping to whichever equality came first.
                return null;
            }

            found = code;
        }

        return found;
    }
}
