// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Shouldly;

namespace Ignixa.Application.Tests.Infrastructure;

public class LogSanitizationExtensionsTests
{
    [Fact]
    public void GivenNull_WhenSanitizingForLog_ThenReturnsNull()
    {
        string value = null;

        var result = value.SanitizeForLog();

        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Patient/p1")]
    [InlineData("Patient/\u00E9")]
    public void GivenNoUnsafeCharacters_WhenSanitizingForLog_ThenPreservesContent(string value)
    {
        var result = value.SanitizeForLog();

        result.ShouldBe(value);
    }

    [Theory]
    [InlineData("\r", " ")]
    [InlineData("\n", " ")]
    [InlineData("\r\n", "  ")]
    [InlineData("a\rb\nc", "a b c")]
    [InlineData("\r\nPatient/p1\r\n", "  Patient/p1  ")]
    [InlineData("a\r\r\n\nb", "a    b")]
    [InlineData("\tPatient/p1\t", " Patient/p1 ")]
    [InlineData("a\u2028b\u2029c", "a b c")]
    [InlineData("a\u202Eb\u2066c", "a b c")]
    public void GivenLineBreaksControlsOrBidiFormatting_WhenSanitizingForLog_ThenReplacesEachWithOneSpace(
        string value,
        string expected)
    {
        var result = value.SanitizeForLog();

        result.Length.ShouldBe(value.Length);
        result.ShouldBe(expected);
    }

    [Fact]
    public void GivenTextLongerThanTheLimit_WhenSanitizingForLogWithLimit_ThenTruncatesAndNamesTheDroppedCount()
    {
        var result = new string('a', 50).SanitizeForLog(10);

        result.ShouldBe("aaaaaaaaaa...[40 more characters]");
    }

    [Fact]
    public void GivenLineBreaksWithinTheLimit_WhenSanitizingForLogWithLimit_ThenReplacesThemWithoutTruncating()
    {
        var result = "a\r\nb".SanitizeForLog(10);

        result.ShouldBe("a  b");
    }

    [Fact]
    public void GivenLongExpression_WhenSanitizingExpressionForLog_ThenIsBoundedByTheExpressionLimit()
    {
        var result = new string('a', 5000).SanitizeExpressionForLog();

        result!.Length.ShouldBeLessThan(LogSanitizationExtensions.MaxExpressionLogLength + 40);
    }
}
