// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Population;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Population;

/// <summary>
/// Tests for the CityDemographics record, focused on PostalCodeFormat defaulting behavior.
/// </summary>
public class CityDemographicsTests
{
    [Fact]
    public void GivenCityDemographics_WhenPostalCodeFormatNotSpecified_ThenDefaultsToNumericSuffix()
    {
        // Arrange & Act
        var city = new CityDemographics(
            Name: "Testville",
            State: "Test State",
            Country: "US",
            Population: 1000,
            AgeGroupDistribution: new() { ["0-17"] = 1.0 },
            MaleRatio: 0.5,
            ZipCodePrefix: "000",
            AreaCodes: ["000"]);

        // Assert
        city.PostalCodeFormat.ShouldBe(PostalCodeFormat.NumericSuffix);
    }

    [Theory]
    [InlineData(PostalCodeFormat.NumericSuffix)]
    [InlineData(PostalCodeFormat.FixedNumeric)]
    [InlineData(PostalCodeFormat.DutchAlphaNumeric)]
    [InlineData(PostalCodeFormat.UkAlphaNumeric)]
    public void GivenCityDemographics_WhenPostalCodeFormatSpecified_ThenUsesThatValue(PostalCodeFormat format)
    {
        // Arrange & Act
        var city = new CityDemographics(
            Name: "Testville",
            State: "Test State",
            Country: "US",
            Population: 1000,
            AgeGroupDistribution: new() { ["0-17"] = 1.0 },
            MaleRatio: 0.5,
            ZipCodePrefix: "000",
            AreaCodes: ["000"],
            PostalCodeFormat: format);

        // Assert
        city.PostalCodeFormat.ShouldBe(format);
    }
}
