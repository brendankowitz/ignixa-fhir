/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a group invocation in a dependent clause.
/// Example: then GroupName(src, tgt)
/// </summary>
public class GroupInvocationExpression : Expression
{
    public GroupInvocationExpression(
        string groupName,
        IEnumerable<Expression> arguments,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(groupName);

        GroupName = groupName;
        Arguments = arguments?.ToList() ?? [];
    }

    public string GroupName { get; }
    public IReadOnlyList<Expression> Arguments { get; }

    public override string ToString() => $"{GroupName}({string.Join(", ", Arguments)})";
}
