// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// Renders a <see cref="NumericRangePredicate"/> into the field-level <see cref="Expression"/> tree the
/// old-shape backends consume.
/// </summary>
/// <remarks>
/// Shared by both field-level builders — the one the parser uses and the rollback lever that mirrors it — so
/// that the two cannot disagree about a prefix even in principle. Unlike its date counterpart the bound-to-
/// field mapping is a parameter rather than a constant, because number and quantity store their ranges in
/// different field pairs while meaning exactly the same thing by a prefix.
/// </remarks>
internal static class NumericRangePredicateExpressionRenderer
{
    public static Expression Render(
        NumericRangePredicate predicate,
        FieldName lowField,
        FieldName highField,
        int? componentIndex)
    {
        switch (predicate)
        {
            case NumericRangePredicate.All all:
                return Expression.And(
                    Render(all.Left, lowField, highField, componentIndex),
                    Render(all.Right, lowField, highField, componentIndex));
            case NumericRangePredicate.Any any:
                return Expression.Or(
                    Render(any.Left, lowField, highField, componentIndex),
                    Render(any.Right, lowField, highField, componentIndex));
            case NumericRangePredicate.Compare compare:
                return new BinaryExpression(
                    compare.Operator,
                    compare.Bound == NumericRangeBound.Low ? lowField : highField,
                    componentIndex,
                    compare.Value);
            default:
                throw new NotSupportedException($"Unknown {nameof(NumericRangePredicate)} '{predicate?.GetType().Name}'.");
        }
    }
}
