// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Population;

/// <summary>
/// Tests for DemographicsDataProvider.SampleZipCode across all PostalCodeFormat shapes.
/// </summary>
public class DemographicsDataProviderTests
{
    private static CityDemographics MakeCity(string zipCodePrefix, PostalCodeFormat format) =>
        new(
            Name: "Testville",
            State: "Test State",
            Country: "XX",
            Population: 1000,
            AgeGroupDistribution: new() { ["0-17"] = 1.0 },
            MaleRatio: 0.5,
            ZipCodePrefix: zipCodePrefix,
            AreaCodes: ["000"],
            PostalCodeFormat: format);

    [Fact]
    public void GivenNumericSuffixFormat_WhenSamplingZipCode_ThenAppendsTwoDigitSuffix()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("021", PostalCodeFormat.NumericSuffix);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^021\\d{2}$");
    }

    [Fact]
    public void GivenFixedNumericFormat_WhenSamplingZipCode_ThenReturnsPrefixUnchanged()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("3000", PostalCodeFormat.FixedNumeric);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldBe("3000");
    }

    [Fact]
    public void GivenDutchAlphaNumericFormat_WhenSamplingZipCode_ThenReturnsFourDigitsSpaceTwoLetters()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("1011", PostalCodeFormat.DutchAlphaNumeric);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^1011 [A-Z]{2}$");
    }

    [Fact]
    public void GivenUKAlphaNumericFormat_WhenSamplingZipCode_ThenReturnsOutwardCodeSpaceDigitTwoLetters()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("SW1A", PostalCodeFormat.UKAlphaNumeric);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^SW1A \\d[A-Z]{2}$");
    }

    [Fact]
    public void GivenSameSeed_WhenSamplingZipCodeTwice_ThenReturnsSameValue()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("SW1A", PostalCodeFormat.UKAlphaNumeric);

        // Act
        var first = provider.SampleZipCode(city, new Bogus.Randomizer(42));
        var second = provider.SampleZipCode(city, new Bogus.Randomizer(42));

        // Assert
        first.ShouldBe(second);
    }

    [Theory]
    [InlineData("Melbourne")]
    [InlineData("Sydney")]
    public void GivenAustralianKnownCity_WhenSamplingZipCode_ThenReturnsExactFourDigitPostcode(string cityName)
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = provider.Cities.First(c => c.Name == cityName);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldBe(city.ZipCodePrefix);
        zipCode.Length.ShouldBe(4);
    }

    [Fact]
    public void GivenAmsterdam_WhenSamplingZipCode_ThenReturnsFourDigitsSpaceTwoLetters()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = provider.Cities.First(c => c.Name == "Amsterdam");
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^1011 [A-Z]{2}$");
    }

    [Fact]
    public void GivenLondon_WhenSamplingZipCode_ThenReturnsOutwardCodeSpaceDigitTwoLetters()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = provider.Cities.First(c => c.Name == "London");
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^SW1A \\d[A-Z]{2}$");
    }

    [Fact]
    public void GivenLondon_WhenReadingEthnicCategoryDistribution_ThenProbabilitiesSumToOne()
    {
        // Arrange
        var city = KnownCities.London;

        // Act
        city.Attributes.TryGetValue(UKCorePatientProfile.EthnicCategoryDistributionKey, out var raw).ShouldBeTrue();
        var distribution = raw.ShouldBeOfType<Dictionary<string, double>>();

        // Assert
        distribution.Values.Sum().ShouldBe(1.0, 0.001);
    }
}
