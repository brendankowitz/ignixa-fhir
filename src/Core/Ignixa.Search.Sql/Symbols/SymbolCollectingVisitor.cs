using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Lowering;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// Walks a typed predicate tree collecting every search parameter it references, doing no I/O —
/// <see cref="Resolve"/> batches the results into <see cref="ISymbolResolver"/> calls afterward. This
/// keeps tree traversal separate from symbol lookup.
/// </summary>
/// <remarks>
/// Collects the parameter off every non-resource-column <see cref="SearchParameterPredicateExpression"/>,
/// <see cref="CompositeComponentExpression"/>, <see cref="SearchParameterExpression"/>, and
/// <see cref="MissingSearchParameterExpression"/>. The <c>VisitSearchParameter</c> override captures a
/// composite's own identity, which lives only on the wrapper, then recurses to reach the leaves beneath.
/// <para>
/// Resource-type identity is generally not collected here — Lower synthesizes the nodes that need it from
/// its own context (notably the query's target resource type). The exceptions collect a resource type
/// directly off a leaf this visitor already walks: a <see cref="ReferenceSearchValue"/>'s type, a
/// <c>_type</c> predicate's own value (the resource type it names), and a <see cref="ChainedExpression"/>'s
/// reference parameter plus both its type arrays. Includes, sort keys, and compartments never appear in
/// the Expression tree, so Resolve feeds them in through the direct <see cref="CollectInclude"/>,
/// <see cref="CollectSort"/>, and <see cref="VisitCompartment"/> entry points instead.
/// </para>
/// </remarks>
internal sealed class SymbolCollectingVisitor : ExpressionRewriter<object?>
{
    public HashSet<SearchParameterInfo> Parameters { get; } = [];

    public HashSet<string> ResourceTypes { get; } = [];

    public override Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
    {
        AddParameter(expression.Parameter);
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
        AddParameter(expression.ComponentSearchParameter);
        return base.VisitCompositeComponent(expression, context);
    }

    public override Expression VisitSearchParameter(SearchParameterExpression expression, object? context)
    {
        AddParameter(expression.Parameter);
        return base.VisitSearchParameter(expression, context);
    }

    public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, object? context)
    {
        AddParameter(expression.Parameter);
        return base.VisitMissingSearchParameter(expression, context);
    }

    public override Expression VisitChained(ChainedExpression expression, object? context)
    {
        AddParameter(expression.ReferenceSearchParameter);
        foreach (var resourceType in expression.ResourceTypes)
        {
            ResourceTypes.Add(resourceType);
        }

        foreach (var resourceType in expression.TargetResourceTypes)
        {
            ResourceTypes.Add(resourceType);
        }

        return base.VisitChained(expression, context);
    }

    public List<(string CompartmentType, ISet<string> FilteredResourceTypes)> Compartments { get; } = [];

    /// <summary>
    /// Records a compartment search's type and filter for Resolve to expand. Does no further recursion
    /// (a CompartmentSearchExpression has no child expression) and no I/O — Resolve, not this visitor,
    /// runs the definition-manager expansion, keeping this class's no-I/O contract intact.
    /// </summary>
    public override Expression VisitCompartment(CompartmentSearchExpression expression, object? context)
    {
        AddResourceType(expression.CompartmentType);
        Compartments.Add((expression.CompartmentType, expression.FilteredResourceTypes));
        return expression;
    }

    /// <summary>
    /// Collects the symbols an <see cref="IncludeExpression"/> references — its reference parameter (unless
    /// a wildcard) and every resource type on its source/target/referenced-type fields. Deliberately
    /// over-collects a superset rather than re-deriving which field a given include direction actually
    /// uses; resolving extra types is harmless. Called directly by Resolve per include, since an
    /// IncludeExpression is never part of the Expression tree. The wildcard sentinel "*" is skipped.
    /// </summary>
    public void CollectInclude(IncludeExpression include)
    {
        if (include.ReferenceSearchParameter is not null)
        {
            AddParameter(include.ReferenceSearchParameter);
            foreach (var targetType in include.ReferenceSearchParameter.TargetResourceTypes)
            {
                AddResourceType(targetType);
            }
        }

        AddResourceType(include.SourceResourceType);
        AddResourceType(include.TargetResourceType);
        foreach (var referencedType in include.ReferencedTypes ?? [])
        {
            AddResourceType(referencedType);
        }
    }

    /// <summary>
    /// Collects a sort key's search parameter for resolution, skipping the resource-column codes:
    /// _lastUpdated orders directly by ResourceSurrogateId, and _id/_type are rejected by BuildSortKey
    /// before any lookup. Called directly by Resolve per sort key, since a SortExpression is never part of
    /// the Expression tree.
    /// </summary>
    public void CollectSort(SortExpression sort)
    {
        AddParameter(sort.Parameter);
    }

    /// <summary>
    /// Records a parameter for resolution, skipping the resource-column codes. Those target dbo.Resource's
    /// own columns and never reach a SearchParamId lookup -- Lower extracts them into the outer predicate,
    /// sorting by one throws before any lookup, and the dispatchers reject them outright. Collecting them
    /// would make a resolver with no row for _id report the query unresolvable when it compiles fine.
    /// </summary>
    private void AddParameter(SearchParameterInfo parameter)
    {
        if (!ResourceColumnLoweringRule.IsResourceColumnCode(parameter.Code))
        {
            Parameters.Add(parameter);
        }
    }

    private void AddResourceType(string? resourceType)
    {
        if (resourceType is { Length: > 0 } and not "*")
        {
            ResourceTypes.Add(resourceType);
        }
    }
}
