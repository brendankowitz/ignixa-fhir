using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>The plan's CTE accumulator. A <see cref="CteRef"/> is positional — an index into this list — so every
/// append is funnelled through <see cref="Add(CteDefinition)"/>, which mints the index from the same append that
/// produced it. The provenance overload is the only way an origin is recorded, keeping <see cref="Origins"/> from
/// drifting out of step with <see cref="Ctes"/>.</summary>
internal sealed class CteGraphBuilder
{
    private readonly List<CteDefinition> _ctes = [];
    private readonly List<CteOrigin> _origins = [];

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public IReadOnlyList<CteOrigin> Origins => _origins;

    /// <summary>Appends a CTE and returns the ref naming it.</summary>
    public CteRef Add(CteDefinition cte)
    {
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    /// <summary>Appends a CTE and records the IR node it was lowered from, for <see cref="PlanProvenance"/>.</summary>
    public CteRef Add(CteDefinition cte, Expression provenanceNode)
    {
        _ctes.Add(cte);
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }

    public CteRef Intersect(CteRef left, CteRef right) => Add(new CteDefinition.Intersect(left, right));

    public CteRef Union(IReadOnlyList<CteRef> parts) => Add(new CteDefinition.Union(parts));

    public CteRef Except(CteRef left, CteRef right) => Add(new CteDefinition.Except(left, right));
}
