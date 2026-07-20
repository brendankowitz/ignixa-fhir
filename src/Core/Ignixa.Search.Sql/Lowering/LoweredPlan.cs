using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>A lowered plan and its provenance. Provenance rides alongside the plan, never inside it,
/// because QueryPlan and its nodes are records where an added field would land in generated equality.</summary>
public sealed record LoweredPlan(QueryPlan Plan, PlanProvenance Provenance);
