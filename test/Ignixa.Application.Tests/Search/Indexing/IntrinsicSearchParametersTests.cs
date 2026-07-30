// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

public class IntrinsicSearchParametersTests
{
    [Theory]
    [InlineData("_id")]
    [InlineData("_type")]
    [InlineData("_lastUpdated")]
    public void GivenAnIntrinsicCode_WhenClassified_ThenItIsRecognised(string code)
    {
        // Arrange, Act
        var isIntrinsic = IntrinsicSearchParameters.IsIntrinsicCode(code);

        // Assert
        isIntrinsic.ShouldBeTrue();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("birthdate")]
    [InlineData("_tag")]
    [InlineData("_profile")]
    [InlineData("_ID")]
    [InlineData("")]
    [InlineData(null)]
    public void GivenANonIntrinsicCode_WhenClassified_ThenItIsNotRecognised(string code)
    {
        // Arrange, Act
        var isIntrinsic = IntrinsicSearchParameters.IsIntrinsicCode(code);

        // Assert: the comparison is ordinal and case-sensitive, matching how codes are compared
        // everywhere else in the compiler. _tag and _profile are resource metadata but are indexed,
        // so they are not intrinsic.
        isIntrinsic.ShouldBeFalse();
    }

    [Fact]
    public void GivenTheCodes_WhenComparedToThePredicate_ThenEveryMemberIsRecognised()
    {
        // Arrange, Act, Assert: the set and the predicate are one definition, so a code added to Codes
        // is classified without anyone remembering to update a second literal.
        IntrinsicSearchParameters.Codes.ShouldAllBe(code => IntrinsicSearchParameters.IsIntrinsicCode(code));
    }
}
