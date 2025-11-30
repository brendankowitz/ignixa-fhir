/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents a binary operation in a FhirPath expression.
/// Examples: age > 18, name = 'John', 1 + 2
/// </summary>
public class BinaryExpression : FunctionCallExpression
{
    public BinaryExpression(string op, Expression left, Expression right, ISourcePositionInfo? location = null)
        : base(AxisExpression.That, $"binary.{op}", new[] { left, right }, location)
    {
        Operator = op;
    }

    public string Operator { get; }
    public Expression Left => Arguments[0];
    public Expression Right => Arguments[1];

    public override string ToString() => $"({Left} {Operator} {Right})";
}
