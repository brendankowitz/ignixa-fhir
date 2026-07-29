using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql;

/// <summary>
/// What a compile recorded about its own work. Present only when
/// <see cref="SearchPlanOptions.DiagnosticsLevel"/> is above <see cref="SearchDiagnosticsLevel.None"/>.
/// </summary>
/// <remarks>
/// <c>CreatePlanFromOptionsAsync</c> never runs the options builder, so on that path
/// <see cref="Parameters"/> and <see cref="Implicit"/> are always empty regardless of level: there is no
/// query string to attribute outcomes to, and no way to tell an explicit <c>_count</c> from a server
/// default. <see cref="Plan"/> and <see cref="SqlTextRanges"/> are populated normally.
/// </remarks>
public sealed record SearchCompilationDiagnostics
{
    /// <summary>Per-parameter outcomes from the options builder, stamped by Resolve and Lower.</summary>
    public IReadOnlyList<ParameterTrace> Parameters { get; init; } = [];

    /// <summary>Control values that took effect without the caller sending them.</summary>
    public IReadOnlyList<ImplicitParameter> Implicit { get; init; } = [];

    /// <summary>The plan's explain rows and per-CTE provenance. Populated at <see cref="SearchDiagnosticsLevel.Full"/>.</summary>
    public QueryPlanTrace? Plan { get; init; }

    /// <summary>Which span of the emitted SQL each plan element produced. Populated at <see cref="SearchDiagnosticsLevel.Full"/>.</summary>
    public IReadOnlyList<SqlTextRange> SqlTextRanges { get; init; } = [];
}
