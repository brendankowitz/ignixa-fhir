using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The compiler's Lower stage: turns a bound Expression tree of ANDed/ORed
/// SearchParameterPredicateExpression leaves and SearchParameterExpression-wrapped composites into a
/// QueryPlan. Chain, include, and sort are not handled -- see this plan's global constraints
/// for the full list and why.
/// </summary>
public static class Lower
{
    public static QueryPlan Run(Expression expression, SymbolTable symbols, string targetResourceType, int? top = null)
    {
        var leafContext = new LeafContext(symbols);
        var (remaining, outerPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
        var context = new StructuralContext(symbols, targetResourceType);
        var match = remaining is null
            ? context.LowerResourceSource()
            : LowerNode(remaining, context);
        return new QueryPlan(context.Ctes, match, top, outerPredicate);
    }

    private static CteRef LowerNode(Expression expression, StructuralContext context) => expression switch
    {
        SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } => throw new NotSupportedException(
            "A :not-modified predicate reached leaf dispatch directly, outside a SearchParameterExpression wrapper -- " +
            "the real binder never produces this shape (LowerSearchParameter handles :not for both the single-value " +
            "and comma-separated cases), so this is unexpected input. Throwing rather than silently lowering it as a " +
            "positive match, which is exactly the bug this guard exists to prevent."),
        SearchParameterPredicateExpression leaf => context.Lower(leaf),
        SearchParameterExpression sp => LowerSearchParameter(sp, context),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context)).ToList()),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

    private static CteRef LowerSearchParameter(SearchParameterExpression sp, StructuralContext context)
    {
        if (sp.Expression is NotExpression not)
        {
            return context.LowerNot(LowerNode(not.Expression, context));
        }

        if (sp.Expression is SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate)
        {
            var positiveMatch = new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value);
            return context.LowerNot(context.Lower(positiveMatch));
        }

        if (TryGetCompositeComponents(sp.Expression, out var components))
        {
            return context.LowerComposite(sp.Parameter, components!);
        }

        if (sp.Expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or
            && or.Expressions.Count > 0
            && or.Expressions.All(e => TryGetCompositeComponents(e, out _)))
        {
            var refs = or.Expressions
                .Select(e =>
                {
                    TryGetCompositeComponents(e, out var alt);
                    return context.LowerComposite(sp.Parameter, alt!);
                })
                .ToList();
            return context.Union(refs);
        }

        return LowerNode(sp.Expression, context);
    }

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

    private static CteRef LowerAnd(MultiaryExpression and, StructuralContext context)
    {
        var refs = and.Expressions.Select(e => LowerNode(e, context)).ToList();
        var result = refs[0];
        for (var i = 1; i < refs.Count; i++)
        {
            result = context.Intersect(result, refs[i]);
        }
        return result;
    }

    private static (Expression? Remaining, Predicate? OuterPredicate) ExtractResourceColumnPredicates(Expression expression, LeafContext leafContext)
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

    private static Predicate? TryExtractResourceColumnPredicate(Expression expression, LeafContext leafContext)
        => expression is SearchParameterExpression { Expression: SearchParameterPredicateExpression predicate }
            ? ResourceColumnLoweringRule.TryLower(predicate, leafContext)
            : null;
}
