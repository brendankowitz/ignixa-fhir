/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents an identifier in a mapping expression.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents an identifier in a mapping expression.
/// Examples: patient, name, Bundle
/// </summary>
public class IdentifierExpression : Expression
{
    public IdentifierExpression(string name, ISourcePositionInfo? location = null) : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public override string ToString() => Name;
}
