using System.Text;
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

    /// <summary>
    /// Prints <see cref="Exception"/> as its type name. The generated implementation would call
    /// <see cref="Exception.ToString"/>, embedding a full multi-line stack trace in what reads as a
    /// one-line record rendering — the message and the attributed <see cref="ParameterCode"/> are what a
    /// log line wants, and the exception itself is still on the property for a caller that needs it.
    /// </summary>
    /// <remarks>
    /// Only the rendering changes. Value equality is left untouched deliberately: two failures carrying
    /// different exception instances are different occurrences, so reference equality on that member is the
    /// honest answer, and widening it here would be a larger decision than this rendering fix.
    /// </remarks>
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
