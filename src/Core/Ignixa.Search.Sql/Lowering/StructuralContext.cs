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
    private readonly string? _targetResourceType;

    public StructuralContext(SymbolTable symbols, string? targetResourceType = null)
    {
        _leafContext = new LeafContext(symbols);
        _targetResourceType = targetResourceType;
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public CteRef Lower(SearchParameterPredicateExpression predicate)
    {
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components)
    {
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
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

    private short ResolveTargetResourceTypeId()
        => _targetResourceType is not null
            ? _leafContext.ResourceTypeId(_targetResourceType)
            : throw new NotSupportedException(
                "This query needs a target resource type (:not, or a resource-column-only match) but " +
                "Lower.Run was not given one -- pass targetResourceType.");
}
