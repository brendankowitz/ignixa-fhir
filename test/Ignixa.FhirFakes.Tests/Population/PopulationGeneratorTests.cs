// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.Models;
using Ignixa.Abstractions;
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
        bundles.Count.ShouldBe(10);

        // Check each bundle is a transaction bundle with Patient resource
        foreach (var bundle in bundles)
        {
            bundle.Type.ShouldBe(BundleJsonNode.BundleType.Transaction);
            var patientResource = GetPatientFromBundle(bundle);
            patientResource.ShouldNotBeNull();
        }

        foreach (var bundle in bundles)
        {
            // All Boston patients should have zip codes starting with "021"
            var patientResource = GetPatientFromBundle(bundle)!;
            var address = patientResource["address"]?[0];
            if (address is not null)
            {
                var postalCode = address["postalCode"]?.GetValue<string>();
                var result = Should.NotThrow(() => postalCode);
                result.ShouldNotBeNullOrEmpty();
                result.ShouldStartWith("021");
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
        bundles.Count.ShouldBe(10);

        foreach (var bundle in bundles)
        {
            // All Boston patients should have area codes 617 or 857
            var patientResource = GetPatientFromBundle(bundle)!;
            var telecom = patientResource["telecom"]?[0];
            if (telecom is not null)
            {
                var phoneNumber = telecom["value"]?.GetValue<string>();
                phoneNumber.ShouldNotBeNullOrEmpty();

                // Extract area code (first 3 digits before first dash)
                var areaCode = phoneNumber?.Split('-')[0];
                areaCode.ShouldBeOneOf(BostonAreaCodes,
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
        bundles.Count.ShouldBe(10);

        foreach (var bundle in bundles)
        {
            var patientResource = GetPatientFromBundle(bundle)!;

            // Seattle zip codes start with "981"
            var address = patientResource["address"]?[0];
            if (address is not null)
            {
                var postalCode = address["postalCode"]?.GetValue<string>();
                var result = Should.NotThrow(() => postalCode);
                result.ShouldNotBeNullOrEmpty();
                result.ShouldStartWith("981");
            }

            // Seattle has area code "206"
            var telecom = patientResource["telecom"]?[0];
            if (telecom is not null)
            {
                var phoneNumber = telecom["value"]?.GetValue<string>();
                phoneNumber.ShouldNotBeNullOrEmpty();

                var areaCode = phoneNumber?.Split('-')[0];
                areaCode.ShouldBe("206", "Seattle area code is 206");
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
        bundles.Count.ShouldBe(20);

        foreach (var bundle in bundles)
        {
            var patientResource = GetPatientFromBundle(bundle)!;

            // Texas patients should have zip codes from one of the three major cities
            var address = patientResource["address"]?[0];
            if (address is not null)
            {
                var postalCode = address["postalCode"]?.GetValue<string>();
                postalCode.ShouldNotBeNullOrEmpty();

                var zipPrefix = postalCode?[..3];
                zipPrefix.ShouldBeOneOf(validTexasZipPrefixes,
                    "Texas zip codes should be from Houston (770), San Antonio (782), or Dallas (752)");
            }

            // Texas patients should have area codes from one of the three major cities
            var telecom = patientResource["telecom"]?[0];
            if (telecom is not null)
            {
                var phoneNumber = telecom["value"]?.GetValue<string>();
                phoneNumber.ShouldNotBeNullOrEmpty();

                var areaCode = phoneNumber?.Split('-')[0];
                areaCode.ShouldBeOneOf(validTexasAreaCodes,
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
        bundles.Count.ShouldBe(10);

        foreach (var bundle in bundles)
        {
            var patientResource = GetPatientFromBundle(bundle)!;
            patientResource["address"].ShouldNotBeNull("all patients should have addresses");
            patientResource["telecom"].ShouldNotBeNull("all patients should have phone numbers");
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
        var result = Should.NotThrow(() => zipCode);
        result.ShouldNotBeNullOrEmpty();
        result.Length.ShouldBe(5);
        result.ShouldStartWith(city.ZipCodePrefix);
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
        var result = Should.NotThrow(() => areaCode);
        result.ShouldNotBeNullOrEmpty();
        result.Length.ShouldBe(3);
        result.ShouldBeOneOf([.. city.AreaCodes]);
    }

    [Fact]
    public void GivenPopulationGenerator_WhenGettingAvailableStates_ThenReturnsAllSupportedStates()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var states = generator.AvailableStates;

        // Assert
        var result = Should.NotThrow(() => states);
        result.ShouldNotBeEmpty();
        result.Count.ShouldBeGreaterThanOrEqualTo(8);
        result.ShouldContain("Massachusetts");
        result.ShouldContain("California");
        result.ShouldContain("Texas");
        result.ShouldContain("Washington");
        result.ShouldBeInOrder();
    }

    [Fact]
    public void GivenPopulationGenerator_WhenGettingAvailableCities_ThenReturnsAllCitiesWithDemographics()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var cities = generator.AvailableCities;

        // Assert
        var result = Should.NotThrow(() => cities);
        result.ShouldNotBeEmpty();
        result.Count.ShouldBeGreaterThanOrEqualTo(11);

        // Verify all cities have complete demographic data
        foreach (var city in cities)
        {
            city.Name.ShouldNotBeNullOrEmpty();
            city.State.ShouldNotBeNullOrEmpty();
            city.Population.ShouldBeGreaterThan(0);
            city.AgeGroupDistribution.ShouldNotBeEmpty();
            city.MaleRatio.ShouldBeInRange(0.4, 0.6, "typical male ratio is 40-60%");
            city.ZipCodePrefix.ShouldNotBeNullOrEmpty();
            city.AreaCodes.ShouldNotBeEmpty();

            // Verify profile-specific attributes based on country
            if (city.IsUSA)
            {
                city.Attributes.ShouldContainKey(USCorePatientProfile.EthnicityDistributionKey);
                var ethnicityDistribution = city.Attributes[USCorePatientProfile.EthnicityDistributionKey] as Dictionary<string, double>;
                ethnicityDistribution.ShouldNotBeNull();
                ethnicityDistribution.ShouldNotBeEmpty();
            }
            else if (city.IsAustralian)
            {
                city.Attributes.ShouldContainKey(AUBasePatientProfile.IndigenousStatusDistributionKey);
                var indigenousDistribution = city.Attributes[AUBasePatientProfile.IndigenousStatusDistributionKey] as Dictionary<string, double>;
                indigenousDistribution.ShouldNotBeNull();
                indigenousDistribution.ShouldNotBeEmpty();
            }
        }
    }

    [Fact]
    public void GivenKnownCities_WhenAccessingBoston_ThenReturnsBostonDemographics()
    {
        // Arrange & Act
        var boston = KnownCities.Boston;

        // Assert
        boston.ShouldNotBeNull();
        boston.Name.ShouldBe("Boston");
        boston.State.ShouldBe("Massachusetts");
        boston.Population.ShouldBe(675_647);
        boston.ZipCodePrefix.ShouldBe("021");
        boston.AreaCodes.ShouldBe(BostonAreaCodes);
    }

    [Fact]
    public void GivenKnownCities_WhenAccessingSeattle_ThenReturnsSeattleDemographics()
    {
        // Arrange & Act
        var seattle = KnownCities.Seattle;

        // Assert
        seattle.ShouldNotBeNull();
        seattle.Name.ShouldBe("Seattle");
        seattle.State.ShouldBe("Washington");
        seattle.Population.ShouldBe(737_015);
        seattle.ZipCodePrefix.ShouldBe("981");
        seattle.AreaCodes.ShouldContain("206");
    }

    [Fact]
    public void GivenKnownCities_WhenAccessingAll_ThenReturnsAllCities()
    {
        // Arrange & Act
        var allCities = KnownCities.All;

        // Assert
        allCities.Count.ShouldBe(14);
        allCities.ShouldContain(c => c.Name == "Boston");
        allCities.ShouldContain(c => c.Name == "Seattle");
        allCities.ShouldContain(c => c.Name == "New York");
        allCities.ShouldContain(c => c.Name == "Los Angeles");
        allCities.ShouldContain(c => c.Name == "Melbourne");
        allCities.ShouldContain(c => c.Name == "Sydney");
        allCities.ShouldContain(c => c.Name == "Amsterdam");
    }

    [Fact]
    public void GivenDemographicsProvider_WhenGettingStates_ThenReturnsDistinctStatesOnly()
    {
        // Arrange
        var demographics = DemographicsDataProvider.CreateDefault();

        // Act
        var states = demographics.States;

        // Assert
        states.Distinct().Count().ShouldBe(states.Count, "states should not be duplicated");
        states.SequenceEqual(states.OrderBy(s => s)).ShouldBeTrue("states should be alphabetically sorted");

        // Texas has 3 cities (Houston, San Antonio, Dallas) but should appear only once
        states.Count(s => s == "Texas").ShouldBe(1);

        // California has 2 cities (Los Angeles, San Diego) but should appear only once
        states.Count(s => s == "California").ShouldBe(1);
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

    [Fact]
    public void GivenPopulation_WhenGenerating_ThenPatientsHaveVitalSignObservations()
    {
        // Arrange
        var generator = new PopulationGenerator(_schemaProvider);

        // Act
        var contexts = generator.Generate("Massachusetts", 20).ToList();

        // Assert - wellness visits across the cohort must record observations,
        // otherwise population data has no vitals or labs at all.
        var totalObservations = contexts.Sum(c => c.Observations.Count);
        totalObservations.ShouldBeGreaterThan(0, "population wellness visits should record vital sign observations");
    }
}
