namespace Ignixa.Search.Sql.Ast;

public sealed record EmittedSqlParameter(string Name, object Value);

public sealed record EmittedSql(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters);
