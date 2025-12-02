// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Provides static references to all built-in cities with demographic data.
/// </summary>
/// <remarks>
/// Use these constants for strongly-typed access to city demographics instead of string lookups.
/// All demographic data is from US Census Bureau 2020 Census.
/// </remarks>
public static class KnownCities
{
    private static readonly DemographicsDataProvider DefaultProvider = DemographicsDataProvider.CreateDefault();

    /// <summary>
    /// Boston, Massachusetts (Population: 675,647)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 021
    /// - Area Codes: 617, 857
    /// - Demographics: 53% White, 25% Black, 19% Hispanic, 9% Asian
    /// </remarks>
    public static CityDemographics Boston => DefaultProvider.Cities.First(c => c.Name == "Boston");

    /// <summary>
    /// New York, New York (Population: 8,336,817)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 100
    /// - Area Codes: 212, 718, 917, 347, 646
    /// - Demographics: 42.4% White, 24.2% Black, 28.9% Hispanic, 14.1% Asian
    /// </remarks>
    public static CityDemographics NewYork => DefaultProvider.Cities.First(c => c.Name == "New York");

    /// <summary>
    /// Los Angeles, California (Population: 3,979,576)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 900
    /// - Area Codes: 213, 310, 323, 424, 818
    /// - Demographics: 48.7% White, 8.3% Black, 48.5% Hispanic, 11.9% Asian
    /// </remarks>
    public static CityDemographics LosAngeles => DefaultProvider.Cities.First(c => c.Name == "Los Angeles");

    /// <summary>
    /// Chicago, Illinois (Population: 2,746,388)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 606
    /// - Area Codes: 312, 773, 872
    /// - Demographics: 49.9% White, 29.0% Black, 29.0% Hispanic, 6.8% Asian
    /// </remarks>
    public static CityDemographics Chicago => DefaultProvider.Cities.First(c => c.Name == "Chicago");

    /// <summary>
    /// Houston, Texas (Population: 2,304,580)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 770
    /// - Area Codes: 713, 281, 832
    /// - Demographics: 57.5% White, 22.9% Black, 44.3% Hispanic, 7.4% Asian
    /// </remarks>
    public static CityDemographics Houston => DefaultProvider.Cities.First(c => c.Name == "Houston");

    /// <summary>
    /// Phoenix, Arizona (Population: 1,680,992)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 850
    /// - Area Codes: 602, 480, 623
    /// - Demographics: 71.6% White, 7.1% Black, 42.8% Hispanic, 3.8% Asian
    /// </remarks>
    public static CityDemographics Phoenix => DefaultProvider.Cities.First(c => c.Name == "Phoenix");

    /// <summary>
    /// Philadelphia, Pennsylvania (Population: 1,603,797)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 191
    /// - Area Codes: 215, 267
    /// - Demographics: 41.0% White, 42.1% Black, 15.1% Hispanic, 7.6% Asian
    /// </remarks>
    public static CityDemographics Philadelphia => DefaultProvider.Cities.First(c => c.Name == "Philadelphia");

    /// <summary>
    /// San Antonio, Texas (Population: 1,547,253)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 782
    /// - Area Codes: 210, 726
    /// - Demographics: 80.2% White, 6.9% Black, 64.1% Hispanic, 2.9% Asian
    /// </remarks>
    public static CityDemographics SanAntonio => DefaultProvider.Cities.First(c => c.Name == "San Antonio");

    /// <summary>
    /// San Diego, California (Population: 1,423,851)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 921
    /// - Area Codes: 619, 858
    /// - Demographics: 65.0% White, 6.2% Black, 30.1% Hispanic, 17.0% Asian
    /// </remarks>
    public static CityDemographics SanDiego => DefaultProvider.Cities.First(c => c.Name == "San Diego");

    /// <summary>
    /// Dallas, Texas (Population: 1,343,573)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 752
    /// - Area Codes: 214, 469, 972
    /// - Demographics: 61.3% White, 24.0% Black, 41.6% Hispanic, 3.9% Asian
    /// </remarks>
    public static CityDemographics Dallas => DefaultProvider.Cities.First(c => c.Name == "Dallas");

    /// <summary>
    /// Seattle, Washington (Population: 737,015)
    /// </summary>
    /// <remarks>
    /// - Zip Code Prefix: 981
    /// - Area Code: 206
    /// - Demographics: 65.7% White, 7.1% Black, 7.1% Hispanic, 16.3% Asian
    /// </remarks>
    public static CityDemographics Seattle => DefaultProvider.Cities.First(c => c.Name == "Seattle");

    /// <summary>
    /// Gets all available cities.
    /// </summary>
    public static IReadOnlyList<CityDemographics> All => DefaultProvider.Cities;
}
