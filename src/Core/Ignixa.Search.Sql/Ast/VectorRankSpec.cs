namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Orders the match page by semantic distance: each match row's minimum cosine distance over its chunks for
/// <see cref="Source"/>'s parameter and model, computed by a correlated <c>CROSS APPLY</c> and projected as a
/// <c>Distance</c> column. Distance follows any <c>_sort</c> keys and precedes the identity tie-break.
/// </summary>
/// <remarks>
/// Holds the gate itself rather than copies of its parameter, model and embedding, so the ranking and the
/// gate cannot disagree about which vectors they read. <see cref="QueryPlanValidator"/> requires it to be the
/// exact instance in the plan's CTE list. Only a <see cref="ResultShape.Matches"/> plan can carry a ranking, and
/// never alongside a keyset boundary: the seek predicate has no distance term, so a second page would skip or
/// repeat rows. Offset paging re-runs the ordering and is unaffected.
/// </remarks>
/// <param name="Source">The plan's semantic gate.</param>
public sealed record VectorRankSpec(CteDefinition.VectorMatchSource Source);
