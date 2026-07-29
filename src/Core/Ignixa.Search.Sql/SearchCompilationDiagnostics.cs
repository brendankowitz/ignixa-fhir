using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql;

/// <summary>
/// What a compile recorded about its own work. Present only when
/// <see cref="SearchPlanOptions.DiagnosticsLevel"/> is above <see cref="SearchDiagnosticsLevel.None"/>. On
/// the <see cref="ISearchSqlCompiler.CreatePlanFromOptionsAsync"/> path there is no query string, so
/// <see cref="Parameters"/> and <see cref="Implicit"/> are always empty; the plan-level traces still populate.
/// </summary>
public sealed record SearchCompilationDiagnostics
{
    /// <summary>Per-parameter outcomes from the options builder, stamped by Resolve and Lower.</summary>
    public IReadOnlyList<ParameterTrace> Parameters { get; init; } = [];

    /// <summary>Control values that took effect without the caller sending them.</summary>
    public IReadOnlyList<ImplicitParameter> Implicit { get; init; } = [];

    /// <summary>The plan's explain rows and per-CTE provenance. Populated at <see cref="SearchDiagnosticsLevel.Full"/>.</summary>
    public QueryPlanTrace? PlanTrace { get; init; }

    /// <summary>Which span of the emitted SQL each plan element produced. Populated at <see cref="SearchDiagnosticsLevel.Full"/>.</summary>
    public IReadOnlyList<SqlTextRange> SqlTextRanges { get; init; } = [];
}
