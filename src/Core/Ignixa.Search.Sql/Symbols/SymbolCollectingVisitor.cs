using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// Walks a typed predicate tree collecting every search parameter it references, without doing
/// any I/O -- <see cref="Resolve"/> batches these into <see cref="ISymbolResolver"/> calls
/// afterward. Un-braids tree traversal from symbol lookup, per
/// docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md.
/// </summary>
/// <remarks>
/// Collects <see cref="SearchParameterPredicateExpression"/>, <see cref="CompositeComponentExpression"/>,
/// and <see cref="SearchParameterExpression"/> parameters. The <c>VisitSearchParameter</c> override
/// specifically collects a composite parameter's own identity (its <c>SearchParamId</c> is otherwise
/// unreachable, since it lives only on the <see cref="SearchParameterExpression"/> wrapper, never on any
/// leaf beneath it). <c>base.VisitSearchParameter</c> is called to preserve recursion into <c>.Expression</c>,
/// which reaches every <see cref="SearchParameterPredicateExpression"/> and <see cref="CompositeComponentExpression"/>
/// beneath via the other two overrides. Resource-type identity (<c>ResourceTypeId</c>) is otherwise
/// deliberately not collected here: the design doc's <c>ResourceSource</c>/<c>ParamSource</c> nodes that
/// need it are synthesized by Lower (Phase 5) from context this visitor does not have -- notably the
/// query's own target resource type, which lives on the surrounding SemanticQuery, not anywhere in this
/// <see cref="Expression"/> tree. Two narrow exceptions collect resource-type identity directly from a
/// leaf this visitor already walks: <see cref="ReferenceSearchValue.ResourceType"/>, when present (task
/// 8); and a <c>_type</c> predicate's own <see cref="TokenSearchValue.Code"/> -- unlike <c>_id</c>'s
/// value (an opaque string, never a resource-type identity) or <c>_lastUpdated</c>'s (a timestamp),
/// <c>_type</c>'s value names the very resource type <c>ResourceColumnLoweringRule.TryLower</c>'s
/// <c>TypeEquals</c> arm needs to resolve, so without collecting it here every <c>_type</c> value other
/// than the query's own <c>targetResourceType</c> would throw. Chain/compartment target-type resolution
/// remains Phase 6/8's job. See Resolve's remarks for the full argument.
/// </remarks>
internal sealed class SymbolCollectingVisitor : ExpressionRewriter<object?>
{
    public HashSet<SearchParameterInfo> Parameters { get; } = [];

    public HashSet<string> ResourceTypes { get; } = [];

    public override Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
    {
        Parameters.Add(expression.Parameter);
        if (expression.Value is ReferenceSearchValue { ResourceType: { Length: > 0 } resourceType })
        {
            ResourceTypes.Add(resourceType);
        }

        if (expression.Parameter.Code == "_type" && expression.Value is TokenSearchValue { Code: { Length: > 0 } typeCode })
        {
            ResourceTypes.Add(typeCode);
        }

        return expression;
    }

    public override Expression VisitCompositeComponent(CompositeComponentExpression expression, object? context)
    {
        Parameters.Add(expression.ComponentSearchParameter);
        return base.VisitCompositeComponent(expression, context);
    }

    public override Expression VisitSearchParameter(SearchParameterExpression expression, object? context)
    {
        Parameters.Add(expression.Parameter);
        return base.VisitSearchParameter(expression, context);
    }
}
