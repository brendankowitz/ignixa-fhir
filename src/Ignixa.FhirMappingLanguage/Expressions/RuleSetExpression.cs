/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a set of nested rules in a dependent clause.
/// Example: then { rule1; rule2; }
/// </summary>
public class RuleSetExpression : Expression
{
    public RuleSetExpression(
        IEnumerable<RuleExpression> rules,
        ISourcePositionInfo? location = null) : base(location)
    {
        Rules = rules?.ToList() ?? [];
    }

    public IReadOnlyList<RuleExpression> Rules { get; }

    public override string ToString() => $"{{ {Rules.Count} rules }}";
}
