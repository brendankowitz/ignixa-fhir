using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql;

/// <summary>
/// A compiled search: the SQL text, its bound parameters, and the plan it came from. Read
/// <see cref="Query"/> to pick a result-row reader — <c>Query.Includes</c> and <c>Query.Projection</c>
/// determine the column shape.
/// </summary>
public sealed record CompiledSearch(
    string Sql,
    IReadOnlyList<EmittedSqlParameter> Parameters,
    QueryPlan Query)
{
    /// <summary>Plan-phase and emit-phase diagnostics merged; null at <see cref="SearchDiagnosticsLevel.None"/>.</summary>
    public SearchCompilationDiagnostics? Diagnostics { get; init; }
}
