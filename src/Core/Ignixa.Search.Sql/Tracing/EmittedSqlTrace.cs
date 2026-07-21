using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>The emitted SQL plus its section ranges.</summary>
public sealed record EmittedSqlTrace(string Sql, IReadOnlyList<SqlTextRange> Ranges);
