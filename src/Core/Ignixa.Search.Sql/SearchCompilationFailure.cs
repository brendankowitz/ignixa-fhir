using System.Text;
using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql;

/// <summary>
/// A compilation failure as data — what the <c>Try</c> entry points return instead of throwing.
/// <see cref="ParameterCode"/> and <see cref="Span"/> are attributed by the lowering dispatchers regardless
/// of <see cref="SearchDiagnosticsLevel"/>, but stay best-effort: a guard outside a dispatcher names no
/// parameter, and a <see cref="CompilationStage.Resolve"/> failure carries no span.
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

    /// <summary>
    /// Prints <see cref="Exception"/> as its type name only; the generated implementation would embed a full
    /// stack trace via <see cref="Exception.ToString"/> in a one-line record rendering. Rendering only —
    /// value equality is left as reference equality on the exception member.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Stage = ");
        builder.Append(Stage);
        builder.Append(", Message = ");
        builder.Append(Message);
        builder.Append(", ParameterCode = ");
        builder.Append(ParameterCode);
        builder.Append(", Span = ");
        builder.Append(Span);
        builder.Append(", Exception = ");
        if (Exception is not null)
        {
            builder.Append(Exception.GetType());
        }

        builder.Append(", Diagnostics = ");
        builder.Append(Diagnostics);
        return true;
    }
}
