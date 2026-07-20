namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One _include/_revinclude/:iterate stage. Not a <see cref="CteDefinition"/>: includes are not
/// predicates, so they live outside QueryPlan.Ctes in their own index space and Emit renders each as an
/// incN/incNlim CTE pair indexed by position in QueryPlan.Includes.
/// <para>
/// SeedStages holds the indices (into QueryPlan.Includes) of every earlier stage whose Produces
/// intersects this stage's Requires — computed by Lower's topological sort so Emit needs no mutable
/// registry of its own. SeedFromMatch is true when this stage also seeds directly from the match page
/// (always for a non-iterate stage; for an :iterate stage only when its Requires includes the match's
/// resource type). A stage with empty SeedStages and SeedFromMatch = false can never produce rows and
/// is never constructed.
/// </para>
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
