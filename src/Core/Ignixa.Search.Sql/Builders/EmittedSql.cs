namespace Ignixa.Search.Sql.Builders;

/// <summary>One bound SQL parameter: its @pN name and the value to bind.</summary>
public sealed record EmittedSqlParameter(string Name, object Value);

/// <summary>
/// The compiled SQL text and its bound parameters. The result columns are (T1, Sid1) when the plan has
/// no includes, and (T1, Sid1, IsMatch, IsPartial) when it does — IsMatch is 1 for a match-page row and
/// 0 for an included row; IsPartial is 1 only on an included row whose stage TOP(@Limit) truncated
/// further rows. When the plan carries a non-null <see cref="QueryPlan.Projection"/>, the projected
/// dbo.Resource columns follow the identity (and flag) columns in the order declared by
/// <see cref="Ignixa.Search.Sql.Ast.ProjectionSpec.Columns"/>, readable by ordinal. Callers pick the
/// expected shape from <c>plan.Includes?.Count</c> and <c>plan.Projection?.Columns</c>, not by
/// inspecting the SQL text directly.
/// </summary>
public sealed record EmittedSql(
    string Sql,
    IReadOnlyList<EmittedSqlParameter> Parameters,
    IReadOnlyList<SqlTextRange>? TextRanges = null);
