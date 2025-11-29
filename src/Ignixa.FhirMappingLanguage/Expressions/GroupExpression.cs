/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents a transformation group.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a transformation group.
/// Example: group PatientToBundle(source src : Patient, target bundle : Bundle)
/// </summary>
public class GroupExpression : Expression
{
    public GroupExpression(
        string name,
        IEnumerable<ParameterExpression> parameters,
        string? extends,
        IEnumerable<RuleExpression> rules,
        ISourcePositionInfo? location = null) : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Parameters = parameters?.ToList() ?? [];
        Extends = extends;
        Rules = rules?.ToList() ?? [];
    }

    public string Name { get; }
    public IReadOnlyList<ParameterExpression> Parameters { get; }
    public string? Extends { get; }
    public IReadOnlyList<RuleExpression> Rules { get; }

    public override string ToString()
    {
        var parameters = string.Join(", ", Parameters.Select(p => p.ToString()));
        var result = $"group {Name}({parameters})";

        if (Extends is not null)
        {
            result += $" extends {Extends}";
        }

        if (Rules.Count > 0)
        {
            result += $" {{ {Rules.Count} rules }}";
        }

        return result;
    }
}
