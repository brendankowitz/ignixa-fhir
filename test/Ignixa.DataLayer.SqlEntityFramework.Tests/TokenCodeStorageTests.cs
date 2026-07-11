// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

/// <summary>
/// Locks down TokenCodeStorage's split threshold against the database's own constraint
/// (CHK_TokenSearchParam_CodeOverflow: LEN(Code) = 256 OR CodeOverflow IS NULL) — a mismatch
/// between MaxInlineCodeLength and that constraint previously went undetected because this
/// class had zero test coverage.
/// </summary>
public class TokenCodeStorageTests
{
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

        // CHK_TokenSearchParam_CodeOverflow requires LEN(Code) = 256 whenever CodeOverflow is set.
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
