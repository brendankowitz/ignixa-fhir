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
/// <para>
/// <see cref="ContributingOrdinals"/> is normalized and copied on the way in, so the ascending, distinct,
/// non-negative shape the docs promise holds by construction rather than by producer discipline, and a
/// caller mutating the list it passed cannot reach inside afterwards. It is compared element-wise by
/// <see cref="Equals(CteProvenance)"/>, because a record's synthesized equality compares a collection
/// property by reference.
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

        if (contributingOrdinals is not null)
        {
            foreach (var contributor in contributingOrdinals)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(contributor, nameof(contributingOrdinals));
            }
        }

        CteIndex = cteIndex;
        ParameterOrdinal = parameterOrdinal;
        Span = span;

        // A directly-attributed CTE contributes itself, so the set is never empty where an ordinal exists
        // -- consumers can read ContributingOrdinals uniformly instead of branching on ParameterOrdinal.
        // Normalizing here rather than validating means a producer that hands over an unsorted or
        // duplicated set gets the documented shape instead of an exception it cannot act on.
        IEnumerable<int> contributors = contributingOrdinals ?? [];
        if (parameterOrdinal is { } own)
        {
            contributors = contributors.Append(own);
        }

        ContributingOrdinals = [.. contributors.Distinct().Order()];
    }

    public int CteIndex { get; }

    public int? ParameterOrdinal { get; }

    public SourceSpan? Span { get; }

    /// <summary>
    /// Every parameter ordinal this CTE draws from, ascending and distinct. Equals
    /// <see cref="ParameterOrdinal"/> alone for a directly-attributed CTE; the union of its children's sets
    /// for a structural one; empty only where nothing is attributable.
    /// </summary>
    public IReadOnlyList<int> ContributingOrdinals { get; }

    public bool Equals(CteProvenance? other)
        => other is not null
            && CteIndex == other.CteIndex
            && ParameterOrdinal == other.ParameterOrdinal
            && Span == other.Span
            && ContributingOrdinals.SequenceEqual(other.ContributingOrdinals);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CteIndex);
        hash.Add(ParameterOrdinal);
        hash.Add(Span);
        foreach (var ordinal in ContributingOrdinals)
        {
            hash.Add(ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Four-arity to match the property count. Deliberately not left at three: a stale
    /// <c>var (i, ord, span) = provenance</c> would keep compiling and silently ignore
    /// <see cref="ContributingOrdinals"/>, which is the field a consumer is most likely to have missed.
    /// </summary>
    public void Deconstruct(
        out int cteIndex,
        out int? parameterOrdinal,
        out SourceSpan? span,
        out IReadOnlyList<int> contributingOrdinals)
    {
        cteIndex = CteIndex;
        parameterOrdinal = ParameterOrdinal;
        span = Span;
        contributingOrdinals = ContributingOrdinals;
    }
}
