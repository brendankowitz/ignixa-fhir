// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

/// <summary>
/// Locks down TokenCodeStorage's split threshold against TokenSearchParam.Code's actual VARCHAR(256)
/// column width. A mismatch here previously went undetected because this class had zero test coverage —
/// MaxInlineCodeLength was 128 for years while the column was 256 wide, silently desyncing write and
/// read for codes in that band.
/// </summary>
public class TokenCodeStorageTests
{
    [Fact]
    public void GivenMaxInlineCodeLength_WhenChecked_ThenMatchesTokenSearchParamCodeColumnWidth()
    {
        // Pins the value itself, not just behavior relative to it - the other tests below all
        // derive their inputs from MaxInlineCodeLength, so they'd stay green even if this constant
        // regressed back to 128 (or anything else) without this explicit assertion.
        TokenCodeStorage.MaxInlineCodeLength.ShouldBe(256);
    }

    [Fact]
    public void GivenCodeAtMaxInlineLength_WhenSplit_ThenStoredInlineWithNoOverflow()
    {
        var code = new string('a', TokenCodeStorage.MaxInlineCodeLength);

        var (storedCode, overflow) = TokenCodeStorage.SplitCode(code);

        storedCode.ShouldBe(code);
        storedCode.Length.ShouldBe(TokenCodeStorage.MaxInlineCodeLength);
        overflow.ShouldBeNull();
    }

    [Fact]
    public void GivenCodeOneCharOverMaxInlineLength_WhenSplit_ThenInlinePortionSatisfiesCheckConstraint()
    {
        var code = new string('a', TokenCodeStorage.MaxInlineCodeLength + 1);

        var (storedCode, overflow) = TokenCodeStorage.SplitCode(code);

        storedCode.Length.ShouldBe(TokenCodeStorage.MaxInlineCodeLength);
        overflow.ShouldBe("a");
    }

    [Fact]
    public void GivenCodeUnderMaxInlineLength_WhenSplit_ThenStoredInlineWithNoOverflow()
    {
        var code = new string('a', TokenCodeStorage.MaxInlineCodeLength - 1);

        var (storedCode, overflow) = TokenCodeStorage.SplitCode(code);

        storedCode.ShouldBe(code);
        overflow.ShouldBeNull();
    }

    [Fact]
    public void GivenVeryLongCode_WhenSplit_ThenInlinePortionSatisfiesCheckConstraint()
    {
        var code = new string('a', TokenCodeStorage.MaxInlineCodeLength * 3);

        var (storedCode, overflow) = TokenCodeStorage.SplitCode(code);

        storedCode.Length.ShouldBe(TokenCodeStorage.MaxInlineCodeLength);
        overflow!.Length.ShouldBe(TokenCodeStorage.MaxInlineCodeLength * 2);
    }

    [Fact]
    public void GivenNullCode_WhenSplit_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => TokenCodeStorage.SplitCode(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GivenEmptyOrNullSystem_WhenCheckingExplicitNoSystem_ThenReturnsTrue(string? system)
    {
        TokenCodeStorage.IsExplicitNoSystem(system).ShouldBeTrue();
    }

    [Fact]
    public void GivenNonEmptySystem_WhenCheckingExplicitNoSystem_ThenReturnsFalse()
    {
        TokenCodeStorage.IsExplicitNoSystem("http://example.org/system").ShouldBeFalse();
    }
}
