// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Models;

namespace Ignixa.DeId.Tests.Models;

public class ResultTests
{
    [Fact]
    public void GivenSuccessfulResult_WhenMatching_ThenOnlySuccessBranchReceivesValue()
    {
        var result = Result<int>.Success(42);

        var value = result.Match(value => value + 1, _ => throw new InvalidOperationException("Wrong branch"));

        value.ShouldBe(43);
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void GivenFailedResult_WhenMatching_ThenOnlyFailureBranchReceivesError()
    {
        var error = new DeIdError("TEST_ERROR", "Invalid input");
        var result = Result<int>.Failure(error);

        var code = result.Match(_ => throw new InvalidOperationException("Wrong branch"), failure => failure.Code);

        code.ShouldBe("TEST_ERROR");
        result.Error.ShouldBeSameAs(error);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
