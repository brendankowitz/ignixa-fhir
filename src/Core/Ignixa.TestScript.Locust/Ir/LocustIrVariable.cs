namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// A compiled TestScript variable definition, describing how its runtime value is resolved.
/// </summary>
/// <param name="Name">The variable name referenced by fixture and action templates.</param>
/// <param name="DefaultValue">The default value used when no extraction produces a value.</param>
/// <param name="SourceId">The identifier of the action whose response the value is extracted from.</param>
/// <param name="ExtractionKind">The mechanism used to extract the value.</param>
/// <param name="Selector">The header name, path, or FHIRPath expression used to extract the value.</param>
public sealed record LocustIrVariable(
    string Name,
    string? DefaultValue,
    string? SourceId,
    LocustIrVariableExtractionKind ExtractionKind,
    string? Selector);
