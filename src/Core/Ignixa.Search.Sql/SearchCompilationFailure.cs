using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql;

/// <summary>
/// A compilation failure as data. <see cref="ParameterCode"/> and <see cref="Span"/> are populated even at
/// <see cref="SearchDiagnosticsLevel.None"/> — the lowering dispatchers attach them to the exception, so
/// attribution costs nothing.
/// </summary>
public sealed record SearchCompilationFailure(
    CompilationStage Stage,
    string Message,
    string? ParameterCode,
    SourceSpan? Span,
    Exception? Exception)
{
    /// <summary>Whatever diagnostics had been gathered when the failure occurred; null at <see cref="SearchDiagnosticsLevel.None"/>.</summary>
    public SearchCompilationDiagnostics? Diagnostics { get; init; }
}
