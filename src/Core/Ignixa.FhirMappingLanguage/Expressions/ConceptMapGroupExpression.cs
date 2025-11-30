/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a group of code mappings from source to target system.
/// Example: source "http://hl7.org/fhir/gender" target "http://snomed.info/sct" { ... }
/// </summary>
public class ConceptMapGroupExpression : Expression
{
    public ConceptMapGroupExpression(
        string? sourceSystem,
        string? targetSystem,
        IEnumerable<ConceptMapCodeMapExpression> codeMaps,
        ISourcePositionInfo? location = null) : base(location)
    {
        SourceSystem = sourceSystem;
        TargetSystem = targetSystem;
        CodeMaps = codeMaps?.ToList() ?? [];
    }

    public string? SourceSystem { get; }
    public string? TargetSystem { get; }
    public IReadOnlyList<ConceptMapCodeMapExpression> CodeMaps { get; }

    public override string ToString()
    {
        var source = SourceSystem ?? "?";
        var target = TargetSystem ?? "?";
        return $"\"{source}\" -> \"{target}\" {{ {CodeMaps.Count} mappings }}";
    }
}
