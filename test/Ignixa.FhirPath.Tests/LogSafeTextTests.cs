// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;

namespace Ignixa.FhirPath.Tests;

public class LogSafeTextTests
{
    [Fact]
    public void GivenNull_WhenSanitizing_ThenReturnsNull() =>
        LogSafeText.Sanitize(null).ShouldBeNull();

    [Fact]
    public void GivenControlCharacters_WhenSanitizing_ThenEachBecomesASpace() =>
        LogSafeText.Sanitize("a\r\nb\tc\u001bd\u0085e").ShouldBe("a  b c d e");

    [Fact]
    public void GivenTextLongerThanTheLimit_WhenSanitizing_ThenTruncatesAndNamesTheDroppedCount() =>
        LogSafeText.Sanitize(new string('a', 50), 10).ShouldBe("aaaaaaaaaa...[40 more characters]");

    [Fact]
    public void GivenSurrogatePairStraddlingTheLimit_WhenSanitizing_ThenThePairIsNotSplit() =>
        LogSafeText.Sanitize("abc😀def", 4).ShouldBe("abc...[5 more characters]");
}
