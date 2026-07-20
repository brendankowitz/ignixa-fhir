namespace Ignixa.Search.Sql.Lowering;

/// <summary>CTE-to-IR links for a lowered plan. Partial by construction — see the design spec.</summary>
public sealed record PlanProvenance(IReadOnlyList<CteOrigin> Origins);
