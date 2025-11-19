/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents a parenthesized expression in a FhirPath expression.
/// Examples: (1 + 2), (name.exists())
/// </summary>
public class ParenthesizedExpression : Expression
{
    public ParenthesizedExpression(Expression innerExpression, ISourcePositionInfo? location = null)
        : base(location)
    {
        InnerExpression = innerExpression ?? throw new ArgumentNullException(nameof(innerExpression));
    }

    public Expression InnerExpression { get; }

    public override string ToString() => $"({InnerExpression})";
}
