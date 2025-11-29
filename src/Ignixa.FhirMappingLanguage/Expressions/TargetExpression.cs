/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a target element in a transformation rule.
/// Example: tgt.name = create('HumanName') or tgt.type = 'collection'
/// </summary>
public class TargetExpression : Expression
{
    public TargetExpression(
        Expression? context,
        string? variable,
        Expression? transform,
        ListMode? listMode,
        ISourcePositionInfo? location = null) : base(location)
    {
        Context = context;
        Variable = variable;
        Transform = transform;
        ListMode = listMode;
    }

    public Expression? Context { get; }
    public string? Variable { get; }
    public Expression? Transform { get; }
    public ListMode? ListMode { get; }

    public override string ToString() => $"Target({Context})";
}
