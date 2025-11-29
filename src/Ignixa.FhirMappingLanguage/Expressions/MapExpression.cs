/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents the top-level map structure.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents the top-level map structure.
/// Example: map "http://example.org/fhir/StructureMap/Example" = "ExampleMap"
/// </summary>
public class MapExpression : Expression
{
    public MapExpression(
        string url,
        string identifier,
        IEnumerable<UsesExpression> uses,
        IEnumerable<ImportsExpression> imports,
        IEnumerable<GroupExpression> groups,
        IEnumerable<ConceptMapDeclarationExpression>? conceptMaps = null,
        IEnumerable<ConstantDeclarationExpression>? constants = null,
        ISourcePositionInfo? location = null) : base(location)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        Uses = uses?.ToList() ?? [];
        Imports = imports?.ToList() ?? [];
        Groups = groups?.ToList() ?? [];
        ConceptMaps = conceptMaps?.ToList() ?? [];
        Constants = constants?.ToList() ?? [];
    }

    public string Url { get; }
    public string Identifier { get; }
    public IReadOnlyList<UsesExpression> Uses { get; }
    public IReadOnlyList<ImportsExpression> Imports { get; }
    public IReadOnlyList<GroupExpression> Groups { get; }

    /// <summary>
    /// Inline ConceptMap declarations.
    /// </summary>
    public IReadOnlyList<ConceptMapDeclarationExpression> ConceptMaps { get; }

    /// <summary>
    /// Constant declarations.
    /// </summary>
    public IReadOnlyList<ConstantDeclarationExpression> Constants { get; }

    public override string ToString() => $"Map({Url} = {Identifier})";
}
