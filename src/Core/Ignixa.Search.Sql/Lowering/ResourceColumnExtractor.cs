using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Peels the resource-column predicates (<c>_id</c>, <c>_type</c>, <c>_lastUpdated</c>) off an expression so
/// they can be emitted as an outer WHERE against dbo.Resource rather than lowered into their own CTEs.
/// </summary>
internal static class ResourceColumnExtractor
{
    /// <summary>Splits an expression into the resource-column predicates (_id/_type/_lastUpdated, ANDed together
    /// into an outer WHERE) and the remaining expression that still needs CTE lowering. Either half may be null.</summary>
    internal static (Expression? Remaining, Predicate? OuterPredicate) ExtractResourceColumnPredicates(Expression expression, LeafContext leafContext)
    {
        if (expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and)
        {
            var kept = new List<Expression>();
            Predicate? outer = null;
            foreach (var child in and.Expressions)
            {
                var resourcePredicate = TryExtractResourceColumnPredicate(child, leafContext);
                outer = resourcePredicate is null
                    ? outer
                    : outer is null ? resourcePredicate : new Predicate.And(outer, resourcePredicate);
                if (resourcePredicate is null)
                {
                    kept.Add(child);
                }
            }

            Expression? remaining = kept.Count switch
            {
                0 => null,
                1 => kept[0],
                _ => new MultiaryExpression(MultiaryOperator.And, kept),
            };
            return (remaining, outer);
        }

        var single = TryExtractResourceColumnPredicate(expression, leafContext);
        return single is null ? (expression, null) : (null, single);
    }

    /// <summary>Returns the resource-column predicate for a single wrapped leaf, or null if it is not one.</summary>
    private static Predicate? TryExtractResourceColumnPredicate(Expression expression, LeafContext leafContext)
        => expression is SearchParameterExpression wrapped
            ? TryLowerResourceColumn(wrapped.Expression, leafContext)
            : null;

    /// <summary>Lowers a resource-column leaf, or a comma list of them (<c>_id=a,b,c</c> binds to an Or). The Or is
    /// all-or-nothing: a non-resource-column branch leaves the whole expression to CTE lowering, because half
    /// an Or in the outer WHERE would widen the match rather than narrow it.</summary>
    private static Predicate? TryLowerResourceColumn(Expression expression, LeafContext leafContext)
    {
        // A negated resource column (_id:not, _type:not) arrives as a NotExpression wrapping the positive
        // alternatives. Lower the positive form, then wrap it in Predicate.Not so the negation reaches the
        // outer WHERE as NOT (...) rather than being silently dropped.
        if (expression is NotExpression not)
        {
            var inner = TryLowerResourceColumn(not.Expression, leafContext);
            return inner is null ? null : new Predicate.Not(inner);
        }

        if (expression is SearchParameterPredicateExpression predicate)
        {
            return TryLowerResourceColumnPredicate(predicate, leafContext);
        }

        if (expression is not MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or)
        {
            return null;
        }

        Predicate? combined = null;
        foreach (var branch in or.Expressions)
        {
            var lowered = TryLowerResourceColumn(branch, leafContext);
            if (lowered is null)
            {
                return null;
            }

            combined = combined is null ? lowered : new Predicate.Or(combined, lowered);
        }

        return combined;
    }

    /// <summary>Lowers a single resource-column predicate, rewriting a <c>:not</c>-modified one the way
    /// <see cref="StructuralLoweringDispatcher.TryGetNegatedInner"/> rewrites its CTE-path equivalent.</summary>
    /// <remarks>
    /// <c>_id:not=a,b</c> reaches <see cref="TryLowerResourceColumn"/> as a NotExpression because
    /// SearchExpressionBinder.BindAlternatives lifts the modifier off the items it wraps in an Or; a
    /// single-valued <c>_id:not=a</c> binds through BindAtomic instead, which has no Or to lift onto and so
    /// leaves the modifier on the predicate. Both spellings mean the same thing, so both must lower to the
    /// same Predicate.Not. ResourceColumnLoweringRule still rejects every other modifier, since dropping one
    /// of those would silently widen the match rather than negate it.
    /// </remarks>
    private static Predicate? TryLowerResourceColumnPredicate(SearchParameterPredicateExpression predicate, LeafContext leafContext)
    {
        if (predicate.Modifier?.SearchModifierCode != SearchModifierCode.Not)
        {
            return ResourceColumnLoweringRule.TryLower(predicate, leafContext);
        }

        var positive = new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value) { Span = predicate.Span };
        var inner = ResourceColumnLoweringRule.TryLower(positive, leafContext);
        return inner is null ? null : new Predicate.Not(inner);
    }
}
