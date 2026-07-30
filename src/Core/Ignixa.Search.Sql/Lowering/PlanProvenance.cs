namespace Ignixa.Search.Sql.Lowering;

/// <summary>CTE-to-IR links for a lowered plan. Partial by construction.</summary>
internal sealed record PlanProvenance(IReadOnlyList<CteOrigin> Origins);
