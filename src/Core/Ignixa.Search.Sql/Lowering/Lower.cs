using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The compiler's Lower stage: turns a bound Expression tree of ANDed/ORed
/// SearchParameterPredicateExpression leaves and SearchParameterExpression-wrapped composites into a
/// QueryPlan. Chain, include, sort, and :not are not handled -- see this plan's global constraints
/// for the full list and why.
/// </summary>
public static class Lower
{
    public static QueryPlan Run(Expression expression, SymbolTable symbols, int? top = null)
    {
        var context = new StructuralContext(symbols);
        var match = LowerNode(expression, context);
        return new QueryPlan(context.Ctes, match, top);
    }

    private static CteRef LowerNode(Expression expression, StructuralContext context) => expression switch
    {
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
}
