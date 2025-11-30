/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents an indexed access expression.
/// Example: src.name[0], src.identifier[1]
/// </summary>
public class IndexExpression : Expression
{
    public IndexExpression(
        Expression context,
        int index,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
        Index = index;
    }

    public Expression Context { get; }
    public int Index { get; }

    public override string ToString() => $"{Context}[{Index}]";
}
