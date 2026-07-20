// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// The SQL-only date-index optimization: on a containment-shaped date predicate it injects a redundant
/// DateTimeStart bound to constrain the (DateTimeStart, DateTimeEnd) index range scan; overlap shapes are
/// left untouched. Guards that the rewrite fires on the lowered field-level shape — it silently no-op'd
/// when it ran on the pre-lowering typed IR, and now runs in the SQL backend after LowerToLegacy.
/// </summary>
public class DateTimeEqualityRewriterTests
{
    private static readonly DateTimeOffset Start = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2000, 12, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GivenAContainmentShape_WhenRewritten_ThenARedundantDateTimeStartBoundIsInjected()
    {
        // DateTimeStart >= x AND DateTimeEnd <= y  (the shape :ap lowers to)
        Expression containment = Expression.And(
            Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, Start),
            Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, End));

        var result = containment.AcceptVisitor(DateTimeEqualityRewriter.Instance, null);

        result.ShouldBeOfType<MultiaryExpression>().Expressions.Count.ShouldBe(3);
    }

    [Fact]
    public void GivenAnOverlapShape_WhenRewritten_ThenItIsLeftUnchanged()
    {
        // DateTimeStart <= y AND DateTimeEnd >= x  (the shape date equality lowers to)
        Expression overlap = Expression.And(
            Expression.LessThanOrEqual(FieldName.DateTimeStart, null, End),
            Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, Start));

        var result = overlap.AcceptVisitor(DateTimeEqualityRewriter.Instance, null);

        result.ShouldBeOfType<MultiaryExpression>().Expressions.Count.ShouldBe(2);
    }
}
