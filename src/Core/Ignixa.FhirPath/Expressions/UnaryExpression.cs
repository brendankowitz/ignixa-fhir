/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents a unary operation in a FhirPath expression.
/// Examples: -5, +10
/// </summary>
public class UnaryExpression : FunctionCallExpression
{
    public UnaryExpression(string op, Expression operand, ISourcePositionInfo? location = null)
        : base(AxisExpression.That, $"unary.{op}", new[] { operand }, location)
    {
        Operator = op;
    }

    public string Operator { get; }
    public Expression Operand => Arguments[0];

    public override string ToString() => $"({Operator}{Operand})";
}
