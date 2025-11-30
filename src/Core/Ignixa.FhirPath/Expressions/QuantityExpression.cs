/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents a quantity literal with a value and UCUM unit in a FhirPath expression.
/// Examples: 5 'mg', 37.5 'Cel', 100 '[lb_av]'
/// </summary>
public class QuantityExpression : Expression
{
    public QuantityExpression(decimal value, string unit, ISourcePositionInfo? location = null) : base(location)
    {
        Value = value;
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
    }

    /// <summary>Numeric value of the quantity</summary>
    public decimal Value { get; }

    /// <summary>UCUM unit code</summary>
    public string Unit { get; }

    public override string ToString() => $"{Value} '{Unit}'";
}
