namespace Ignixa.Search.Sql.Ast;

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

    // A record synthesizes a protected copy constructor that an external assembly could chain to,
    // adding a third paging mode this compiler would not recognise. An abstract private protected member
    // cannot be implemented outside this assembly, so the union is closed in fact and not by convention.
    // See ResultShape for why InternalsVisibleTo does not weaken this.
    private protected abstract void ThisUnionIsClosed();

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
    /// <param name="TopIncludesProbeRow">
    /// True when <paramref name="Top"/> is the caller's page size plus one lookahead row, fetched so the
    /// caller can tell whether a further page exists. The distinction is load-bearing for _include/_revinclude:
    /// include stages must seed only from the rows genuinely on the page, or the probe row the caller later
    /// trims leaves its included resources stranded in the bundle. It is a flag rather than an inference
    /// because the compiler cannot tell <c>Top: 11</c> meaning "eleven rows" from <c>Top: 11</c> meaning
    /// "ten rows plus a probe" — callers transform <c>MaxItemCount + 1</c> themselves before compiling.
    /// Requires <paramref name="Top"/>; see <see cref="OffsetSpec.ProbeExtraRow"/> for the OFFSET/FETCH
    /// equivalent.
    /// </param>
    public sealed record Keyset(int? Top = null, PageSpec? Boundary = null, bool TopIncludesProbeRow = false) : SearchPaging
    {
        private protected override void ThisUnionIsClosed()
        {
        }
    }

    /// <summary>
    /// <c>OFFSET … FETCH</c> paging. Only for callers that need an absolute row offset — it re-reads and
    /// discards every preceding row, and it drifts when the underlying rows change between pages.
    /// </summary>
    /// <param name="Spec">The offset and page size. Required.</param>
    public sealed record Offset(OffsetSpec Spec) : SearchPaging
    {
        private protected override void ThisUnionIsClosed()
        {
        }
    }
}
