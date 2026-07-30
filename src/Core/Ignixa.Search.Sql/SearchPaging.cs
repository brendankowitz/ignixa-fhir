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
    /// <param name="Top">The row cap, or null for no cap.</param>
    /// <param name="From">Where to resume, or null to start at the first page of the first segment.</param>
    public sealed record Keyset(int? Top = null, SearchContinuation? From = null) : SearchPaging;

    /// <summary>
    /// <c>OFFSET … FETCH</c> paging. Only for callers that need an absolute row offset — it re-reads and
    /// discards every preceding row, and it drifts when the underlying rows change between pages.
    /// </summary>
    /// <param name="Spec">The offset and page size.</param>
    public sealed record Offset(OffsetSpec Spec) : SearchPaging;
}
