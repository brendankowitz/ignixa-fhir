using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// The explained plan plus each CTE's provenance. <see cref="Rows"/> is the structured form of
/// <see cref="Explain"/>, so a caller can address one plan line and join it to a parameter via
/// <see cref="CteProvenance"/>; <see cref="Explain"/> stays as the human-readable golden format.
/// </summary>
public sealed record QueryPlanTrace(string Explain, IReadOnlyList<CteProvenance> Ctes, IReadOnlyList<PlanExplainRow> Rows);
