/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a source element in a transformation rule.
/// Example: src.name as vn where name.exists()
/// </summary>
public class SourceExpression : Expression
{
    public SourceExpression(
        Expression context,
        string? variable,
        string? type,
        Expression? condition,
        Expression? check,
        Expression? log,
        Expression? defaultValue = null,
        Cardinality? cardinality = null,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
        Variable = variable;
        Type = type;
        Condition = condition;
        Check = check;
        Log = log;
        Default = defaultValue;
        Cardinality = cardinality;
    }

    public Expression Context { get; }
    public string? Variable { get; }
    public string? Type { get; }
    public Expression? Condition { get; }
    public Expression? Check { get; }
    public Expression? Log { get; }
    public Expression? Default { get; }
    public Cardinality? Cardinality { get; }

    public override string ToString() => $"Source({Context})";
}
