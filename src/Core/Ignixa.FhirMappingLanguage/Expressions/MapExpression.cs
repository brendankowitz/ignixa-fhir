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

    public override string ToString()
    {
        List<string> parts =
        [
            $"map \"{Url}\" = \"{Identifier}\""
        ];

        // Add uses declarations
        if (Uses.Count > 0)
        {
            foreach (var use in Uses)
            {
                parts.Add($"  {use}");
            }
        }

        // Add imports
        if (Imports.Count > 0)
        {
            foreach (var import in Imports)
            {
                parts.Add($"  {import}");
            }
        }

        // Add conceptmaps summary
        if (ConceptMaps.Count > 0)
        {
            parts.Add($"  // {ConceptMaps.Count} conceptmap(s)");
        }

        // Add constants summary
        if (Constants.Count > 0)
        {
            parts.Add($"  // {Constants.Count} constant(s)");
        }

        // Add groups with preview
        if (Groups.Count > 0)
        {
            parts.Add("");
            foreach (var group in Groups)
            {
                parts.Add($"  {group}");
            }
        }

        return string.Join(Environment.NewLine, parts);
    }
}
