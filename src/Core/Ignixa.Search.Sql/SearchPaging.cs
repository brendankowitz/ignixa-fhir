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
    /// Keyset paging: a <c>TOP</c> cap, positioned by a seek predicate over the sort keys. The default and
    /// the only shape that stays stable while rows are inserted underneath a paging client.
    /// </summary>
    /// <param name="Top">The row cap, or null for no cap. Rejected when negative.</param>
    /// <param name="Boundary">
    /// The keyset boundary decoded from the caller's continuation token, or null for the first page of
    /// <see cref="SearchPlanOptions.SortPhase"/>. Its value count must match that phase's active key count —
    /// every key when <see cref="Ast.SortPhase.Valued"/>, all but the primary when
    /// <see cref="Ast.SortPhase.MissingPrimary"/> — so a boundary is never carried across the segment handoff.
    /// A boundary is representable only here, which is what keeps it out of <see cref="Offset"/>: a seek
    /// predicate and <c>OFFSET … FETCH</c> are two independent paging mechanisms.
    /// </param>
    public sealed record Keyset(int? Top = null, PageSpec? Boundary = null) : SearchPaging;

    /// <summary>
    /// <c>OFFSET … FETCH</c> paging. Only for callers that need an absolute row offset — it re-reads and
    /// discards every preceding row, and it drifts when the underlying rows change between pages.
    /// </summary>
    /// <param name="Spec">The offset and page size. Required.</param>
    public sealed record Offset(OffsetSpec Spec) : SearchPaging;
}
