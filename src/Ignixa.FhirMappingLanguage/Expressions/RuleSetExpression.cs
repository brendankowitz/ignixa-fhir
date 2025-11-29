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

    public override string ToString()
    {
        if (Rules.Count == 0)
            return "{ }";

        if (Rules.Count == 1)
            return $"{{ {Rules[0]} }}";

        // Show first 2 rules with ellipsis if more
        var preview = string.Join("; ", Rules.Take(2).Select(r => r.ToString()));
        var suffix = Rules.Count > 2 ? $"; ... ({Rules.Count - 2} more)" : "";
        return $"{{ {preview}{suffix} }}";
    }
}
