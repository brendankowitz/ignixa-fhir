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
/// The tier-2 (structural) context: builds the CTE graph by dispatching leaves to tier-1 rules and
/// combining their results. Owns the plan's Ctes list -- LeafContext (tier 1) never sees it.
/// </summary>
public sealed class StructuralContext
{
    private readonly List<CteDefinition> _ctes = [];
    private readonly LeafContext _leafContext;
    private int _chainDepth;

    private const int MaxChainDepth = 10;

    public StructuralContext(SymbolTable symbols)
    {
        _leafContext = new LeafContext(symbols);
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public LeafContext LeafContext => _leafContext;

    public CteRef Lower(SearchParameterPredicateExpression predicate, string resourceType)
    {
        RejectResourceColumnCode(predicate.Parameter.Code);
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components, string resourceType)
    {
        foreach (var component in components)
        {
            RejectResourceColumnCode(component.ComponentSearchParameter.Code);
        }

        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
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
                $":missing is not supported for search parameter type '{parameter.Type}' on '{parameter.Code}' -- " +
                "composite types are handled separately (see ResolveMissingCompositeTable); Special is out of scope."),
        };

        return SqlCatalog.Default.Table(tableName);
    }

    private static void RejectResourceColumnCode(string parameterCode)
    {
        if (parameterCode is "_id" or "_type" or "_lastUpdated")
        {
            throw new NotSupportedException(
                $"A resource-column predicate ('{parameterCode}') reached the leaf/composite dispatch choke point -- " +
                "only Lower.Run's top-level extraction pass (via ResourceColumnLoweringRule) handles these. This " +
                "guard exists at StructuralContext's dispatch choke points (not just at LowerNode's generic leaf " +
                "arm) so every current and future caller of Lower/LowerComposite is covered structurally, rather " +
                "than relying on each caller happening to route through LowerNode first. Throwing rather than " +
                "silently routing a resource column into an unrelated leaf or composite rule's table, which would " +
                "silently produce a wrong-scope or always-empty match.");
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

    public CteRef LowerNot(CteRef innerMatch, string resourceType)
    {
        var baseRef = LowerResourceSource(resourceType);
        _ctes.Add(new CteDefinition.Except(baseRef, innerMatch));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerChain(ChainedExpression chain, Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        _chainDepth++;
        if (_chainDepth > MaxChainDepth)
        {
            throw new NotSupportedException(
                $"Chain nesting exceeds this compiler's 10-level depth guard -- this is a robustness ceiling against " +
                "SQL Server optimizer degradation under deeply nested CTE chains (see the chain design doc §8 for " +
                "the fhir-server precedent this mirrors), not a FHIR-spec limit. If a real query legitimately needs " +
                "more than 10 chain levels, this guard's threshold should be revisited deliberately, not silently raised.");
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
