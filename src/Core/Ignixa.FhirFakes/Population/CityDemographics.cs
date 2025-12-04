// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Builders.Profiles;

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Demographic profile for a city including population and age distribution.
/// </summary>
/// <param name="Name">City name</param>
/// <param name="State">State/Province/Region name</param>
/// <param name="Country">Country code (e.g., "US", "AU", "NL"). Used to select the appropriate FHIR profile.</param>
/// <param name="Population">Total population</param>
/// <param name="AgeGroupDistribution">Age group distribution (e.g., "0-17": 0.22, "18-44": 0.35)</param>
/// <param name="MaleRatio">Proportion of population that is male (e.g., 0.49 = 49% male, 51% female)</param>
/// <param name="ZipCodePrefix">Zip/Postal code prefix range for the city (e.g., "021" for Boston 02101-02298, "3000" for Melbourne)</param>
/// <param name="AreaCodes">Phone area codes for the city (e.g., ["617", "857"] for Boston, ["03"] for Melbourne)</param>
/// <param name="Attributes">Optional profile-specific attributes for this city (e.g., ethnicity distribution for US, indigenous status distribution for AU)</param>
public record CityDemographics(
    string Name,
    string State,
    string Country,
    int Population,
    Dictionary<string, double> AgeGroupDistribution,
    double MaleRatio,
    string ZipCodePrefix,
    IReadOnlyList<string> AreaCodes,
    IReadOnlyDictionary<string, object>? Attributes = null
)
{
    /// <summary>
    /// Profile-specific attributes for this city.
    /// </summary>
    /// <remarks>
    /// Stores profile-specific demographic data. Common keys:
    /// - "ethnicityDistribution": Dictionary&lt;string, double&gt; for US cities (US Core profile)
    /// - "indigenousStatusDistribution": Dictionary&lt;string, double&gt; for AU cities (AU Base profile)
    ///
    /// Not all cities will have all attributes - profiles should handle missing data gracefully.
    /// </remarks>
    public IReadOnlyDictionary<string, object> Attributes { get; } = Attributes ?? new Dictionary<string, object>();

    /// <summary>
    /// Gets whether this city is in Australia.
    /// </summary>
    public bool IsAustralian => Country == "AU";

    /// <summary>
    /// Gets whether this city is in the United States.
    /// </summary>
    public bool IsUSA => Country == "US";

    /// <summary>
    /// Gets the FHIR patient profile URL for this city based on country.
    /// </summary>
    public string ProfileUrl => GetProfile().ProfileUrl;

    /// <summary>
    /// Gets the patient profile for this city based on country.
    /// </summary>
    public IPatientProfile GetProfile() => PatientProfileFactory.GetProfile(Country);
}
