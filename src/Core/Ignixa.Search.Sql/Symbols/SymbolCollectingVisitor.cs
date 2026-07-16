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
/// Scoped to <see cref="SearchParameterPredicateExpression"/> and
/// <see cref="CompositeComponentExpression"/> -- the two node kinds Phase 2 added to carry typed
/// <see cref="SearchParameterInfo"/> identity. Resource-type identity (<c>ResourceTypeId</c>) is
/// otherwise deliberately not collected here: the design doc's <c>ResourceSource</c>/<c>ParamSource</c>
/// nodes that need it are synthesized by Lower (Phase 5) from context this visitor does not have --
/// notably the query's own target resource type, which lives on the surrounding SemanticQuery, not
/// anywhere in this <see cref="Expression"/> tree. The one narrow exception is
/// <see cref="ReferenceSearchValue.ResourceType"/>, which -- when present -- lives directly on a leaf
/// this visitor already walks, so it is collected here too (task 8); chain/compartment target-type
/// resolution remains Phase 6/8's job. See Resolve's remarks for the full argument.
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

        return expression;
    }

    public override Expression VisitCompositeComponent(CompositeComponentExpression expression, object? context)
    {
        Parameters.Add(expression.ComponentSearchParameter);
        return base.VisitCompositeComponent(expression, context);
    }
}
