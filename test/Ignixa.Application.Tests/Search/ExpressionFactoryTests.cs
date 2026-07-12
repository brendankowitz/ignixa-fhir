// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;

namespace Ignixa.Application.Tests.Search;

public class ExpressionFactoryTests
{
    [Fact]
    public void GivenFieldAndValue_WhenStartsAfter_ThenBuildsStartsAfterBinaryExpression()
    {
        var result = Expression.StartsAfter(FieldName.Number, componentIndex: null, 5.4m);

        result.BinaryOperator.ShouldBe(BinaryOperator.StartsAfter);
        result.FieldName.ShouldBe(FieldName.Number);
        result.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenFieldAndValue_WhenEndsBefore_ThenBuildsEndsBeforeBinaryExpression()
    {
        var result = Expression.EndsBefore(FieldName.Number, componentIndex: null, 5.4m);

        result.BinaryOperator.ShouldBe(BinaryOperator.EndsBefore);
        result.FieldName.ShouldBe(FieldName.Number);
        result.Value.ShouldBe(5.4m);
    }
}
