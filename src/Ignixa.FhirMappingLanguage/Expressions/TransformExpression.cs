/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a transform function call.
/// Example: create('HumanName'), translate(src, '#conceptMap', 'code')
/// </summary>
public class TransformExpression : Expression
{
    public TransformExpression(
        string functionName,
        IEnumerable<Expression> arguments,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(functionName);

        FunctionName = functionName;
        Arguments = arguments?.ToList() ?? [];
    }

    public string FunctionName { get; }
    public IReadOnlyList<Expression> Arguments { get; }

    public override string ToString() => $"Transform({FunctionName})";
}
