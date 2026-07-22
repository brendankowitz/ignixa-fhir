namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// Describes how a <see cref="LocustIrVariable"/> value is extracted from a prior operation's response.
/// </summary>
public enum LocustIrVariableExtractionKind
{
    None,
    Header,
    Path,
    FhirPath,
}
