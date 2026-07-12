// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

public class StringStorageTests
{
    [Fact]
    public void GivenConstants_WhenRead_ThenMatchExpectedValues()
    {
        StringStorage.InlineWidth.ShouldBe(256);
        StringStorage.DefaultCollation.ShouldBe("Latin1_General_100_CI_AI");
        StringStorage.ExactCollation.ShouldBe("Latin1_General_100_CS_AS");
    }

    [Fact]
    public void GivenValueAtOrUnderInlineWidth_WhenSplit_ThenNoOverflow()
    {
        var (inline, overflow) = StringStorage.Split(new string('a', 256));

        inline.Length.ShouldBe(256);
        overflow.ShouldBeNull();
    }

    [Fact]
    public void GivenValueOverInlineWidth_WhenSplit_ThenSplitsAtBoundary()
    {
        var value = new string('a', 300);

        var (inline, overflow) = StringStorage.Split(value);

        inline.Length.ShouldBe(256);
        overflow.ShouldBe(new string('a', 44));
    }
}
