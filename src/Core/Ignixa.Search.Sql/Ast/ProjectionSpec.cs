namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The dbo.Resource columns the terminal SELECT returns alongside the identity columns. A null
/// projection on a <see cref="QueryPlan"/> keeps the historical (T1, Sid1) shape, where the caller
/// fetches rows itself; naming columns makes the compiler emit the whole statement instead.
/// </summary>
/// <remarks>
/// Column names are bracket-quoted before emission, with any embedded <c>]</c> escaped as <c>]]</c>.
/// This matters because <see cref="ProjectionSpec"/> is public and populated by the consuming data layer:
/// unlike every other identifier in the emitter, which is catalog-derived or predicate-tree-internal,
/// these values can originate outside the compiler. Quoting ensures a caller cannot inject SQL through
/// a column name even if input validation in the calling layer is absent or incomplete.
/// </remarks>
/// <param name="Columns">Column names in the order they should appear in the SELECT list.</param>
public sealed record ProjectionSpec(IReadOnlyList<string> Columns);
