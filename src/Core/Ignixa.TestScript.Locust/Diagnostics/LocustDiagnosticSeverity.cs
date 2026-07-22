namespace Ignixa.TestScript.Locust.Diagnostics;

/// <summary>
/// Severity of a <see cref="LocustDiagnostic"/> reported while analyzing or compiling a
/// TestScript definition for Locust support. <see cref="Info"/> is reserved for metric mapping
/// diagnostics introduced by a later task; the support analyzer only emits
/// <see cref="Warning"/> and <see cref="Error"/>.
/// </summary>
public enum LocustDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
