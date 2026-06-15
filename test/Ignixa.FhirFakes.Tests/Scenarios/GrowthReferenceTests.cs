// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// Tests for <see cref="GrowthReference"/> age-based anthropometric medians.
/// </summary>
public class GrowthReferenceTests
{
    [Fact]
    public void GivenToddler_WhenLookingUpHeight_ThenReturnsAgeAppropriateValue()
    {
        // A 1-year-old is roughly 70-82 cm, NOT an adult-range height.
        GrowthReference.MedianHeightCm(1).ShouldBeInRange(70m, 82m);
    }

    [Fact]
    public void GivenAdult_WhenLookingUpHeight_ThenReturnsAdultRangeValue()
    {
        GrowthReference.MedianHeightCm(40).ShouldBeInRange(160m, 185m);
    }

    [Theory]
    [InlineData(2, 8)]
    [InlineData(8, 14)]
    [InlineData(14, 18)]
    public void GivenChildhoodAges_WhenLookingUpHeight_ThenHeightIncreasesWithAge(int youngerAge, int olderAge)
    {
        GrowthReference.MedianHeightCm(youngerAge)
            .ShouldBeLessThan(GrowthReference.MedianHeightCm(olderAge));
    }

    [Fact]
    public void GivenAdultAges_WhenLookingUpHeight_ThenHeightPlateaus()
    {
        GrowthReference.MedianHeightCm(25).ShouldBe(GrowthReference.MedianHeightCm(60));
    }

    [Fact]
    public void GivenToddler_WhenLookingUpWeight_ThenReturnsAgeAppropriateValue()
    {
        GrowthReference.MedianWeightKg(1).ShouldBeInRange(8m, 13m);
    }

    [Theory]
    [InlineData(2, 10)]
    [InlineData(10, 16)]
    public void GivenChildhoodAges_WhenLookingUpWeight_ThenWeightIncreasesWithAge(int youngerAge, int olderAge)
    {
        GrowthReference.MedianWeightKg(youngerAge)
            .ShouldBeLessThan(GrowthReference.MedianWeightKg(olderAge));
    }
}
