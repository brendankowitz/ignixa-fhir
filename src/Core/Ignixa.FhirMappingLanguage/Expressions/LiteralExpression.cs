/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents a literal value in a mapping expression.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a literal value in a mapping expression.
/// Examples: 'hello', 42, 3.14, true
/// </summary>
public class LiteralExpression : Expression
{
    public LiteralExpression(object value, ISourcePositionInfo? location = null) : base(location)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public object Value { get; }

    public override string ToString()
    {
        return Value switch
        {
            string s => $"'{s}'",
            bool b => b ? "true" : "false",
            null => "null",
            _ => Value.ToString() ?? ""
        };
    }
}
