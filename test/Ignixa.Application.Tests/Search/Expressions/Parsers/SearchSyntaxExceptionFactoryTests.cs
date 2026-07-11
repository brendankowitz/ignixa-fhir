// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchSyntaxExceptionFactoryTests
{
    [Theory]
    [InlineData("patient..name", 8, 1, 9)]
    [InlineData("first\nsecond", 6, 2, 1)]
    [InlineData("first\rsecond", 6, 2, 1)]
    [InlineData("first\r\nsecond", 7, 2, 1)]
    [InlineData(@"value\", 5, 1, 6)]
    [InlineData("abc", -1, 1, 1)]
    [InlineData("abc", 99, 1, 4)]
    public void GivenSourceOffset_WhenCreatingException_ThenReportsOneBasedPosition(
        string source,
        int offset,
        int expectedLine,
        int expectedColumn)
    {
        var exception = SearchSyntaxExceptionFactory.Create(
            source,
            offset,
            "search value",
            "expected valid syntax");

        exception.Message.ShouldBe(
            $"Malformed search value at line {expectedLine}, column {expectedColumn}: expected valid syntax");
    }
}
