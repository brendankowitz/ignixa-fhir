// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// Names one end of an indexed numeric range, so a <see cref="NumericRangePredicate"/> can say which bound
/// it constrains without knowing the column or field that stores it.
/// </summary>
public enum NumericRangeBound
{
    /// <summary>
    /// The low end of the indexed range (<c>LowValue</c>, or the <c>NumberLow</c>/<c>QuantityLow</c> field).
    /// </summary>
    Low,

    /// <summary>
    /// The high end of the indexed range (<c>HighValue</c>, or the <c>NumberHigh</c>/<c>QuantityHigh</c> field).
    /// </summary>
    High,
}
