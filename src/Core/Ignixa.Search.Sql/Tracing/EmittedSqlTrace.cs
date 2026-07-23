using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>The emitted SQL plus its bound parameters and section ranges.</summary>
public sealed record EmittedSqlTrace(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters, IReadOnlyList<SqlTextRange> Ranges);
