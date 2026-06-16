// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// US Core Patient Profile implementation.
/// </summary>
/// <remarks>
/// Implements US Core FHIR Patient profile with:
/// - Race extension (http://hl7.org/fhir/us/core/StructureDefinition/us-core-race)
/// - Ethnicity extension (http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity)
/// - BMI extension (if provided)
///
/// Required attributes from demographics:
/// - "ethnicity": string value like "White", "Black", "Hispanic", "Asian"
/// - "hispanicOrigin" (optional): string value like "Hispanic or Latino", "Not Hispanic or Latino"
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class USCorePatientProfile : IPatientProfile
{
    /// <summary>
    /// Singleton instance of the US Core profile.
    /// </summary>
    public static readonly USCorePatientProfile Instance = new();

    /// <summary>
    /// Attribute key for ethnicity distribution in city attributes.
    /// </summary>
    public const string EthnicityDistributionKey = "ethnicityDistribution";

    /// <summary>
    /// Attribute key for ethnicity (the demographic category e.g., "White", "Black", "Asian", "Hispanic").
    /// </summary>
    public const string UsCoreRaceAttribute = "race";

    /// <summary>
    /// Attribute key for Hispanic origin (e.g., "Hispanic or Latino", "Not Hispanic or Latino").
    /// </summary>
    public const string UsCoreEthnicityAttribute = "ethnicity";

    /// <summary>
    /// US Census race categories (maps to FHIR us-core-race extension).
    /// Used with PatientBuilder.WithAttribute(USCorePatientProfile.UsCoreRaceAttribute, ...) and ethnic name generation.
    /// </summary>
    public static class Race
    {
        /// <summary>White race category.</summary>
        public const string White = "White";

        /// <summary>Black or African American race category.</summary>
        public const string Black = "Black";

        /// <summary>Hispanic or Latino race category.</summary>
        public const string Hispanic = "Hispanic";

        /// <summary>Asian race category (general).</summary>
        public const string Asian = "Asian";

        /// <summary>Asian - Chinese race.</summary>
        public const string AsianChinese = "Asian-Chinese";

        /// <summary>Asian - Indian race.</summary>
        public const string AsianIndian = "Asian-Indian";

        /// <summary>Asian - Filipino race.</summary>
        public const string AsianFilipino = "Asian-Filipino";

        /// <summary>Asian - Vietnamese race.</summary>
        public const string AsianVietnamese = "Asian-Vietnamese";

        /// <summary>Asian - Korean race.</summary>
        public const string AsianKorean = "Asian-Korean";

        /// <summary>Asian - Japanese race.</summary>
        public const string AsianJapanese = "Asian-Japanese";

        /// <summary>Native American or Alaska Native race category.</summary>
        public const string NativeAmerican = "NativeAmerican";

        /// <summary>Native Hawaiian or Other Pacific Islander race category.</summary>
        public const string PacificIslander = "PacificIslander";

        /// <summary>Arab race category.</summary>
        public const string Arab = "Arab";

        /// <summary>Other race category.</summary>
        public const string Other = "Other";
    }

    /// <summary>
    /// US Core Hispanic origin categories (maps to FHIR us-core-ethnicity extension).
    /// </summary>
    public static class Ethnicity
    {
        /// <summary>Hispanic or Latino origin.</summary>
        public const string HispanicOrLatino = "Hispanic or Latino";

        /// <summary>Not Hispanic or Latino origin.</summary>
        public const string NotHispanicOrLatino = "Not Hispanic or Latino";
    }

    /// <inheritdoc />
    public INameGenerationStrategy NameGenerationStrategy => USCoreNameGenerationStrategy.Instance;

    /// <inheritdoc />
    public string ProfileUrl => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient";

    /// <inheritdoc />
    public string CountryCode => "US";

    /// <inheritdoc />
    public IEnumerable<string> RequiredAttributes => [UsCoreRaceAttribute];

    /// <inheritdoc />
    public IEnumerable<JsonObject> BuildExtensions(
        IReadOnlyDictionary<string, object> attributes,
        decimal? bmi)
    {
        // US Core Race Extension (FHIR extension URL is "us-core-race" per spec)
        if (attributes.TryGetValue(UsCoreRaceAttribute, out var ethnicityValue) && ethnicityValue is string ethnicity && !string.IsNullOrEmpty(ethnicity))
        {
            yield return new JsonObject
            {
                ["url"] = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race",
                ["extension"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["url"] = "text",
                        ["valueString"] = ethnicity
                    }
                }
            };
        }

        // US Core Ethnicity Extension (for Hispanic origin)
        if (attributes.TryGetValue(UsCoreEthnicityAttribute, out var hispanicOriginValue) && hispanicOriginValue is string hispanicOrigin && !string.IsNullOrEmpty(hispanicOrigin))
        {
            yield return new JsonObject
            {
                ["url"] = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity",
                ["extension"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["url"] = "text",
                        ["valueString"] = hispanicOrigin
                    }
                }
            };
        }

        // BMI Extension (if provided)
        if (bmi.HasValue)
        {
            yield return new JsonObject
            {
                ["url"] = "http://ignixa.dev/StructureDefinition/patient-bmi",
                ["valueDecimal"] = bmi.Value
            };
        }
    }

    /// <inheritdoc />
    public IEnumerable<JsonObject>? BuildIdentifiers(IReadOnlyDictionary<string, object> attributes)
    {
        // US Core does not require specific identifiers in this implementation
        // Could be extended to generate SSN-like identifiers if needed
        return null;
    }

    /// <inheritdoc />
    public bool ValidateAttributes(IReadOnlyDictionary<string, object> attributes)
    {
        // Ethnicity is required for US Core
        return attributes.ContainsKey(UsCoreRaceAttribute);
    }

    /// <inheritdoc />
    public Dictionary<string, object> SampleProfileAttributes(CityDemographics city, Bogus.Randomizer randomizer)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(randomizer);

        var attributes = new Dictionary<string, object>();

        // Sample ethnicity from city's ethnicity distribution
        attributes[UsCoreRaceAttribute] = SampleEthnicity(city, randomizer);

        return attributes;
    }

    /// <summary>
    /// Samples an ethnicity based on the city's ethnicity distribution.
    /// </summary>
    /// <param name="city">City demographics containing ethnicity distribution</param>
    /// <param name="randomizer">The seeded randomizer used for weighted sampling</param>
    /// <returns>Ethnicity value (e.g., "White", "Black", "Hispanic", "Asian")</returns>
    /// <remarks>
    /// Uses weighted random sampling based on the city's ethnicity distribution from Attributes.
    /// If no distribution is provided, returns a default US ethnicity ("White").
    /// If the distribution probabilities don't sum to 1.0, falls back to the first key.
    /// </remarks>
    private static string SampleEthnicity(CityDemographics city, Bogus.Randomizer randomizer)
    {
        // Try to get ethnicity distribution from city attributes
        if (city.Attributes.TryGetValue(EthnicityDistributionKey, out var data)
            && data is Dictionary<string, double> distribution
            && distribution.Count > 0)
        {
            var random = randomizer.Double();
            var cumulative = 0.0;

            foreach (var (ethnicity, probability) in distribution)
            {
                cumulative += probability;
                if (random < cumulative)
                {
                    return ethnicity;
                }
            }

            // Fallback if distribution doesn't sum to 1.0
            return distribution.Keys.First();
        }

        // Fallback to default US ethnicity if no distribution provided
        return Race.White;
    }
}
