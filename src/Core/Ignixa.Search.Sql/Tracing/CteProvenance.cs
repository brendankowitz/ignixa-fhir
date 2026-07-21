using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>One CTE's link back to the parameter that produced it. Null ordinal where exempt —
/// :missing, compartment, and structural CTEs have no source text.</summary>
/// <remarks>
/// <see cref="ParameterOrdinal"/> is set only when the CTE was lowered from a node inside exactly one
/// parameter's IR. A structural CTE (Intersect, Union, Except, ChainJoin) combines other CTEs by
/// construction and so belongs to no single parameter — <see cref="ContributingOrdinals"/> is the set it
/// draws from, closed over its children, so a consumer can say "this join came from parameters 1 and 3"
/// without re-deriving the tree itself.
/// <para>
/// Not a positional record: <see cref="CteIndex"/> and <see cref="ParameterOrdinal"/> are plan and query
/// positions and must be non-negative, so construction validates rather than trusting every producer to
/// pass well-formed indices. The properties are get-only rather than <c>init</c> for the same reason — a
/// <c>with</c> expression copies through the compiler-generated copy constructor and would skip these
/// checks entirely, so the guard is only worth having if that route does not exist.
/// </para>
/// </remarks>
public sealed record CteProvenance
{
    public CteProvenance(
        int cteIndex,
        int? parameterOrdinal,
        SourceSpan? span,
        IReadOnlyList<int>? contributingOrdinals = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cteIndex);
        if (parameterOrdinal is { } ordinal)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(ordinal, nameof(parameterOrdinal));
        }

        CteIndex = cteIndex;
        ParameterOrdinal = parameterOrdinal;
        Span = span;

        // A directly-attributed CTE contributes itself, so the set is never empty where an ordinal exists
        // -- consumers can read ContributingOrdinals uniformly instead of branching on ParameterOrdinal.
        ContributingOrdinals = contributingOrdinals
            ?? (parameterOrdinal is { } own ? [own] : []);
    }

    public int CteIndex { get; }

    public int? ParameterOrdinal { get; }

    public SourceSpan? Span { get; }

    /// <summary>
    /// Every parameter ordinal this CTE draws from, ascending and distinct. Equals
    /// <see cref="ParameterOrdinal"/> alone for a directly-attributed CTE; the union of its children's sets
    /// for a structural one; empty where nothing is attributable.
    /// </summary>
    public IReadOnlyList<int> ContributingOrdinals { get; }

    public void Deconstruct(out int cteIndex, out int? parameterOrdinal, out SourceSpan? span)
    {
        cteIndex = CteIndex;
        parameterOrdinal = ParameterOrdinal;
        span = Span;
    }
}
