using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>One CTE's link back to the parameter that produced it. Null ordinal where exempt —
/// :missing, compartment, and structural CTEs have no source text.</summary>
public sealed record CteProvenance(int CteIndex, int? ParameterOrdinal, SourceSpan? Span);
