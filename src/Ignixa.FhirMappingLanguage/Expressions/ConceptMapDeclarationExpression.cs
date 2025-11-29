/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents an inline ConceptMap declaration.
/// Example: conceptmap "#genderMap" { prefix s = "http://snomed.info/sct" ... }
/// </summary>
public class ConceptMapDeclarationExpression : Expression
{
    public ConceptMapDeclarationExpression(
        string identifier,
        IEnumerable<ConceptMapPrefixExpression> prefixes,
        IEnumerable<ConceptMapGroupExpression> groups,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        Identifier = identifier;
        Prefixes = prefixes?.ToList() ?? [];
        Groups = groups?.ToList() ?? [];
    }

    /// <summary>
    /// The conceptmap identifier (e.g., "#genderMap").
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// Prefix declarations for code systems.
    /// </summary>
    public IReadOnlyList<ConceptMapPrefixExpression> Prefixes { get; }

    /// <summary>
    /// Groups of code mappings (source system -> target system).
    /// </summary>
    public IReadOnlyList<ConceptMapGroupExpression> Groups { get; }

    public override string ToString()
    {
        var prefixCount = Prefixes.Count > 0 ? $" {Prefixes.Count} prefixes" : "";
        var groupCount = Groups.Count > 0 ? $" {Groups.Count} groups" : "";
        return $"conceptmap {Identifier}{{{prefixCount}{groupCount} }}";
    }
}
