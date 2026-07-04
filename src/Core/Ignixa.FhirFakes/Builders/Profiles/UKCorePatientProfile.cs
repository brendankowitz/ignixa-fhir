// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// UK Core Patient Profile implementation.
/// </summary>
/// <remarks>
/// Implements the HL7 UK Core FHIR Patient profile with:
/// - Ethnic Category extension (https://fhir.hl7.org.uk/StructureDefinition/Extension-UKCore-EthnicCategory)
/// - NHS Number identifier (https://fhir.nhs.uk/Id/nhs-number) with verification-status extension
/// - BMI extension (if provided)
///
/// Ethnic category codes follow the ONS 2011 census / NHS Data Dictionary "ETHNIC CATEGORY" data element.
///
/// Required attributes from demographics:
/// - "ethnicCategory": ONS ethnic category code ("A".."S", "Z")
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class UKCorePatientProfile : IPatientProfile
{
    /// <summary>
    /// Singleton instance of the UK Core profile.
    /// </summary>
    public static readonly UKCorePatientProfile Instance = new();

    /// <summary>
    /// Attribute key for ethnic category distribution in city attributes.
    /// </summary>
    public const string EthnicCategoryDistributionKey = "ethnicCategoryDistribution";

    /// <summary>
    /// Attribute key for ethnic category.
    /// </summary>
    public const string EthnicCategoryAttribute = "ethnicCategory";

    /// <summary>
    /// ONS / NHS Data Dictionary ethnic category codes (maps to FHIR Extension-UKCore-EthnicCategory).
    /// </summary>
    public static class EthnicCategory
    {
        /// <summary>British, White.</summary>
        public const string British = "A";

        /// <summary>Irish.</summary>
        public const string Irish = "B";

        /// <summary>Any other White background.</summary>
        public const string OtherWhite = "C";

        /// <summary>White and Black Caribbean.</summary>
        public const string WhiteAndBlackCaribbean = "D";

        /// <summary>White and Black African.</summary>
        public const string WhiteAndBlackAfrican = "E";

        /// <summary>White and Asian.</summary>
        public const string WhiteAndAsian = "F";

        /// <summary>Any other Mixed background.</summary>
        public const string OtherMixed = "G";

        /// <summary>Indian.</summary>
        public const string Indian = "H";

        /// <summary>Pakistani.</summary>
        public const string Pakistani = "J";

        /// <summary>Bangladeshi.</summary>
        public const string Bangladeshi = "K";

        /// <summary>Any other Asian background.</summary>
        public const string OtherAsian = "L";

        /// <summary>Caribbean.</summary>
        public const string Caribbean = "M";

        /// <summary>African.</summary>
        public const string African = "N";

        /// <summary>Any other Black background.</summary>
        public const string OtherBlack = "P";

        /// <summary>Chinese.</summary>
        public const string Chinese = "R";

        /// <summary>Any other ethnic group.</summary>
        public const string OtherEthnicGroup = "S";

        /// <summary>Not stated.</summary>
        public const string NotStated = "Z";
    }

    /// <summary>
    /// Ethnic category display values by code.
    /// </summary>
    private static readonly Dictionary<string, string> EthnicCategoryDisplay = new()
    {
        ["A"] = "British, White",
        ["B"] = "Irish",
        ["C"] = "Any other White background",
        ["D"] = "White and Black Caribbean",
        ["E"] = "White and Black African",
        ["F"] = "White and Asian",
        ["G"] = "Any other Mixed background",
        ["H"] = "Indian",
        ["J"] = "Pakistani",
        ["K"] = "Bangladeshi",
        ["L"] = "Any other Asian background",
        ["M"] = "Caribbean",
        ["N"] = "African",
        ["P"] = "Any other Black background",
        ["R"] = "Chinese",
        ["S"] = "Any other ethnic group",
        ["Z"] = "Not stated"
    };

    /// <inheritdoc />
    public INameGenerationStrategy NameGenerationStrategy => UKCoreNameGenerationStrategy.Instance;

    /// <inheritdoc />
    public string ProfileUrl => "https://fhir.hl7.org.uk/StructureDefinition/UKCore-Patient";

    /// <inheritdoc />
    public string CountryCode => "GB";

    /// <inheritdoc />
    public IEnumerable<string> RequiredAttributes => [EthnicCategoryAttribute];

    /// <inheritdoc />
    public IEnumerable<JsonObject> BuildExtensions(
        IReadOnlyDictionary<string, object> attributes,
        decimal? bmi)
    {
        // UK Core Ethnic Category Extension
        if (attributes.TryGetValue(EthnicCategoryAttribute, out var categoryValue) && categoryValue is string code && !string.IsNullOrEmpty(code))
        {
            var display = EthnicCategoryDisplay.TryGetValue(code, out var d) ? d : "Unknown";

            yield return new JsonObject
            {
                ["url"] = "https://fhir.hl7.org.uk/StructureDefinition/Extension-UKCore-EthnicCategory",
                ["valueCodeableConcept"] = new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = "https://fhir.hl7.org.uk/CodeSystem/UKCore-EthnicCategory",
                            ["code"] = code,
                            ["display"] = display
                        }
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
        yield return new JsonObject
        {
            ["system"] = "https://fhir.nhs.uk/Id/nhs-number",
            ["value"] = GenerateNhsNumber(),
            ["extension"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = "https://fhir.hl7.org.uk/StructureDefinition/Extension-UKCore-NHSNumberVerificationStatus",
                    ["valueCodeableConcept"] = new JsonObject
                    {
                        ["coding"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["system"] = "https://fhir.hl7.org.uk/CodeSystem/UKCore-NHSNumberVerificationStatus",
                                ["code"] = "01",
                                ["display"] = "Number present and verified"
                            }
                        }
                    }
                }
            }
        };
    }

    /// <inheritdoc />
    public bool ValidateAttributes(IReadOnlyDictionary<string, object> attributes)
    {
        // Ethnic category is required for UK Core
        return attributes.ContainsKey(EthnicCategoryAttribute);
    }

    /// <inheritdoc />
    public Dictionary<string, object> SampleProfileAttributes(CityDemographics city, Bogus.Randomizer randomizer)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(randomizer);

        var attributes = new Dictionary<string, object>();

        // Sample ethnic category from city's distribution
        attributes[EthnicCategoryAttribute] = SampleEthnicCategory(city, randomizer);

        return attributes;
    }

    /// <summary>
    /// Samples an ONS ethnic category code from the city's distribution.
    /// </summary>
    /// <param name="city">City demographics containing an ethnic category distribution</param>
    /// <param name="randomizer">The seeded randomizer used for weighted sampling</param>
    /// <returns>Ethnic category code ("A".."S", "Z")</returns>
    /// <remarks>
    /// Uses weighted random sampling based on the city's ethnic category distribution from Attributes.
    /// If no distribution is provided, falls back to <see cref="EthnicCategory.British"/>.
    /// If the distribution probabilities don't sum to 1.0, falls back to the first key.
    /// </remarks>
    private static string SampleEthnicCategory(CityDemographics city, Bogus.Randomizer randomizer)
    {
        if (city.Attributes.TryGetValue(EthnicCategoryDistributionKey, out var data)
            && data is Dictionary<string, double> distribution
            && distribution.Count > 0)
        {
            var random = randomizer.Double();
            var cumulative = 0.0;

            foreach (var (code, probability) in distribution)
            {
                cumulative += probability;
                if (random < cumulative)
                {
                    return code;
                }
            }

            // Fallback if distribution doesn't sum to 1.0
            return distribution.Keys.First();
        }

        // Fallback to majority UK ethnic category if no distribution provided
        return EthnicCategory.British;
    }

    /// <summary>
    /// Generates a valid 10-digit NHS Number (9-digit base plus a Modulus 11 check digit).
    /// </summary>
    /// <remarks>
    /// Standard NHS algorithm: each of the 9 base digits is multiplied by a weight of 10 down to 2,
    /// the products are summed, and the check digit is <c>11 - (sum mod 11)</c>. A result of 11 becomes
    /// 0; a result of 10 is invalid, so the base is regenerated.
    /// </remarks>
    private static string GenerateNhsNumber()
    {
        while (true)
        {
            var digits = new int[9];
            var sum = 0;

            for (var i = 0; i < 9; i++)
            {
                digits[i] = Random.Shared.Next(0, 10);
                sum += digits[i] * (10 - i);
            }

            var checkDigit = 11 - (sum % 11);

            if (checkDigit == 11)
            {
                checkDigit = 0;
            }
            else if (checkDigit == 10)
            {
                // Invalid check digit - regenerate the base.
                continue;
            }

            return string.Concat(digits.Select(d => d.ToString(CultureInfo.InvariantCulture)))
                + checkDigit.ToString(CultureInfo.InvariantCulture);
        }
    }
}
