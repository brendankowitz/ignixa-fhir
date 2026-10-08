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
    [InlineData(" \tPatient/p1\t ")]
    [InlineData("Patient/\u00E9")]
    public void GivenNoLineBreaks_WhenSanitizingForLog_ThenPreservesContent(string value)
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
    public void GivenLineBreaks_WhenSanitizingForLog_ThenReplacesEachWithOneSpace(
        string value,
        string expected)
    {
        var result = value.SanitizeForLog();

        result.Length.ShouldBe(value.Length);
        result.ShouldBe(expected);
    }
}
