/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a prefix declaration within a ConceptMap.
/// Example: prefix s = "http://snomed.info/sct"
/// </summary>
public class ConceptMapPrefixExpression : Expression
{
    public ConceptMapPrefixExpression(
        string prefixName,
        string url,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(prefixName);
        ArgumentNullException.ThrowIfNull(url);

        PrefixName = prefixName;
        Url = url;
    }

    public string PrefixName { get; }
    public string Url { get; }

    public override string ToString() => $"prefix {PrefixName} = \"{Url}\"";
}
