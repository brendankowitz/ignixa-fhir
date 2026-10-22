namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The match page an <see cref="ResultShape.IncludesPage"/> seeds its include stages from, replayed exactly:
/// the first <paramref name="PageSize"/> matches in the requested sort order and
/// <c>SearchPlanOptions.SortPhase</c>, seeking past <paramref name="Boundary"/>. A surrogate-id window cannot
/// stand in for this under a custom sort, because sort order is unrelated to surrogate id.
/// </summary>
/// <remarks>
/// Carries no has-more probe row by construction: a replay re-reads a page whose extent is already known, so
/// <paramref name="PageSize"/> is the number of matches genuinely on that page — the original compile's
/// <c>Top</c> minus its probe row, not the over-fetched cap.
/// </remarks>
/// <param name="PageSize">How many matches were on the page. Rejected when negative.</param>
/// <param name="Boundary">
/// The keyset boundary decoded from the continuation token that produced the page, or null when the page was
/// the first of its <see cref="SortPhase"/>. Subject to the same rules as <see cref="SearchPaging.Keyset.Boundary"/>:
/// typeless for a custom sort, typed otherwise, and decoded for the current phase.
/// </param>
public sealed record MatchPageReplay(int PageSize, PageSpec? Boundary = null);
