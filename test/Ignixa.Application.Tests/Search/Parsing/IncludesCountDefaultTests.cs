// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

public class IncludesCountDefaultTests
{
    private static SearchOptionsBuilderHarness Harness()
        => SearchOptionsBuilderHarness.ForPatientChainedThrough("general-practitioner", "Practitioner", "name", SearchParamType.String);

    [Fact]
    public void GivenAnInclude_WhenBuildingSearchOptions_ThenIncludesMaxItemCountDefaultsToTheMaximumPageSize()
    {
        // Act
        SearchOptions options = Harness().Build([("_include", "Patient:general-practitioner")]);

        // Assert
        options.Include.Count.ShouldBe(1);
        options.IncludesMaxItemCount.ShouldBe(SearchOptionsBuilder.MaxAllowedItemCount);
    }

    [Fact]
    public void GivenARevInclude_WhenBuildingSearchOptions_ThenIncludesMaxItemCountDefaultsToTheMaximumPageSize()
    {
        // Act
        SearchOptions options = Harness().Build([("_revinclude", "Patient:general-practitioner")]);

        // Assert
        options.RevInclude.Count.ShouldBe(1);
        options.IncludesMaxItemCount.ShouldBe(SearchOptionsBuilder.MaxAllowedItemCount);
    }

    [Fact]
    public void GivenNoIncludes_WhenBuildingSearchOptions_ThenIncludesMaxItemCountStaysUnset()
    {
        // Act
        SearchOptions options = Harness().Build([("_count", "5")]);

        // Assert
        options.IncludesMaxItemCount.ShouldBeNull();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("25", 25)]
    public void GivenAnIncludeAndAnExplicitIncludesCount_WhenBuildingSearchOptions_ThenTheExplicitCountWins(string includesCount, int expected)
    {
        // Act
        SearchOptions options = Harness().Build(
        [
            ("_include", "Patient:general-practitioner"),
            ("_includesCount", includesCount),
        ]);

        // Assert
        options.IncludesMaxItemCount.ShouldBe(expected);
    }
}
