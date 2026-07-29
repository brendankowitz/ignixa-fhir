using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql;

/// <summary>
/// A compilation failure as data — what the <c>Try</c> entry points return in place of the exception the
/// others throw.
/// </summary>
/// <remarks>
/// Attribution does not depend on <see cref="SearchDiagnosticsLevel"/>: the lowering dispatchers attach the
/// failing parameter, and its span when it has one, to the in-flight exception, so <see cref="ParameterCode"/>
/// and <see cref="Span"/> survive even at <see cref="SearchDiagnosticsLevel.None"/>. Both stay best-effort
/// though — a guard that throws from outside a dispatcher names no parameter, and a
/// <see cref="CompilationStage.Resolve"/> failure carries no span at all and names a parameter only when
/// exactly one went unresolved.
/// </remarks>
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
