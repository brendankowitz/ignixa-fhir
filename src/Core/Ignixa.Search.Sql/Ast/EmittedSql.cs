namespace Ignixa.Search.Sql.Ast;

public sealed record EmittedSqlParameter(string Name, object Value);

/// <summary>
/// Result shape is (T1, Sid1) for any QueryPlan with no Includes (the overwhelming majority).
/// Whenever plan.Includes is non-empty, the shape is (T1, Sid1, IsMatch, IsPartial) instead --
/// IsMatch distinguishes an ordinary match-page row (1) from an included row (0); IsPartial (only
/// ever 1 on an included row) means that stage's TOP(@Limit) truncated further rows. Callers key off
/// plan.Includes.Count > 0 to know which shape to expect, not by inspecting column count at runtime.
/// </summary>
public sealed record EmittedSql(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters);
