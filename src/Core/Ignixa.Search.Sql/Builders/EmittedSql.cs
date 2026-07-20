namespace Ignixa.Search.Sql.Builders;

/// <summary>One bound SQL parameter: its @pN name and the value to bind.</summary>
public sealed record EmittedSqlParameter(string Name, object Value);

/// <summary>
/// The compiled SQL text and its bound parameters. The result columns are (T1, Sid1) when the plan has
/// no includes, and (T1, Sid1, IsMatch, IsPartial) when it does — IsMatch is 1 for a match-page row and
/// 0 for an included row; IsPartial is 1 only on an included row whose stage TOP(@Limit) truncated
/// further rows. Callers pick the expected shape from plan.Includes.Count, not by inspecting columns.
/// </summary>
public sealed record EmittedSql(
    string Sql,
    IReadOnlyList<EmittedSqlParameter> Parameters,
    IReadOnlyList<SqlTextRange>? TextRanges = null);
