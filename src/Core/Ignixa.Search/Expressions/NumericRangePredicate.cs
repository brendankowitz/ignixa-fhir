// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// A backend-neutral description of the comparison a number or quantity search prefix performs against one
/// indexed [Low, High] range.
/// </summary>
/// <remarks>
/// The numeric counterpart to <see cref="DateRangePredicate"/>, and the interchange format between
/// <see cref="NumericRangeComparisonSemantics"/>, which decides what a prefix means, and the backends, which
/// decide how to say it. It carries resolved <see cref="decimal"/> bounds rather than a reference to the
/// search value, so every backend compares against the same numbers rather than recomputing them — in
/// particular the precision modifier and the <c>ap</c> tolerance are applied once, here, not per backend.
/// </remarks>
public abstract record NumericRangePredicate
{
    /// <summary>
    /// Compares one bound of the indexed range against a fixed value.
    /// </summary>
    /// <param name="Bound">The end of the indexed range being compared.</param>
    /// <param name="Operator">The comparison to apply.</param>
    /// <param name="Value">The value to compare against.</param>
    public sealed record Compare(NumericRangeBound Bound, BinaryOperator Operator, decimal Value) : NumericRangePredicate;

    /// <summary>
    /// Requires both operands to hold.
    /// </summary>
    /// <param name="Left">The first operand.</param>
    /// <param name="Right">The second operand.</param>
    public sealed record All(NumericRangePredicate Left, NumericRangePredicate Right) : NumericRangePredicate;

    /// <summary>
    /// Requires either operand to hold.
    /// </summary>
    /// <param name="Left">The first operand.</param>
    /// <param name="Right">The second operand.</param>
    public sealed record Any(NumericRangePredicate Left, NumericRangePredicate Right) : NumericRangePredicate;
}
