namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The keyset-pagination continuation token (boundary) of a global <c>$includes</c> page: the (type id,
/// surrogate id) of the last include row the previous page returned. The next page selects only rows ordered
/// after it under <c>ORDER BY T1 ASC, Sid1 ASC</c>, so paging the union of every include stage advances
/// without gaps or repeats. Both fields render as bound parameters rather than literals, for the same reason
/// as <see cref="SurrogateIdRange"/>: they are caller input, and inlining them would defeat plan reuse
/// across the pages of one operation, which differ only in their boundary.
/// </summary>
/// <remarks>
/// One boundary, not one per stage: the legacy protocol pages the UNION ALL of every include stage as a
/// single ordered stream keyed on (T1, Sid1), so every stage resumes from the same point. A per-stage
/// boundary would let one stage's rows overtake another's between pages and silently drop or duplicate
/// included resources at the page boundary — the very failure the shared ordered stream exists to prevent.
/// This concept parallels <see cref="PageSpec.BoundaryResourceTypeId"/> and <see cref="PageSpec.BoundarySurrogateId"/>
/// (which represent the boundary of a resource search page) and <see cref="KeysetContinuationToken"/>'s
/// keyset-pagination terminology.
/// </remarks>
/// <param name="TypeId">The resource type id of the last include row returned by the previous page.</param>
/// <param name="SurrogateId">The resource surrogate id of the last include row returned by the previous page.</param>
public sealed record IncludeBoundary(short TypeId, long SurrogateId);
