// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.Models;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Population;

/// <summary>
/// Tests for PopulationGenerator to ensure realistic demographics and geographic consistency.
/// </summary>
public class PopulationGeneratorTests
{
    private static readonly string[] BostonAreaCodes = ["617", "857"];
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    [Fact]
    public void GivenMassachusettsPopulation_WhenGenerating_ThenPatientsHaveBostonZipCodes()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var contexts = generator.Generate("Massachusetts", 10).ToList();
        var bundles = contexts.Select(c => c.ToBundle()).ToList();

        // Assert
        bundles.Should().HaveCount(10);

        // Check each bundle is a transaction bundle with Patient resource
        foreach (var bundle in bundles)
        {
            bundle.Type.Should().Be(BundleJsonNode.BundleType.Transaction);
            var patientResource = GetPatientFromBundle(bundle);
            patientResource.Should().NotBeNull();
        }

        foreach (var bundle in bundles)
        {
            // All Boston patients should have zip codes starting with "021"
            var patientResource = GetPatientFromBundle(bundle)!;
            var address = patientResource["address"]?[0];
            if (address is not null)
            {
                var postalCode = address["postalCode"]?.GetValue<string>();
                postalCode.Should().NotBeNullOrEmpty()
                    .And.StartWith("021", "Boston zip codes start with 021");
            }
        }
    }

    [Fact]
    public void GivenMassachusettsPopulation_WhenGenerating_ThenPatientsHaveBostonAreaCodes()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var contexts = generator.Generate("Massachusetts", 10).ToList();
        var bundles = contexts.Select(c => c.ToBundle()).ToList();

        // Assert
        bundles.Should().HaveCount(10);

        foreach (var bundle in bundles)
        {
            // All Boston patients should have area codes 617 or 857
            var patientResource = GetPatientFromBundle(bundle)!;
            var telecom = patientResource["telecom"]?[0];
            if (telecom is not null)
            {
                var phoneNumber = telecom["value"]?.GetValue<string>();
                phoneNumber.Should().NotBeNullOrEmpty();

                // Extract area code (first 3 digits before first dash)
                var areaCode = phoneNumber?.Split('-')[0];
                areaCode.Should().BeOneOf(BostonAreaCodes,
                    "Boston area codes are 617 or 857");
            }
        }
    }

    [Fact]
    public void GivenWashingtonPopulation_WhenGenerating_ThenPatientsHaveSeattleGeography()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var contexts = generator.Generate("Washington", 10).ToList();
        var bundles = contexts.Select(c => c.ToBundle()).ToList();

        // Assert
        bundles.Should().HaveCount(10);

        foreach (var bundle in bundles)
        {
            var patientResource = GetPatientFromBundle(bundle)!;

            // Seattle zip codes start with "981"
            var address = patientResource["address"]?[0];
            if (address is not null)
            {
                var postalCode = address["postalCode"]?.GetValue<string>();
                postalCode.Should().NotBeNullOrEmpty()
                    .And.StartWith("981", "Seattle zip codes start with 981");
            }

            // Seattle has area code "206"
            var telecom = patientResource["telecom"]?[0];
            if (telecom is not null)
            {
                var phoneNumber = telecom["value"]?.GetValue<string>();
                phoneNumber.Should().NotBeNullOrEmpty();

                var areaCode = phoneNumber?.Split('-')[0];
                areaCode.Should().Be("206", "Seattle area code is 206");
            }
        }
    }

    [Fact]
    public void GivenTexasPopulation_WhenGenerating_ThenPatientsHaveTexasCityGeography()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);
        var validTexasZipPrefixes = new[] { "770", "782", "752" }; // Houston, San Antonio, Dallas
        var validTexasAreaCodes = new[] { "713", "281", "832", "210", "726", "214", "469", "972" };

        // Act
        var contexts = generator.Generate("Texas", 20).ToList();
        var bundles = contexts.Select(c => c.ToBundle()).ToList();

        // Assert
        bundles.Should().HaveCount(20);

        foreach (var bundle in bundles)
        {
            var patientResource = GetPatientFromBundle(bundle)!;

            // Texas patients should have zip codes from one of the three major cities
            var address = patientResource["address"]?[0];
            if (address is not null)
            {
                var postalCode = address["postalCode"]?.GetValue<string>();
                postalCode.Should().NotBeNullOrEmpty();

                var zipPrefix = postalCode?[..3];
                zipPrefix.Should().BeOneOf(validTexasZipPrefixes,
                    "Texas zip codes should be from Houston (770), San Antonio (782), or Dallas (752)");
            }

            // Texas patients should have area codes from one of the three major cities
            var telecom = patientResource["telecom"]?[0];
            if (telecom is not null)
            {
                var phoneNumber = telecom["value"]?.GetValue<string>();
                phoneNumber.Should().NotBeNullOrEmpty();

                var areaCode = phoneNumber?.Split('-')[0];
                areaCode.Should().BeOneOf(validTexasAreaCodes,
                    "Texas area codes should be from Houston, San Antonio, or Dallas");
            }
        }
    }

    [Fact]
    public void GivenPopulation_WhenGenerating_ThenAllPatientsHaveAddressesAndPhones()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var contexts = generator.Generate("California", 10).ToList();
        var bundles = contexts.Select(c => c.ToBundle()).ToList();

        // Assert
        bundles.Should().HaveCount(10);

        foreach (var bundle in bundles)
        {
            var patientResource = GetPatientFromBundle(bundle)!;
            patientResource["address"].Should().NotBeNull("all patients should have addresses");
            patientResource["telecom"].Should().NotBeNull("all patients should have phone numbers");
        }
    }

    [Fact]
    public void GivenCityDemographics_WhenSamplingZipCode_ThenReturnsValidZipCode()
    {
        // Arrange
        var demographics = DemographicsDataProvider.CreateDefault();
        var city = demographics.SelectCity("Massachusetts");

        // Act
        var zipCode = demographics.SampleZipCode(city);

        // Assert
        zipCode.Should().NotBeNullOrEmpty()
            .And.HaveLength(5, "US zip codes are 5 digits")
            .And.StartWith(city.ZipCodePrefix);
    }

    [Fact]
    public void GivenCityDemographics_WhenSamplingAreaCode_ThenReturnsValidAreaCode()
    {
        // Arrange
        var demographics = DemographicsDataProvider.CreateDefault();
        var city = demographics.SelectCity("Washington");

        // Act
        var areaCode = demographics.SampleAreaCode(city);

        // Assert
        areaCode.Should().NotBeNullOrEmpty()
            .And.HaveLength(3, "US area codes are 3 digits")
            .And.BeOneOf(city.AreaCodes);
    }

    [Fact]
    public void GivenPopulationGenerator_WhenGettingAvailableStates_ThenReturnsAllSupportedStates()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var states = generator.AvailableStates;

        // Assert
        states.Should().NotBeEmpty()
            .And.HaveCountGreaterThanOrEqualTo(8, "should have at least 8 states")
            .And.Contain("Massachusetts")
            .And.Contain("California")
            .And.Contain("Texas")
            .And.Contain("Washington")
            .And.BeInAscendingOrder("states should be alphabetically sorted");
    }

    [Fact]
    public void GivenPopulationGenerator_WhenGettingAvailableCities_ThenReturnsAllCitiesWithDemographics()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var cities = generator.AvailableCities;

        // Assert
        cities.Should().NotBeEmpty()
            .And.HaveCountGreaterThanOrEqualTo(11, "should have at least 11 cities");

        // Verify all cities have complete demographic data
        foreach (var city in cities)
        {
            city.Name.Should().NotBeNullOrEmpty();
            city.State.Should().NotBeNullOrEmpty();
            city.Population.Should().BeGreaterThan(0);
            city.AgeGroupDistribution.Should().NotBeEmpty();
            city.MaleRatio.Should().BeInRange(0.4, 0.6, "typical male ratio is 40-60%");
            city.ZipCodePrefix.Should().NotBeNullOrEmpty();
            city.AreaCodes.Should().NotBeEmpty();

            // Verify profile-specific attributes based on country
            if (city.IsUSA)
            {
                city.Attributes.Should().ContainKey(USCorePatientProfile.EthnicityDistributionKey);
                var ethnicityDistribution = city.Attributes[USCorePatientProfile.EthnicityDistributionKey] as Dictionary<string, double>;
                ethnicityDistribution.Should().NotBeNull().And.NotBeEmpty();
            }
            else if (city.IsAustralian)
            {
                city.Attributes.Should().ContainKey(AUBasePatientProfile.IndigenousStatusDistributionKey);
                var indigenousDistribution = city.Attributes[AUBasePatientProfile.IndigenousStatusDistributionKey] as Dictionary<string, double>;
                indigenousDistribution.Should().NotBeNull().And.NotBeEmpty();
            }
        }
    }

    [Fact]
    public void GivenKnownCities_WhenAccessingBoston_ThenReturnsBostonDemographics()
    {
        // Arrange & Act
        var boston = KnownCities.Boston;

        // Assert
        boston.Should().NotBeNull();
        boston.Name.Should().Be("Boston");
        boston.State.Should().Be("Massachusetts");
        boston.Population.Should().Be(675_647);
        boston.ZipCodePrefix.Should().Be("021");
        boston.AreaCodes.Should().BeEquivalentTo(BostonAreaCodes);
    }

    [Fact]
    public void GivenKnownCities_WhenAccessingSeattle_ThenReturnsSeattleDemographics()
    {
        // Arrange & Act
        var seattle = KnownCities.Seattle;

        // Assert
        seattle.Should().NotBeNull();
        seattle.Name.Should().Be("Seattle");
        seattle.State.Should().Be("Washington");
        seattle.Population.Should().Be(737_015);
        seattle.ZipCodePrefix.Should().Be("981");
        seattle.AreaCodes.Should().Contain("206");
    }

    [Fact]
    public void GivenKnownCities_WhenAccessingAll_ThenReturnsAllCities()
    {
        // Arrange & Act
        var allCities = KnownCities.All;

        // Assert
        allCities.Should().HaveCount(14)
            .And.Contain(c => c.Name == "Boston")
            .And.Contain(c => c.Name == "Seattle")
            .And.Contain(c => c.Name == "New York")
            .And.Contain(c => c.Name == "Los Angeles")
            .And.Contain(c => c.Name == "Melbourne")
            .And.Contain(c => c.Name == "Sydney")
            .And.Contain(c => c.Name == "Amsterdam");
    }

    [Fact]
    public void GivenDemographicsProvider_WhenGettingStates_ThenReturnsDistinctStatesOnly()
    {
        // Arrange
        var demographics = DemographicsDataProvider.CreateDefault();

        // Act
        var states = demographics.States;

        // Assert
        states.Should().OnlyHaveUniqueItems("states should not be duplicated")
            .And.BeInAscendingOrder("states should be alphabetically sorted");

        // Texas has 3 cities (Houston, San Antonio, Dallas) but should appear only once
        states.Count(s => s == "Texas").Should().Be(1);

        // California has 2 cities (Los Angeles, San Diego) but should appear only once
        states.Count(s => s == "California").Should().Be(1);
    }

    /// <summary>
    /// Helper method to extract the Patient resource from a transaction bundle.
    /// </summary>
    private static System.Text.Json.Nodes.JsonNode? GetPatientFromBundle(BundleJsonNode bundle)
    {
        foreach (var entry in bundle.Entry)
        {
            var resource = entry.Resource;
            if (resource?.ResourceType == "Patient")
            {
                return resource.MutableNode;
            }
        }

        return null;
    }
}
