// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Builders.Profiles;

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Provides demographic data for US cities and supports weighted sampling.
/// </summary>
/// <remarks>
/// Uses real US Census data for major cities to generate realistic population distributions.
/// Future enhancement: Load from demographics.csv for 29,000+ cities.
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public class DemographicsDataProvider
{
    private readonly List<CityDemographics> _cities = [];

    /// <summary>
    /// Gets all cities available in this demographics provider.
    /// </summary>
    public IReadOnlyList<CityDemographics> Cities => _cities;

    /// <summary>
    /// Gets all unique state names available in this demographics provider.
    /// </summary>
    public IReadOnlyList<string> States => _cities.Select(c => c.State).Distinct().OrderBy(s => s).ToList();

    /// <summary>
    /// Creates a default provider with top 11 US cities and real census demographics.
    /// </summary>
    public static DemographicsDataProvider CreateDefault()
    {
        var provider = new DemographicsDataProvider();

        // Data source: US Census Bureau 2020 Census
        provider.AddCity(new CityDemographics(
            Name: "New York",
            State: "New York",
            Country: "US",
            Population: 8_336_817,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.208,
                ["18-44"] = 0.453,
                ["45-64"] = 0.239,
                ["65+"] = 0.100
            },
            MaleRatio: 0.476,
            ZipCodePrefix: "100",
            AreaCodes: ["212", "718", "917", "347", "646"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.424,
                    ["Black"] = 0.242,
                    ["Hispanic"] = 0.289,
                    ["Asian"] = 0.141
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Los Angeles",
            State: "California",
            Country: "US",
            Population: 3_979_576,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.213,
                ["18-44"] = 0.455,
                ["45-64"] = 0.232,
                ["65+"] = 0.100
            },
            MaleRatio: 0.496,
            ZipCodePrefix: "900",
            AreaCodes: ["213", "310", "323", "424", "818"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.487,
                    ["Black"] = 0.083,
                    ["Hispanic"] = 0.485,
                    ["Asian"] = 0.119
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Chicago",
            State: "Illinois",
            Country: "US",
            Population: 2_746_388,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.214,
                ["18-44"] = 0.468,
                ["45-64"] = 0.226,
                ["65+"] = 0.092
            },
            MaleRatio: 0.486,
            ZipCodePrefix: "606",
            AreaCodes: ["312", "773", "872"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.499,
                    ["Black"] = 0.290,
                    ["Hispanic"] = 0.290,
                    ["Asian"] = 0.068
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Houston",
            State: "Texas",
            Country: "US",
            Population: 2_304_580,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.258,
                ["18-44"] = 0.463,
                ["45-64"] = 0.210,
                ["65+"] = 0.069
            },
            MaleRatio: 0.500,
            ZipCodePrefix: "770",
            AreaCodes: ["713", "281", "832"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.575,
                    ["Black"] = 0.229,
                    ["Hispanic"] = 0.443,
                    ["Asian"] = 0.074
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Phoenix",
            State: "Arizona",
            Country: "US",
            Population: 1_680_992,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.250,
                ["18-44"] = 0.443,
                ["45-64"] = 0.216,
                ["65+"] = 0.091
            },
            MaleRatio: 0.501,
            ZipCodePrefix: "850",
            AreaCodes: ["602", "480", "623"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.716,
                    ["Black"] = 0.071,
                    ["Hispanic"] = 0.428,
                    ["Asian"] = 0.038
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Philadelphia",
            State: "Pennsylvania",
            Country: "US",
            Population: 1_603_797,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.216,
                ["18-44"] = 0.456,
                ["45-64"] = 0.233,
                ["65+"] = 0.095
            },
            MaleRatio: 0.475,
            ZipCodePrefix: "191",
            AreaCodes: ["215", "267"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.410,
                    ["Black"] = 0.421,
                    ["Hispanic"] = 0.151,
                    ["Asian"] = 0.076
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "San Antonio",
            State: "Texas",
            Country: "US",
            Population: 1_547_253,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.266,
                ["18-44"] = 0.449,
                ["45-64"] = 0.210,
                ["65+"] = 0.075
            },
            MaleRatio: 0.495,
            ZipCodePrefix: "782",
            AreaCodes: ["210", "726"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.802,
                    ["Black"] = 0.069,
                    ["Hispanic"] = 0.641,
                    ["Asian"] = 0.029
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "San Diego",
            State: "California",
            Country: "US",
            Population: 1_423_851,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.208,
                ["18-44"] = 0.467,
                ["45-64"] = 0.228,
                ["65+"] = 0.097
            },
            MaleRatio: 0.502,
            ZipCodePrefix: "921",
            AreaCodes: ["619", "858"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.650,
                    ["Black"] = 0.062,
                    ["Hispanic"] = 0.301,
                    ["Asian"] = 0.170
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Dallas",
            State: "Texas",
            Country: "US",
            Population: 1_343_573,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.258,
                ["18-44"] = 0.486,
                ["45-64"] = 0.199,
                ["65+"] = 0.057
            },
            MaleRatio: 0.501,
            ZipCodePrefix: "752",
            AreaCodes: ["214", "469", "972"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.613,
                    ["Black"] = 0.240,
                    ["Hispanic"] = 0.416,
                    ["Asian"] = 0.039
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Boston",
            State: "Massachusetts",
            Country: "US",
            Population: 675_647,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.170,
                ["18-44"] = 0.500,
                ["45-64"] = 0.210,
                ["65+"] = 0.120
            },
            MaleRatio: 0.480,
            ZipCodePrefix: "021",
            AreaCodes: ["617", "857"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.530,
                    ["Black"] = 0.250,
                    ["Hispanic"] = 0.190,
                    ["Asian"] = 0.090
                }
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Seattle",
            State: "Washington",
            Country: "US",
            Population: 737_015,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.155,
                ["18-44"] = 0.513,
                ["45-64"] = 0.239,
                ["65+"] = 0.093
            },
            MaleRatio: 0.502,
            ZipCodePrefix: "981",
            AreaCodes: ["206"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double>
                {
                    ["White"] = 0.657,
                    ["Black"] = 0.071,
                    ["Hispanic"] = 0.071,
                    ["Asian"] = 0.163
                }
            }
        ));

        // International cities - Australia
        // Data source: Australian Bureau of Statistics 2021 Census
        // Indigenous status distribution based on ABS data (approx. 3.2% nationally identify as Aboriginal/Torres Strait Islander)
        var australianIndigenousDistribution = new Dictionary<string, double>
        {
            ["1"] = 0.028, // Aboriginal but not Torres Strait Islander
            ["2"] = 0.002, // Torres Strait Islander but not Aboriginal
            ["3"] = 0.002, // Both Aboriginal and Torres Strait Islander
            ["4"] = 0.968  // Neither Aboriginal nor Torres Strait Islander
        };

        provider.AddCity(new CityDemographics(
            Name: "Melbourne",
            State: "Victoria",
            Country: "AU",
            Population: 5_078_000,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.195,
                ["18-44"] = 0.445,
                ["45-64"] = 0.265,
                ["65+"] = 0.095
            },
            MaleRatio: 0.495,
            ZipCodePrefix: "3000",
            AreaCodes: ["03"],
            Attributes: new Dictionary<string, object>
            {
                [AUBasePatientProfile.IndigenousStatusDistributionKey] = australianIndigenousDistribution
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Sydney",
            State: "New South Wales",
            Country: "AU",
            Population: 5_312_000,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.188,
                ["18-44"] = 0.460,
                ["45-64"] = 0.253,
                ["65+"] = 0.099
            },
            MaleRatio: 0.494,
            ZipCodePrefix: "2000",
            AreaCodes: ["02"],
            Attributes: new Dictionary<string, object>
            {
                [AUBasePatientProfile.IndigenousStatusDistributionKey] = australianIndigenousDistribution
            }
        ));

        provider.AddCity(new CityDemographics(
            Name: "Amsterdam",
            State: "North Holland",
            Country: "NL",
            Population: 872_680,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.165,
                ["18-44"] = 0.475,
                ["45-64"] = 0.245,
                ["65+"] = 0.115
            },
            MaleRatio: 0.498,
            ZipCodePrefix: "1011",
            AreaCodes: ["020"]
            // No profile-specific attributes for Amsterdam
        ));

        return provider;
    }

    public void AddCity(CityDemographics city) => _cities.Add(city);

    /// <summary>
    /// Selects a city from the specified state using weighted random sampling by population.
    /// </summary>
    public CityDemographics SelectCity(string state)
    {
        var citiesInState = _cities.Where(c => c.State == state).ToList();
        if (citiesInState.Count == 0) return _cities.First(); // Fallback to any city

        var totalPop = citiesInState.Sum(c => c.Population);
        var random = Random.Shared.Next(totalPop);
        var cumulative = 0;

        foreach (var city in citiesInState)
        {
            cumulative += city.Population;
            if (random < cumulative) return city;
        }

        return citiesInState[^1];
    }

    /// <summary>
    /// Samples an age from the city's age group distribution.
    /// </summary>
    public int SampleAge(CityDemographics city)
    {
        var ageGroup = SampleFromDistribution(city.AgeGroupDistribution);
        return ageGroup switch
        {
            "0-17" => Random.Shared.Next(0, 18),
            "18-44" => Random.Shared.Next(18, 45),
            "45-64" => Random.Shared.Next(45, 65),
            "65+" => Random.Shared.Next(65, 90),
            _ => Random.Shared.Next(0, 90)
        };
    }

    /// <summary>
    /// Samples a gender from the city's male ratio.
    /// </summary>
    public string SampleGender(CityDemographics city)
    {
        return Random.Shared.NextDouble() < city.MaleRatio ? PatientBuilderConstants.Gender.Male : PatientBuilderConstants.Gender.Female;
    }

    /// <summary>
    /// Samples a zip code from the city's zip code range.
    /// </summary>
    /// <example>
    /// Boston (prefix "021") → "02101", "02142", "02298", etc.
    /// </example>
    public string SampleZipCode(CityDemographics city)
    {
        // Generate a random 2-digit suffix for the zip code prefix
        var suffix = Random.Shared.Next(0, 100).ToString("D2");
        return city.ZipCodePrefix + suffix;
    }

    /// <summary>
    /// Samples a phone area code from the city's area codes.
    /// </summary>
    /// <example>
    /// Boston → "617" or "857"
    /// </example>
    public string SampleAreaCode(CityDemographics city)
    {
        return city.AreaCodes[Random.Shared.Next(city.AreaCodes.Count)];
    }

    /// <summary>
    /// Samples profile-specific attributes based on the city's demographics.
    /// Delegates to the city's profile for profile-specific attribute sampling.
    /// </summary>
    /// <param name="city">City demographics</param>
    /// <returns>Dictionary of profile-specific attributes</returns>
    /// <example>
    /// For US cities: { "ethnicity": "White" }
    /// For AU cities: { "indigenousStatus": "4" }
    /// </example>
    public Dictionary<string, object> SampleProfileAttributes(CityDemographics city)
    {
        ArgumentNullException.ThrowIfNull(city);

        // Delegate to the city's profile for attribute sampling
        var profile = city.GetProfile();
        return profile.SampleProfileAttributes(city);
    }

    private string SampleFromDistribution(Dictionary<string, double> distribution)
    {
        var random = Random.Shared.NextDouble();
        var cumulative = 0.0;

        foreach (var (key, prob) in distribution)
        {
            cumulative += prob;
            if (random < cumulative) return key;
        }

        return distribution.Keys.Last();
    }
}
