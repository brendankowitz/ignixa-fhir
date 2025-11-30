/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents a constant value in a FhirPath expression.
/// Examples: 42, 3.14, 'hello', true, @2024-01-15
/// </summary>
public class ConstantExpression : Expression
{
    public ConstantExpression(object value, ISourcePositionInfo? location = null) : base(location)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public object Value { get; }

    public override string ToString() => $"Constant({Value})";
}
