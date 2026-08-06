namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// An OFFSET/FETCH page: skip <paramref name="Offset"/> rows, return the <paramref name="Limit"/> rows of
/// the page, plus one lookahead row past its end when <paramref name="ProbeExtraRow"/> is set.
/// Mutually exclusive with keyset <see cref="PageSpec"/> and with a TOP cap — T-SQL rejects the
/// combination (error 10741).
/// </summary>
/// <param name="Offset">Rows skipped before the page begins.</param>
/// <param name="Limit">
/// The TRUE page size: how many rows the caller intends to hand back. Never pre-incremented for has-more
/// detection — a caller that over-fetches says so through <paramref name="ProbeExtraRow"/> instead.
/// </param>
/// <param name="ProbeExtraRow">
/// Fetch one row beyond the page so the caller can tell whether a further page exists. That row is a
/// lookahead, not a member of the page, and the distinction is load-bearing for _include/_revinclude:
/// include stages seed only from the first <paramref name="Limit"/> rows of the match page, so a probe row
/// the caller later trims cannot leave its included resources stranded in the bundle. Folding the +1 into
/// <paramref name="Limit"/> makes that distinction unrepresentable, which is exactly why it is a flag.
/// </param>
public sealed record OffsetSpec(int Offset, int Limit, bool ProbeExtraRow = false)
{
    /// <summary>Rows the emitted FETCH NEXT asks for: the page, plus its probe row when there is one.</summary>
    public int FetchCount => Limit + (ProbeExtraRow ? 1 : 0);
}
