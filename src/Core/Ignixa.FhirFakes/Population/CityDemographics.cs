// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Demographic profile for a US city including population, race/ethnicity distribution, and age distribution.
/// </summary>
/// <param name="Name">City name</param>
/// <param name="State">State name</param>
/// <param name="Population">Total population</param>
/// <param name="RaceDistribution">Race/ethnicity distribution (e.g., "White": 0.53, "Hispanic": 0.19)</param>
/// <param name="AgeGroupDistribution">Age group distribution (e.g., "0-17": 0.22, "18-44": 0.35)</param>
/// <param name="MaleRatio">Proportion of population that is male (e.g., 0.49 = 49% male, 51% female)</param>
/// <param name="ZipCodePrefix">Zip code prefix range for the city (e.g., "021" for Boston 02101-02298)</param>
/// <param name="AreaCodes">Phone area codes for the city (e.g., ["617", "857"] for Boston)</param>
public record CityDemographics(
    string Name,
    string State,
    int Population,
    Dictionary<string, double> RaceDistribution,
    Dictionary<string, double> AgeGroupDistribution,
    double MaleRatio,
    string ZipCodePrefix,
    IReadOnlyList<string> AreaCodes
);
