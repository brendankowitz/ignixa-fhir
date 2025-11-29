/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a parameter in a group definition.
/// Example: source src : Patient
/// </summary>
public class ParameterExpression : Expression
{
    public ParameterExpression(
        ParameterMode mode,
        string name,
        string? type,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(name);

        Mode = mode;
        Name = name;
        Type = type;
    }

    public ParameterMode Mode { get; }
    public string Name { get; }
    public string? Type { get; }

    public override string ToString() => $"Parameter({Mode} {Name}: {Type})";
}
