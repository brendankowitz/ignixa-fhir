using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>The explained plan plus each CTE's provenance.</summary>
/// <remarks>
/// <see cref="Rows"/> carries the same content as <see cref="Explain"/> with the label kept separate, so a
/// caller can address one plan line and join it to a parameter through <see cref="CteProvenance"/>.
/// <see cref="Explain"/> stays because it is the plan-shape golden format and the form a human reads.
/// </remarks>
public sealed record QueryPlanTrace(string Explain, IReadOnlyList<CteProvenance> Ctes, IReadOnlyList<PlanExplainRow> Rows);
