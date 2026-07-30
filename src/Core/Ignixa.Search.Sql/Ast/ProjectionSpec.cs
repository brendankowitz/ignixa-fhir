namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The dbo.Resource columns the terminal SELECT returns alongside the identity columns. A null projection on
/// a <see cref="QueryPlan"/> keeps the identity-only (T1, Sid1) shape where the caller fetches rows itself;
/// naming columns makes the compiler emit the whole statement. Names are bracket-quoted (<c>]</c> as
/// <c>]]</c>) before emission — they can originate outside the compiler, so quoting blocks SQL injection.
/// </summary>
/// <param name="Columns">Column names in the order they should appear in the SELECT list.</param>
public sealed record ProjectionSpec(IReadOnlyList<string> Columns);
