namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// An inclusive ResourceSurrogateId window applied to the match set, used to partition a bulk read
/// across workers. Both bounds render as bound parameters rather than literals: they are caller input,
/// and inlining them would defeat plan reuse across partitions that differ only in their window.
/// </summary>
/// <param name="Start">The inclusive lower bound.</param>
/// <param name="End">The inclusive upper bound.</param>
public sealed record SurrogateIdRange(SqlParameterRef Start, SqlParameterRef End);
