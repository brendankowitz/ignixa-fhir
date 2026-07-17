using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The tier-2 (structural) context: builds the CTE graph by dispatching leaves to tier-1 rules and
/// combining their results. Owns the plan's Ctes list -- LeafContext (tier 1) never sees it.
/// </summary>
public sealed class StructuralContext
{
    private readonly List<CteDefinition> _ctes = [];
    private readonly LeafContext _leafContext;
    private readonly string _targetResourceType;

    public StructuralContext(SymbolTable symbols, string targetResourceType)
    {
        _leafContext = new LeafContext(symbols);
        _targetResourceType = targetResourceType;
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public CteRef Lower(SearchParameterPredicateExpression predicate)
    {
        RejectResourceColumnCode(predicate.Parameter.Code);
        var resourceTypeId = _leafContext.ResourceTypeId(_targetResourceType);
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components)
    {
        foreach (var component in components)
        {
            RejectResourceColumnCode(component.ComponentSearchParameter.Code);
        }

        var resourceTypeId = _leafContext.ResourceTypeId(_targetResourceType);
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
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

    public CteRef LowerResourceSource()
    {
        var resourceTypeId = ResolveTargetResourceTypeId();
        _ctes.Add(new CteDefinition.ResourceSource(resourceTypeId));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerNot(CteRef innerMatch)
    {
        var baseRef = LowerResourceSource();
        _ctes.Add(new CteDefinition.Except(baseRef, innerMatch));
        return new CteRef(_ctes.Count - 1);
    }

    private short ResolveTargetResourceTypeId() => _leafContext.ResourceTypeId(_targetResourceType);
}
