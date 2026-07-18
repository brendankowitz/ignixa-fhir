namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One `_include`/`_revinclude`/`:iterate` stage. Deliberately NOT a CteDefinition -- includes stay
/// outside QueryPlan.Ctes per the original design doc's "includes are not predicates" decision; a
/// stage is rendered as its own incN/incNlim CTE pair by Emit, indexed by its position in
/// QueryPlan.Includes (a separate index space from CteRef/QueryPlan.Ctes).
/// SeedStages holds indices into QueryPlan.Includes (never QueryPlan.Ctes) of every EARLIER stage
/// whose Produces intersects this stage's Requires -- populated by Lower's Kahn sort, the
/// load-bearing mechanism that lets Emit be a dumb renderer with no emitter-mutable registry to
/// maintain (contrast fhir-server's own _includeLimitCtesByResourceType). SeedFromMatch is true when
/// this stage ALSO seeds from cteMatchPage directly (every non-iterate stage; an iterate stage only
/// when its Requires intersects the match's own resource type). A stage with SeedStages = [] AND
/// SeedFromMatch = false is unreachable and never constructed -- see Lower's degenerate-case handling
/// (design doc §2).
/// See docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-include-design.md §2.
/// </summary>
public sealed record IncludeStage(
    IncludeDirection Direction,
    short? ReferenceSearchParamId,
    IReadOnlyList<short>? SeedTypeIds,
    IReadOnlyList<short>? OutputTypeIds,
    IReadOnlyList<int> SeedStages,
    bool SeedFromMatch,
    bool Iterate,
    int Limit);
