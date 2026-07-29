using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql;

/// <summary>
/// One CTE's link back to the parameter that produced it. <see cref="ParameterOrdinal"/> is set only when
/// the CTE was lowered from a single parameter's IR; a structural CTE (Intersect, Union, Except, ChainJoin)
/// owns none, and <see cref="ContributingOrdinals"/> is the set it draws from, closed over its children.
/// </summary>
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

        // Append the CTE's own ordinal so a directly-attributed CTE contributes itself and the set is never
        // empty where an ordinal exists; normalize (rather than validate) so an unsorted/duplicated producer
        // input still yields the documented ascending-distinct shape.
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
    /// Four-arity to match the property count: a stale three-element deconstruction would keep compiling and
    /// silently ignore <see cref="ContributingOrdinals"/>.
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
