/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a transformation rule.
/// Example: src.name -> tgt.name
/// </summary>
public class RuleExpression : Expression
{
    public RuleExpression(
        string? name,
        IEnumerable<SourceExpression> sources,
        IEnumerable<TargetExpression> targets,
        Expression? dependent,
        ISourcePositionInfo? location = null) : base(location)
    {
        Name = name;
        Sources = sources?.ToList() ?? [];
        Targets = targets?.ToList() ?? [];
        Dependent = dependent;
    }

    public string? Name { get; }
    public IReadOnlyList<SourceExpression> Sources { get; }
    public IReadOnlyList<TargetExpression> Targets { get; }
    public Expression? Dependent { get; }

    public override string ToString()
    {
        var name = Name ?? "anonymous";
        var sources = string.Join(", ", Sources.Select(s => s.ToString()));
        var targets = string.Join(", ", Targets.Select(t => t.ToString()));

        var rule = $"{name}: {sources} -> {targets}";

        if (Dependent is not null)
        {
            rule += $" then {Dependent}";
        }

        return rule;
    }
}
