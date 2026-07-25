namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The dbo.Resource columns the terminal SELECT returns alongside the identity columns. A null
/// projection on a <see cref="QueryPlan"/> keeps the historical (T1, Sid1) shape, where the caller
/// fetches rows itself; naming columns makes the compiler emit the whole statement instead.
/// </summary>
/// <remarks>
/// Column names are emitted verbatim, qualified with the terminal join's <c>r.</c> alias. They are
/// compiler-supplied identifiers, never user input, so no quoting or validation is applied — the same
/// trust boundary every other identifier in this emitter sits behind.
/// </remarks>
/// <param name="Columns">Column names in the order they should appear in the SELECT list.</param>
public sealed record ProjectionSpec(IReadOnlyList<string> Columns);
