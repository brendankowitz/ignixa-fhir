/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents a qualified identifier (e.g., context.property).
/// Example: src.name, bundle.entry
/// </summary>
public class QualifiedIdentifierExpression : Expression
{
    public QualifiedIdentifierExpression(
        Expression context,
        string property,
        ISourcePositionInfo? location = null) : base(location)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(property);

        Context = context;
        Property = property;
    }

    public Expression Context { get; }
    public string Property { get; }

    public override string ToString() => $"{Context}.{Property}";
}
