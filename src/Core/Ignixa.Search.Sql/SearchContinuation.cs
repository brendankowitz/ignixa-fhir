using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// Where in the result set a compile starts. A sorted search is delivered as two consecutive segments — the
/// resources carrying the primary sort key, then the resources missing it (see <see cref="SortPhase"/>) —
/// because a keyset seek has to be sargable against the search-parameter index, which rules out ordering a
/// single statement by a nullable key. Both coordinates of "where am I" therefore travel together here rather
/// than as loose compile options: the segment, and the boundary within it.
/// </summary>
/// <param name="Phase">Which segment this compile reads.</param>
/// <param name="Boundary">
/// The keyset boundary decoded from the caller's continuation token, or null for the first page of
/// <paramref name="Phase"/>. Its value count must match the phase's active key count — every key when
/// <see cref="SortPhase.Valued"/>, all but the primary when <see cref="SortPhase.MissingPrimary"/> — so a
/// boundary is never carried across the segment handoff.
/// </param>
public sealed record SearchContinuation(SortPhase Phase = SortPhase.Valued, PageSpec? Boundary = null);
