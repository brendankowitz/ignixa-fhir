// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// A backend-neutral description of the comparison a date search prefix performs against one indexed
/// [Start, End] range.
/// </summary>
/// <remarks>
/// This is the interchange format between <see cref="DateRangeComparisonSemantics"/>, which decides what a
/// prefix means, and the backends, which decide how to say it. It carries resolved
/// <see cref="DateTimeOffset"/> bounds rather than a reference to the search value, so every backend
/// compares against the same instants rather than recomputing them.
/// </remarks>
public abstract record DateRangePredicate
{
    /// <summary>
    /// Compares one bound of the indexed range against a fixed instant.
    /// </summary>
    /// <param name="Bound">The end of the indexed range being compared.</param>
    /// <param name="Operator">The comparison to apply.</param>
    /// <param name="Value">The instant to compare against.</param>
    public sealed record Compare(DateRangeBound Bound, BinaryOperator Operator, DateTimeOffset Value) : DateRangePredicate;

    /// <summary>
    /// Requires both operands to hold.
    /// </summary>
    /// <param name="Left">The first operand.</param>
    /// <param name="Right">The second operand.</param>
    public sealed record All(DateRangePredicate Left, DateRangePredicate Right) : DateRangePredicate;

    /// <summary>
    /// Requires either operand to hold.
    /// </summary>
    /// <param name="Left">The first operand.</param>
    /// <param name="Right">The second operand.</param>
    public sealed record Any(DateRangePredicate Left, DateRangePredicate Right) : DateRangePredicate;
}
