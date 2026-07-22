namespace Ignixa.TestScript.Locust.Diagnostics;

/// <summary>
/// A single reportable finding produced while analyzing or compiling a TestScript definition
/// for Locust support.
/// </summary>
/// <param name="Code">Stable diagnostic code, e.g. <c>LOCUST001</c>.</param>
/// <param name="Severity">Severity of the finding.</param>
/// <param name="Source">
/// Canonical location the finding was raised from, e.g. <c>{source}:test:{test.Name}:action:{index}</c>.
/// </param>
/// <param name="Message">Human-readable description of the finding.</param>
public sealed record LocustDiagnostic(
    string Code,
    LocustDiagnosticSeverity Severity,
    string Source,
    string Message);
