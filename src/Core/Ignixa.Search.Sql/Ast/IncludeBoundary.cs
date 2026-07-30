namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Keyset continuation boundary of a global <c>$includes</c> page: the (type id, surrogate id) of the last
/// include row returned. The next page selects rows ordered after it under <c>ORDER BY T1 ASC, Sid1 ASC</c>,
/// paging the <c>UNION</c> of every stage as one deduplicated stream — one boundary, not one per stage, or
/// rows drop or duplicate at the page seam. Both fields bind as parameters so plan reuse survives across pages.
/// </summary>
/// <param name="TypeId">The resource type id of the last include row returned by the previous page.</param>
/// <param name="SurrogateId">The resource surrogate id of the last include row returned by the previous page.</param>
public sealed record IncludeBoundary(short TypeId, long SurrogateId);
