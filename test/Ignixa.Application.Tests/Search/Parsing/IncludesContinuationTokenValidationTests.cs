// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Exceptions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

public class IncludesContinuationTokenValidationTests
{
    private static SearchOptionsBuilderHarness Harness()
        => SearchOptionsBuilderHarness.ForPatientChainedThrough("general-practitioner", "Practitioner", "name", SearchParamType.String);

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("eyJJbmNsdWRlc09mZnNldCI6MTAwMDAxLCJQYWdlU2l6ZSI6MTB9")] // {"IncludesOffset":100001,"PageSize":10}
    public void GivenAnUndecodableIncludesToken_WhenBuildingSearchOptions_ThenTheSearchIsRejected(string token)
    {
        Should.Throw<BadSearchRequestException>(() => Harness().Build(
            [("_include", "Patient:general-practitioner"), ("_includesContinuationToken", token)]));
    }

    [Fact]
    public void GivenAValidIncludesToken_WhenBuildingSearchOptions_ThenTheTokenIsKept()
    {
        string token = IncludesContinuationToken.Encode(10, 10);

        SearchOptions options = Harness().Build(
            [("_include", "Patient:general-practitioner"), ("_includesContinuationToken", token)]);

        options.IncludesContinuationToken.ShouldBe(token);
    }
}
