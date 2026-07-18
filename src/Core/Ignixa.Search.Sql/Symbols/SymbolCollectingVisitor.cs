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
/// <see cref="Expression"/> tree. Three exceptions collect resource-type identity directly from a
/// leaf this visitor already walks: <see cref="ReferenceSearchValue.ResourceType"/>, when present (task
/// 8); a <c>_type</c> predicate's own <see cref="TokenSearchValue.Code"/> -- unlike <c>_id</c>'s
/// value (an opaque string, never a resource-type identity) or <c>_lastUpdated</c>'s (a timestamp),
/// <c>_type</c>'s value names the very resource type <c>ResourceColumnLoweringRule.TryLower</c>'s
/// <c>TypeEquals</c> arm needs to resolve, so without collecting it here every <c>_type</c> value other
/// than the query's own <c>targetResourceType</c> would throw; and <see cref="VisitChained"/>, which
/// collects a <see cref="ChainedExpression"/>'s <c>ReferenceSearchParameter</c> and every type in both
/// <c>ResourceTypes</c> and <c>TargetResourceTypes</c> -- forward and reverse chains alike, since which
/// array carries the "source" vs. "target" side flips with <c>Reversed</c>. As of Phase 7,
/// <see cref="CollectInclude"/> collects an IncludeExpression's own symbols the same way -- not via a
/// visitor override (IncludeExpression is never part of this Expression tree), but as a direct method
/// Resolve calls once per include/revinclude entry. As of Phase 8, VisitCompartment collects a
/// CompartmentSearchExpression's own CompartmentType (added to ResourceTypes, since the compiled
/// predicate needs it to filter ReferenceResourceTypeId) and records the full (CompartmentType,
/// FilteredResourceTypes) pair into Compartments for Resolve to expand via
/// ICompartmentDefinitionManager/ISearchParameterDefinitionManager -- see Resolve's remarks for the
/// full argument. As of Phase 8 part 2, CollectSort collects a SortExpression's own SearchParameterInfo the same way CollectInclude does -- a direct method, not a visitor override, since SortExpression is also never part of this Expression tree.
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

    public override Expression VisitChained(ChainedExpression expression, object? context)
    {
        Parameters.Add(expression.ReferenceSearchParameter);
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
    /// Records a CompartmentSearchExpression's own type/filter for Resolve to expand -- unlike
    /// VisitChained, this override does no further recursion (CompartmentSearchExpression has no
    /// child Expression field to walk into). Resolve, not this visitor, does the actual
    /// ICompartmentDefinitionManager/ISearchParameterDefinitionManager expansion -- this class's own
    /// contract is tree traversal without I/O; recording the raw (type, filter) pair here and
    /// resolving it in Resolve keeps that contract intact for compartment search the same way it
    /// already does for every other collected symbol.
    /// </summary>
    public override Expression VisitCompartment(CompartmentSearchExpression expression, object? context)
    {
        AddResourceType(expression.CompartmentType);
        Compartments.Add((expression.CompartmentType, expression.FilteredResourceTypes));
        return expression;
    }

    /// <summary>
    /// Collects the symbols an <see cref="IncludeExpression"/> references -- its own
    /// <c>ReferenceSearchParameter</c> (when not a wildcard), and every resource type appearing in
    /// <c>SourceResourceType</c>, <c>TargetResourceType</c>, <c>ReferenceSearchParameter.TargetResourceTypes</c>,
    /// and <c>ReferencedTypes</c>. This over-collects relative to what <c>Requires</c>/<c>Produces</c>
    /// actually uses for any one <see cref="IncludeExpression"/> instance (their exact source field
    /// depends on which of <c>TargetResourceType</c>/<c>ReferenceSearchParameter.TargetResourceTypes</c>/
    /// <c>WildCard</c> is populated) -- resolving a superset is safe, matching <see cref="VisitChained"/>'s
    /// existing precedent of collecting both <c>ResourceTypes</c> and <c>TargetResourceTypes</c> rather than
    /// re-deriving which one a given chain direction actually needs. Not a visitor override:
    /// <see cref="IncludeExpression"/> lives on <c>SearchOptions.Include</c>/<c>RevInclude</c>, never on the
    /// <see cref="Expression"/> tree this visitor walks, so <c>Resolve</c> calls this directly per include.
    /// The literal sentinel string "*" (a <c>_revinclude</c> wildcard-source's <c>SourceResourceType</c>,
    /// design doc §1.2) is skipped, never added as a resource type to resolve.
    /// </summary>
    public void CollectInclude(IncludeExpression include)
    {
        if (include.ReferenceSearchParameter is not null)
        {
            Parameters.Add(include.ReferenceSearchParameter);
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
    /// Collects a SortExpression's own SearchParameterInfo for the existing SearchParamId resolution
    /// loop -- _lastUpdated needs no SearchParamId at all (it lowers to a direct ResourceSurrogateId
    /// ordering, matching the compiler's existing precedent that treats _lastUpdated as a derived
    /// function of the surrogate id, per the sixth increment's ResourceColumnLoweringRule), so it is
    /// deliberately skipped here rather than added and later failing SymbolTable.SearchParamId. Not a
    /// visitor override: SortExpression lives on SearchOptions.Sort, never on the Expression tree this
    /// visitor walks, so Resolve calls this directly per sort key.
    /// </summary>
    public void CollectSort(SortExpression sort)
    {
        if (sort.Parameter.Code != "_lastUpdated")
        {
            Parameters.Add(sort.Parameter);
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
