// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// Renders a <see cref="DateRangePredicate"/> into the field-level <see cref="Expression"/> tree the
/// old-shape backends consume.
/// </summary>
/// <remarks>
/// Shared by both field-level builders — the one the parser uses and the rollback lever that mirrors it —
/// so that the two cannot disagree about a prefix even in principle. What they may still differ on is the
/// tree they wrap around this; the prefix semantics itself now has one source.
/// </remarks>
internal static class DateRangePredicateExpressionRenderer
{
    public static Expression Render(DateRangePredicate predicate, int? componentIndex)
    {
        switch (predicate)
        {
            case DateRangePredicate.All all:
                return Expression.And(Render(all.Left, componentIndex), Render(all.Right, componentIndex));
            case DateRangePredicate.Any any:
                return Expression.Or(Render(any.Left, componentIndex), Render(any.Right, componentIndex));
            case DateRangePredicate.Compare compare:
                return new BinaryExpression(compare.Operator, ToFieldName(compare.Bound), componentIndex, compare.Value);
            default:
                throw new NotSupportedException($"Unknown {nameof(DateRangePredicate)} '{predicate?.GetType().Name}'.");
        }
    }

    private static FieldName ToFieldName(DateRangeBound bound)
        => bound == DateRangeBound.Start ? FieldName.DateTimeStart : FieldName.DateTimeEnd;
}
