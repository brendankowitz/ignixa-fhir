namespace Ignixa.Search.Sql.Builders;

/// <summary>One bound SQL parameter: its @pN name and the value to bind.</summary>
public sealed record EmittedSqlParameter(string Name, object Value);

/// <summary>
/// The compiled SQL text and its bound parameters. Result columns are (T1, Sid1), plus (IsMatch, IsPartial)
/// when the plan has includes, plus any <see cref="QueryPlan.Projection"/> columns in declared order.
/// IsMatch is 1 for a match-page row; IsPartial is 1 on an included row whose stage TOP truncated more rows.
/// Callers pick the shape from <c>plan.Includes?.Count</c> and <c>plan.Projection</c>, not from the SQL text.
/// </summary>
internal sealed record EmittedSql(
    string Sql,
    IReadOnlyList<EmittedSqlParameter> Parameters,
    IReadOnlyList<SqlTextRange>? TextRanges = null);
