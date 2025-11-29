/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents a uses declaration.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a uses declaration.
/// Example: uses "http://hl7.org/fhir/StructureDefinition/Patient" alias Patient as source
/// </summary>
public class UsesExpression : Expression
{
    public UsesExpression(
        string url,
        string? alias,
        ModelMode mode,
        ISourcePositionInfo? location = null) : base(location)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Alias = alias;
        Mode = mode;
    }

    public string Url { get; }
    public string? Alias { get; }
    public ModelMode Mode { get; }

    public override string ToString() => $"Uses({Url} as {Mode})";
}
