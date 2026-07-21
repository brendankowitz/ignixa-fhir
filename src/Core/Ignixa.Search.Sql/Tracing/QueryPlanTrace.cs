namespace Ignixa.Search.Sql.Tracing;

/// <summary>The explained plan plus each CTE's provenance.</summary>
public sealed record QueryPlanTrace(string Explain, IReadOnlyList<CteProvenance> Ctes);
