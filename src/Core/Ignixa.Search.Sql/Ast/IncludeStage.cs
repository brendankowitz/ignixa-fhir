namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One _include/_revinclude/:iterate stage. Not a <see cref="CteDefinition"/> (includes aren't predicates):
/// they occupy their own index space, emitted as an incN/incNlim CTE pair. SeedStages holds the indices of
/// earlier stages this one seeds from (topo-sorted by Lower); SeedFromMatch adds the match page as a seed. A
/// stage with neither seed can never produce rows and is never constructed.
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
/// One access-constraint binding on an include stage, emitted as a type-guarded EXISTS so only rows of
/// <see cref="ConstraintTypeId"/> must satisfy the constraint CTE at <see cref="ConstraintCteIndex"/>;
/// other types pass through. An optional trailing field on <see cref="IncludeStage"/>: null emits as before.
/// </summary>
/// <param name="ConstraintTypeId">The resource-type id the constraint governs.</param>
/// <param name="ConstraintCteIndex">The index into QueryPlan.Ctes of the CTE the constraint lowered to.</param>
public sealed record IncludeConstraint(short ConstraintTypeId, int ConstraintCteIndex);
