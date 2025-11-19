/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents an axis reference in a FhirPath expression.
/// Examples: $this, $index, $total
/// </summary>
public class AxisExpression : Expression
{
    public AxisExpression(string axisName, ISourcePositionInfo? location = null) : base(location)
    {
        AxisName = axisName ?? throw new ArgumentNullException(nameof(axisName));
    }

    public string AxisName { get; }

    public override string ToString() => $"Axis(${AxisName})";

    // Common axis instances
    public static readonly AxisExpression This = new("this");
    public static readonly AxisExpression Index = new("index");
    public static readonly AxisExpression Total = new("total");
    public static readonly AxisExpression That = new("that");
}
