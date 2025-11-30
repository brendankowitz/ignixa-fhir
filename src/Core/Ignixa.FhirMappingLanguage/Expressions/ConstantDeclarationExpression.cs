/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a constant declaration.
/// Example: constant MY_VALUE = 'some value'
/// </summary>
public class ConstantDeclarationExpression : Expression
{
    public ConstantDeclarationExpression(
        string name,
        Expression value,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        Name = name;
        Value = value;
    }

    public string Name { get; }
    public Expression Value { get; }

    public override string ToString() => $"constant {Name} = {Value}";
}
