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

    /// <summary>
    /// Control values the server resolved without the caller sending them. Advisory — see
    /// <see cref="ImplicitParameter"/>; nothing here changes the emitted SQL.
    /// </summary>
    public IReadOnlyList<ImplicitParameter> Implicit { get; init; } = [];

    /// <summary>The plan's explain rows and per-CTE provenance. Populated at <see cref="SearchDiagnosticsLevel.Full"/>.</summary>
    public QueryPlanTrace? PlanTrace { get; init; }

    /// <summary>
    /// Why <see cref="PlanTrace"/> is absent at <see cref="SearchDiagnosticsLevel.Full"/>, or null when it was
    /// built. Building the trace renders the plan, so it can refuse for two quite different reasons: the plan
    /// genuinely cannot be emitted, in which case the same refusal resurfaces from the compile itself; or the
    /// explainer disagrees with the emitters, which does not affect the SQL at all. Neither is allowed to fail
    /// a compile that otherwise succeeded — but neither may be silent either, which is what this field is for.
    /// </summary>
    public SearchCompilationFailure? PlanTraceFailure { get; init; }

    /// <summary>Which span of the emitted SQL each plan element produced. Populated at <see cref="SearchDiagnosticsLevel.Full"/>.</summary>
    public IReadOnlyList<SqlTextRange> SqlTextRanges { get; init; } = [];
}
