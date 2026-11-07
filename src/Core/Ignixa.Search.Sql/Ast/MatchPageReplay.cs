namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The match page an <see cref="ResultShape.IncludesPage"/> seeds its include stages from, replayed exactly:
/// the first <paramref name="PageSize"/> matches in the requested sort order and
/// <c>SearchPlanOptions.SortPhase</c>, seeking past <paramref name="Boundary"/>. A surrogate-id window cannot
/// stand in for this under a custom sort, because sort order is unrelated to surrogate id.
/// </summary>
/// <remarks>
/// <para>
/// Carries no has-more probe row by construction: a replay re-reads a page whose extent is already known, so
/// <paramref name="PageSize"/> is the number of matches genuinely on that page — the original page's
/// <c>Top</c> minus any probe row, not the over-fetched cap.
/// </para>
/// <para>
/// The replay is defined relative to the plan's sort: the caller must compile it under the same <c>_sort</c> and
/// <c>SortPhase</c> that produced <paramref name="Boundary"/>. Neither is carried here, and only the boundary's
/// value count is checked against the phase, so a replay under a different sort with the same key count, or a
/// first-page replay under the wrong phase, silently replays a different page.
/// </para>
/// <para>
/// One replay reads one phase. A match page that straddled the hand-off between the two phases of a
/// missing-value sort needs one replay per phase, and each replay excludes only its own matches: merge their
/// rows by <c>(T1, Sid1)</c>, drop any resource that was a match anywhere on the original page, and page each
/// replay by its own <see cref="ResultShape.IncludesPage.Resume"/>.
/// </para>
/// </remarks>
/// <param name="PageSize">How many matches were on the page. Rejected when negative.</param>
/// <param name="Boundary">
/// The keyset boundary decoded from the continuation token that produced the page, or null when the page was
/// the first of its <see cref="SortPhase"/>. Subject to the same rules as <see cref="SearchPaging.Keyset.Boundary"/>:
/// typeless for a custom sort, typed otherwise, and decoded for the current phase.
/// </param>
public sealed record MatchPageReplay(int PageSize, PageSpec? Boundary = null);
