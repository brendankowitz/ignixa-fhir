namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// An OFFSET/FETCH page: skip <paramref name="Offset"/> rows, return at most <paramref name="Limit"/>.
/// Mutually exclusive with keyset <see cref="PageSpec"/> and with a TOP cap — T-SQL rejects the
/// combination (error 10741).
/// </summary>
public sealed record OffsetSpec(int Offset, int Limit);
