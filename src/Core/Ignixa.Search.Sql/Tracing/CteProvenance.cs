using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>One CTE's link back to the parameter that produced it. Null ordinal where exempt —
/// :missing, compartment, and structural CTEs have no source text.</summary>
/// <remarks>
/// Not a positional record: <see cref="CteIndex"/> and <see cref="ParameterOrdinal"/> are plan and query
/// positions and must be non-negative, so construction validates rather than trusting every producer to
/// pass well-formed indices.
/// </remarks>
public sealed record CteProvenance
{
    public CteProvenance(int cteIndex, int? parameterOrdinal, SourceSpan? span)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cteIndex);
        if (parameterOrdinal is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameterOrdinal), parameterOrdinal, "Parameter ordinal must be non-negative when present.");
        }

        CteIndex = cteIndex;
        ParameterOrdinal = parameterOrdinal;
        Span = span;
    }

    public int CteIndex { get; init; }

    public int? ParameterOrdinal { get; init; }

    public SourceSpan? Span { get; init; }

    public void Deconstruct(out int cteIndex, out int? parameterOrdinal, out SourceSpan? span)
    {
        cteIndex = CteIndex;
        parameterOrdinal = ParameterOrdinal;
        span = Span;
    }
}
