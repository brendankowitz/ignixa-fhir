namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Where a caller has reached in a result set: the keyset boundary of the last row it read, plus the segment
/// of a two-phase sort that row came from. This is the whole of the resume state, which is why
/// <see cref="KeysetContinuationToken"/> round-trips this type rather than a loose boundary — a boundary
/// alone cannot say which segment produced it, and the first page of either segment has none.
/// </summary>
/// <param name="BoundaryValues">
/// One value per active sort key for <paramref name="Phase"/>: <c>SortSpec.Keys.Count</c> under
/// <see cref="SortPhase.Valued"/>, one fewer under <see cref="SortPhase.MissingPrimary"/>, since the primary
/// key contributes no value when it is absent. Empty on an unsorted query.
/// </param>
/// <param name="BoundaryResourceTypeId">
/// The type of the boundary row. Always carried; a token-to-<see cref="PageSpec"/> adapter discards it for a
/// custom sort, where <see cref="PageSpec"/> requires a typeless boundary.
/// </param>
/// <param name="BoundarySurrogateId">The boundary row's surrogate id, which breaks the final tie.</param>
/// <param name="Phase">The segment this position was reached in. A position never crosses a phase boundary.</param>
public sealed record KeysetPosition(
    IReadOnlyList<string> BoundaryValues,
    int BoundaryResourceTypeId,
    long BoundarySurrogateId,
    SortPhase Phase);
