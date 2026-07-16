using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The compiler's Lower stage, narrowed to this plan's scope: turns a bound Expression tree of
/// ANDed/ORed SearchParameterPredicateExpression leaves (String/Token/Reference only) into a
/// QueryPlan. Composites, chain, include, sort, and :not are not handled -- see this plan's global
/// constraints for the full list and why.
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
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context)).ToList()),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

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
