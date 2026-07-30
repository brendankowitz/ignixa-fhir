using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// How a compile bounds and positions the rows it returns. Closed: keyset and offset paging are alternatives,
/// not a combination — T-SQL rejects <c>TOP</c> alongside <c>OFFSET … FETCH</c> in one statement — so choosing
/// one makes the other unrepresentable rather than a rejected combination.
/// </summary>
public abstract record SearchPaging
{
    private SearchPaging()
    {
    }

    /// <summary>
    /// Which segment of a sorted result this compile reads. A sort on a nullable search parameter splits the
    /// result into the resources carrying the primary key and the resources missing it, because a keyset seek
    /// has to be sargable against the search-parameter index, which rules out ordering one statement by a
    /// nullable key. The segment is a coordinate of both paging mechanisms, so it lives here rather than on
    /// either case. Requesting <see cref="SortPhase.MissingPrimary"/> without a <c>_sort</c> is rejected.
    /// </summary>
    public SortPhase Phase { get; init; } = SortPhase.Valued;

    /// <summary>
    /// Keyset paging: a <c>TOP</c> cap, positioned by a seek predicate over the sort keys. The default and
    /// the only shape that stays stable while rows are inserted underneath a paging client.
    /// </summary>
    /// <param name="Top">The row cap, or null for no cap. Rejected when negative.</param>
    /// <param name="Boundary">
    /// The keyset boundary decoded from the caller's continuation token, or null for the first page of
    /// <see cref="Phase"/>. Its value count must match the phase's active key count — every key when
    /// <see cref="SortPhase.Valued"/>, all but the primary when <see cref="SortPhase.MissingPrimary"/> — so a
    /// boundary is never carried across the segment handoff.
    /// </param>
    public sealed record Keyset(int? Top = null, PageSpec? Boundary = null) : SearchPaging;

    /// <summary>
    /// <c>OFFSET … FETCH</c> paging. Only for callers that need an absolute row offset — it re-reads and
    /// discards every preceding row, and it drifts when the underlying rows change between pages.
    /// </summary>
    /// <param name="Spec">The offset and page size. Required.</param>
    public sealed record Offset(OffsetSpec Spec) : SearchPaging;
}
