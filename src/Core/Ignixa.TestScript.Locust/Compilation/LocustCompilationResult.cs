using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;

namespace Ignixa.TestScript.Locust.Compilation;

/// <summary>
/// The outcome of compiling a TestScript definition into the Locust intermediate representation.
/// </summary>
/// <param name="Document">
/// The compiled intermediate representation document, or <see langword="null"/> when compilation
/// failed with at least one <see cref="LocustDiagnosticSeverity.Error"/> diagnostic.
/// </param>
/// <param name="Diagnostics">
/// The full set of diagnostics raised while analyzing and compiling the definition, including any
/// support-analyzer warnings/errors and, on success, informational metric mapping diagnostics.
/// </param>
public sealed record LocustCompilationResult(
    LocustIrDocument? Document,
    IReadOnlyList<LocustDiagnostic> Diagnostics)
{
    /// <summary>
    /// Gets a value indicating whether <see cref="Diagnostics"/> contains at least one
    /// <see cref="LocustDiagnosticSeverity.Error"/> entry.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == LocustDiagnosticSeverity.Error);
}
