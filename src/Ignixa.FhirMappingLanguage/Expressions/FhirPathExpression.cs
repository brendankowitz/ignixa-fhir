/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents an embedded FHIRPath expression.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents an embedded FHIRPath expression (surrounded by parentheses).
/// Example: (name.given.first())
/// </summary>
public class FhirPathExpression : Expression
{
    public FhirPathExpression(string expression, ISourcePositionInfo? location = null) : base(location)
    {
        PathExpression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    public string PathExpression { get; }

    public override string ToString() => PathExpression;
}
