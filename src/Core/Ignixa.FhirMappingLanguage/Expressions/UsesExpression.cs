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

    public override string ToString()
    {
        var mode = Mode switch
        {
            ModelMode.Source => "source",
            ModelMode.Queried => "queried",
            ModelMode.Target => "target",
            ModelMode.Produced => "produced",
            _ => "source"
        };

        var alias = Alias is not null ? $" alias {Alias}" : "";
        return $"uses {Url} {mode}{alias}";
    }
}
