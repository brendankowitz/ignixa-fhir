using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The structural (tier-2) context: builds the CTE graph by dispatching leaves to the leaf rules and
/// combining their results with Intersect/Union/Except. Owns the plan's Ctes list, which the leaf-tier
/// <see cref="LeafContext"/> never sees.
/// </summary>
public sealed class StructuralContext
{
    private readonly List<CteDefinition> _ctes = [];
    private readonly List<CteOrigin> _origins = [];
    private readonly LeafContext _leafContext;
    private int _chainDepth;

    private const int MaxChainDepth = 10;

    public StructuralContext(SymbolTable symbols, DateTimeOffset? approximationReferenceTime = null)
    {
        _leafContext = new LeafContext(symbols, approximationReferenceTime);
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public IReadOnlyList<CteOrigin> Origins => _origins;

    public LeafContext LeafContext => _leafContext;

    public CteRef Lower(SearchParameterPredicateExpression predicate, string resourceType)
        => Lower(predicate, resourceType, provenanceNode: predicate);

    /// <summary>Lowers a leaf predicate, recording provenance against <paramref name="provenanceNode"/> rather
    /// than <paramref name="predicate"/> itself — needed at the :not clone site, where the predicate actually
    /// lowered is a synthesized positive-match clone with no place in any parameter's IR subtree.</summary>
    public CteRef Lower(SearchParameterPredicateExpression predicate, string resourceType, Expression provenanceNode)
    {
        RejectResourceColumnCode(predicate.Parameter.Code);
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }

    /// <summary>
    /// Lowers a <c>_not-referenced</c> search to a NotReferencedSource CTE: resources of the target type
    /// that no reference row points at. A named source type and reference path narrow the anti-join; a
    /// path that did not resolve to a reference parameter falls back to a source-type-only (path-agnostic)
    /// filter, matching the shipping engine.
    /// </summary>
    public CteRef LowerNotReferenced(NotReferencedExpression expression, string resourceType)
    {
        var targetTypeId = _leafContext.ResourceTypeId(resourceType);

        // A source type the resolver could not find yields UnmatchableResourceTypeId (-1), which Emit
        // renders as `rsp.ResourceTypeId = -1` inside the anti-join subquery. No row has that id, so the
        // inner EXISTS is empty and NOT EXISTS is vacuously true -- every target passes. That is the
        // OPPOSITE of the sentinel's effect in a positive position (an empty match), yet it is the correct
        // answer here: a source type that does not exist has no reference rows, so no target is referenced
        // by it, so all targets are "not referenced by it". The unmatchable target type at the outer scan
        // still (correctly) matches nothing.
        short? sourceTypeId = expression.SourceResourceType is { } sourceType
            ? _leafContext.ResourceTypeId(sourceType)
            : null;

        short? referenceParamId =
            expression.SourceResourceType is { } src
            && expression.ReferencePath is { } path
            && _leafContext.NotReferencedPath(src, path) is { } parameter
                ? _leafContext.SearchParamId(parameter)
                : null;

        _ctes.Add(new CteDefinition.NotReferencedSource(targetTypeId, sourceTypeId, referenceParamId));
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, expression));
        return new CteRef(index);
    }

    /// <summary>Lowers a <c>:text</c> search, which reads dbo.TokenText rather than a search-param table.</summary>
    public CteRef LowerTokenText(SearchParameterInfo parameter, StringExpression expression, string resourceType, Expression provenanceNode)
    {
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        _ctes.Add(TokenTextLoweringRule.Lower(parameter, expression, _leafContext, resourceTypeId));
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components, string resourceType, Expression provenanceNode)
    {
        foreach (var component in components)
        {
            RejectResourceColumnCode(component.ComponentSearchParameter.Code);
        }

        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }

    public CteRef LowerParameterPresence(SearchParameterInfo parameter, string resourceType)
    {
        RejectResourceColumnCode(parameter.Code);

        var table = ResolveMissingTable(parameter);
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var searchParamId = _leafContext.SearchParamId(parameter);

        var cte = new CteDefinition.ParamSource(table, resourceTypeId, searchParamId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    private static TableDescriptor ResolveMissingTable(SearchParameterInfo parameter)
    {
        if (parameter.Type == SearchParamType.Composite)
        {
            return ResolveMissingCompositeTable(parameter);
        }

        var tableName = parameter.Type switch
        {
            SearchParamType.String => "StringSearchParam",
            SearchParamType.Token => "TokenSearchParam",
            SearchParamType.Reference => "ReferenceSearchParam",
            SearchParamType.Uri => "UriSearchParam",
            SearchParamType.Number => "NumberSearchParam",
            SearchParamType.Quantity => "QuantitySearchParam",
            SearchParamType.Date => "DateTimeSearchParam",
            _ => throw new NotSupportedException(
                $":missing is not supported for search parameter type '{parameter.Type}' on '{parameter.Code}'."),
        };

        return SqlCatalog.Default.Table(tableName);
    }

    private static TableDescriptor ResolveMissingCompositeTable(SearchParameterInfo parameter)
    {
        var componentTypes = parameter.Component.Select(c => c.ResolvedSearchParameter?.Type).ToArray();

        var tableName = componentTypes switch
        {
            [SearchParamType.Token, SearchParamType.Token] => "TokenTokenCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Number, SearchParamType.Number] => "TokenNumberNumberCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.String] => "TokenStringCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Quantity] => "TokenQuantityCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Date] => "TokenDateTimeCompositeSearchParam",
            [SearchParamType.Reference, SearchParamType.Token] => "ReferenceTokenCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Reference] => "ReferenceTokenCompositeSearchParam",
            var types => throw new NotSupportedException(
                $":missing is not supported for composite search parameter '{parameter.Code}' with component types " +
                $"[{string.Join(", ", types.Select(t => t?.ToString() ?? "unresolved"))}] -- no matching composite table."),
        };

        return SqlCatalog.Default.Table(tableName);
    }

    private static void RejectResourceColumnCode(string parameterCode)
    {
        if (ResourceColumnLoweringRule.IsResourceColumnCode(parameterCode))
        {
            throw new NotSupportedException(
                $"A resource-column predicate ('{parameterCode}') reached the leaf/composite dispatch — only " +
                "Lower.Run's top-level extraction pass (via ResourceColumnLoweringRule) handles these. Guarding here, " +
                "at the dispatch choke point, covers every caller of Lower/LowerComposite structurally. Throwing " +
                "rather than routing a resource column into an unrelated table, which would silently produce a " +
                "wrong-scope or always-empty match.");
        }
    }

    public CteRef Intersect(CteRef left, CteRef right)
    {
        _ctes.Add(new CteDefinition.Intersect(left, right));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef Union(IReadOnlyList<CteRef> parts)
    {
        _ctes.Add(new CteDefinition.Union(parts));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerResourceSource(string resourceType) => LowerResourceSourceWithPredicate(resourceType, predicate: null);

    public CteRef LowerResourceSourceWithPredicate(string resourceType, Predicate? predicate)
    {
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        _ctes.Add(new CteDefinition.ResourceSource(resourceTypeId, predicate));
        return new CteRef(_ctes.Count - 1);
    }

    /// <summary>
    /// Lowers a multi-type or system-wide base set. Each name is resolved through the symbol table; an
    /// unresolvable name yields the sentinel -1, which is kept in the list rather than dropped.
    /// <para>
    /// Dropping unresolvable ids would be dangerous: if every requested type is unknown the list would
    /// collapse to empty, and an empty <see cref="CteDefinition.MultiTypeResourceSource"/> means
    /// <em>every</em> resource type — a full-table scan instead of an empty match. The sentinel -1
    /// matches no row, so keeping it produces the correct empty result without widening the query.
    /// </para>
    /// <para>
    /// An empty <paramref name="resourceTypes"/> input is the explicit system-wide contract ("all types"):
    /// <see cref="CteDefinition.MultiTypeResourceSource.AllTypes"/> is called in that case so the intent
    /// is named rather than inferred from an empty list.
    /// </para>
    /// </summary>
    public CteRef LowerMultiTypeResourceSource(IReadOnlyList<string> resourceTypes)
    {
        // Use ResourceTypeIdOrSentinel rather than ResourceTypeId so that a type name not present in the
        // symbol table (never collected) maps to -1 rather than throwing. This matters for the fail-safe
        // contract: dropping unresolvable ids would collapse an all-unknown list to empty, which means
        // "every resource type" — a full-table scan instead of the correct empty result. Keeping -1
        // produces IN (-1), which matches no row. See also the comment at EmitMultiTypeResourceSource.
        //
        // An empty resourceTypes input is the explicit system-wide contract ("all types"): the caller at
        // LowerBaseSet deliberately passes an empty list for a bare GET /. Use AllTypes() in that case to
        // make the intent unambiguous; use ForTypes() for every non-empty list so the guard in ForTypes
        // enforces that no future caller can accidentally pass an empty list and silently widen.
        CteDefinition.MultiTypeResourceSource source = resourceTypes.Count == 0
            ? CteDefinition.MultiTypeResourceSource.AllTypes()
            : CteDefinition.MultiTypeResourceSource.ForTypes(
                resourceTypes.Select(t => _leafContext.ResourceTypeIdOrSentinel(t)).ToList());

        _ctes.Add(source);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerNot(CteRef innerMatch, string resourceType)
        => Except(LowerResourceSource(resourceType), innerMatch);

    /// <summary>
    /// Subtracts one match set from another. Callers that already hold a narrower left-hand set should
    /// use this directly rather than <see cref="LowerNot"/>, whose ResourceSource anchor reads every
    /// resource of the type.
    /// </summary>
    public CteRef Except(CteRef left, CteRef right)
    {
        _ctes.Add(new CteDefinition.Except(left, right));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerChain(ChainedExpression chain, Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        _chainDepth++;
        if (_chainDepth > MaxChainDepth)
        {
            throw new NotSupportedException(
                $"Chain nesting exceeds this compiler's 10-level depth guard — a robustness ceiling against SQL Server " +
                "optimizer degradation under deeply nested CTE chains, not a FHIR-spec limit. If a real query " +
                "legitimately needs more than 10 chain levels, raise this threshold deliberately.");
        }

        try
        {
            if (chain.Reversed)
            {
                var referencingResourceType = chain.ResourceTypes switch
                {
                    [var single] => single,
                    _ => throw new NotSupportedException(
                        $"Reverse chain's referencing side resolved to {chain.ResourceTypes.Length} types -- the real binder " +
                        "always binds a reverse chain's target expression against a single referencing type " +
                        "(SearchKeyBinder.BindReverse's syntax.SourceResourceType), so this is unexpected input."),
                };

                var innerMatch = lowerNode(chain.Expression, this, referencingResourceType);
                var referenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
                var innerResourceTypeId = _leafContext.ResourceTypeId(referencingResourceType);
                var outputResourceTypeIds = chain.TargetResourceTypes.Select(_leafContext.ResourceTypeId).ToList();

                _ctes.Add(new CteDefinition.ChainJoin(innerMatch, referenceSearchParamId, innerResourceTypeId, outputResourceTypeIds, ChainDirection.Reverse));
                return new CteRef(_ctes.Count - 1);
            }

            var targetResourceType = chain.TargetResourceTypes switch
            {
                [var single] => single,
                _ => throw new NotSupportedException(
                    $"Forward chain resolved to {chain.TargetResourceTypes.Length} candidate target types -- the real binder " +
                    "always resolves forward chains to exactly one target type before this point (SearchKeyBinder.BindForward " +
                    "throws ChainedParameterSpecifyType on genuine ambiguity), so this is unexpected input."),
            };

            var forwardInnerMatch = lowerNode(chain.Expression, this, targetResourceType);
            var forwardReferenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
            var forwardInnerResourceTypeId = _leafContext.ResourceTypeId(targetResourceType);
            var forwardOutputResourceTypeIds = chain.ResourceTypes.Select(_leafContext.ResourceTypeId).ToList();

            _ctes.Add(new CteDefinition.ChainJoin(forwardInnerMatch, forwardReferenceSearchParamId, forwardInnerResourceTypeId, forwardOutputResourceTypeIds, ChainDirection.Forward));
            return new CteRef(_ctes.Count - 1);
        }
        finally
        {
            _chainDepth--;
        }
    }

    public CteRef LowerCompartment(CompartmentSearchExpression expression)
    {
        var membership = _leafContext.CompartmentMembership(expression.CompartmentType);
        var groups = expression.FilteredResourceTypes.Count == 0
            ? membership
            : membership
                .Select(m => (m.Parameter, ResourceTypes: (IReadOnlyList<string>)m.ResourceTypes.Where(expression.FilteredResourceTypes.Contains).ToList()))
                .Where(m => m.ResourceTypes.Count > 0)
                .ToList();

        if (groups.Count == 0)
        {
            throw new NotSupportedException(
                $"Compartment search for '{expression.CompartmentType}/{expression.CompartmentId}' resolved to " +
                "zero membership search parameters for the requested resource type(s) -- this compartment/filter " +
                "combination can never match any row. Callers should short-circuit this case before calling " +
                "Lower (matching CompartmentSearchQueryGenerator's own empty-result short-circuit today), not " +
                "rely on this throw.");
        }

        var refs = groups.Select(g =>
        {
            var cte = CompartmentLoweringRule.Lower(g.Parameter, g.ResourceTypes, expression.CompartmentType, expression.CompartmentId, _leafContext);
            _ctes.Add(cte);
            return new CteRef(_ctes.Count - 1);
        }).ToList();

        return Union(refs);
    }
}
