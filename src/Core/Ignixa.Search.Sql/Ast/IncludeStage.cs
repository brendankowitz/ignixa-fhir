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
    int Limit,
    IReadOnlyList<IncludeConstraint>? Constraints = null);

/// <summary>
/// One access-constraint binding on an include stage: the emitter renders it as a type-guarded EXISTS so
/// only rows of <see cref="ConstraintTypeId"/> are required to satisfy the constraint CTE at
/// <see cref="ConstraintCteIndex"/>, and rows of any other type the stage produces pass through untouched.
/// A trailing optional field on <see cref="IncludeStage"/> rather than a new stage kind: an unconstrained
/// stage leaves it null and emits exactly as before this field existed.
/// </summary>
/// <param name="ConstraintTypeId">The resource-type id the constraint governs.</param>
/// <param name="ConstraintCteIndex">The index into QueryPlan.Ctes of the CTE the constraint lowered to.</param>
public sealed record IncludeConstraint(short ConstraintTypeId, int ConstraintCteIndex);
