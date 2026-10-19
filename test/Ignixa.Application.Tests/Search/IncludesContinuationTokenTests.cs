// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;
using Shouldly;

namespace Ignixa.Application.Tests.Search;

public class IncludesContinuationTokenTests
{
    [Fact]
    public void GivenTheMaximumOffset_WhenRoundTripping_ThenTheTokenDecodes()
    {
        string token = IncludesContinuationToken.Encode(IncludesContinuationToken.MaxAllowedOffset, 10);

        IncludesContinuationToken.TryDecode(token, out int offset, out int pageSize).ShouldBeTrue();
        offset.ShouldBe(IncludesContinuationToken.MaxAllowedOffset);
        pageSize.ShouldBe(10);
    }

    [Theory]
    [InlineData(IncludesContinuationToken.MaxAllowedOffset + 1, 10)]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 1001)]
    public void GivenAnOutOfRangeState_WhenDecoding_ThenDecodeFails(int offset, int pageSize)
    {
        IncludesContinuationToken.TryDecode(IncludesContinuationToken.Encode(offset, pageSize), out _, out _).ShouldBeFalse();
    }
}
