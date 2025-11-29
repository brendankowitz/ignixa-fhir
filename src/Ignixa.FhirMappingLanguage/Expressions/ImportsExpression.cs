/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents an imports declaration.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents an imports declaration.
/// Example: imports "http://example.org/fhir/StructureMap/Helpers"
/// </summary>
public class ImportsExpression : Expression
{
    public ImportsExpression(string url, ISourcePositionInfo? location = null) : base(location)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
    }

    public string Url { get; }

    public override string ToString() => $"imports {Url}";
}
