// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Australian Base Patient Profile implementation.
/// </summary>
/// <remarks>
/// Implements AU Base FHIR Patient profile with:
/// - Indigenous Status extension (http://hl7.org.au/fhir/StructureDefinition/indigenous-status)
/// - BMI extension (if provided)
///
/// Indigenous Status codes (per ABS Standard):
/// - 1: Aboriginal but not Torres Strait Islander origin
/// - 2: Torres Strait Islander but not Aboriginal origin
/// - 3: Both Aboriginal and Torres Strait Islander origin
/// - 4: Neither Aboriginal nor Torres Strait Islander origin
/// - 9: Not stated/inadequately described
///
/// Required attributes from demographics:
/// - "indigenousStatus": string code "1", "2", "3", "4", or "9"
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class AUBasePatientProfile : IPatientProfile
{
    /// <summary>
    /// Singleton instance of the AU Base profile.
    /// </summary>
    public static readonly AUBasePatientProfile Instance = new();

    /// <summary>
    /// Attribute key for indigenous status distribution in city attributes.
    /// </summary>
    public const string IndigenousStatusDistributionKey = "indigenousStatusDistribution";

    /// <summary>
    /// Attribute key for indigenous status.
    /// </summary>
    public const string IndigenousStatusAttribute = "indigenousStatus";

    /// <summary>
    /// Indigenous status coding system URL.
    /// </summary>
    public const string IndigenousStatusSystem = "https://healthterminologies.gov.au/fhir/CodeSystem/australian-indigenous-status-1";

    /// <summary>
    /// Indigenous status display values by code.
    /// </summary>
    private static readonly Dictionary<string, string> IndigenousStatusDisplay = new()
    {
        ["1"] = "Aboriginal but not Torres Strait Islander origin",
        ["2"] = "Torres Strait Islander but not Aboriginal origin",
        ["3"] = "Both Aboriginal and Torres Strait Islander origin",
        ["4"] = "Neither Aboriginal nor Torres Strait Islander origin",
        ["9"] = "Not stated/inadequately described"
    };

    /// <inheritdoc />
    public INameGenerationStrategy NameGenerationStrategy => AUBaseNameGenerationStrategy.Instance;

    /// <inheritdoc />
    public string ProfileUrl => "http://hl7.org.au/fhir/StructureDefinition/au-patient";

    /// <inheritdoc />
    public string CountryCode => "AU";

    /// <inheritdoc />
    public IEnumerable<string> RequiredAttributes => [IndigenousStatusAttribute];

    /// <inheritdoc />
    public IEnumerable<JsonObject> BuildExtensions(
        IReadOnlyDictionary<string, object> attributes,
        decimal? bmi)
    {
        // AU Base Indigenous Status Extension
        if (attributes.TryGetValue(IndigenousStatusAttribute, out var statusValue) && statusValue is string status && !string.IsNullOrEmpty(status))
        {
            var display = IndigenousStatusDisplay.TryGetValue(status, out var d) ? d : "Unknown";

            yield return new JsonObject
            {
                ["url"] = "http://hl7.org.au/fhir/StructureDefinition/indigenous-status",
                ["valueCoding"] = new JsonObject
                {
                    ["system"] = IndigenousStatusSystem,
                    ["code"] = status,
                    ["display"] = display
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
        // AU Base could include Medicare number, IHI, DVA number, etc.
        // Not implemented in this initial version
        return null;
    }

    /// <inheritdoc />
    public bool ValidateAttributes(IReadOnlyDictionary<string, object> attributes)
    {
        // Indigenous status is required for AU Base profile
        return attributes.ContainsKey(IndigenousStatusAttribute);
    }

    /// <inheritdoc />
    public Dictionary<string, object> SampleProfileAttributes(CityDemographics city)
    {
        ArgumentNullException.ThrowIfNull(city);

        var attributes = new Dictionary<string, object>();

        // Sample indigenous status from city's distribution
        attributes[IndigenousStatusAttribute] = SampleIndigenousStatus(city);

        return attributes;
    }

    /// <summary>
    /// Samples an Australian Indigenous Status code from the city's distribution.
    /// </summary>
    /// <param name="city">Australian city demographics</param>
    /// <returns>Indigenous status code ("1", "2", "3", "4", or "9")</returns>
    /// <remarks>
    /// Uses weighted random sampling based on the city's indigenous status distribution from Attributes.
    /// If no distribution is provided, falls back to national Australian demographics (approx 3.3% indigenous).
    ///
    /// Codes per ABS Standard:
    /// - 1: Aboriginal but not Torres Strait Islander
    /// - 2: Torres Strait Islander but not Aboriginal
    /// - 3: Both Aboriginal and Torres Strait Islander
    /// - 4: Neither Aboriginal nor Torres Strait Islander
    /// - 9: Not stated/inadequately described
    /// </remarks>
    private static string SampleIndigenousStatus(CityDemographics city)
    {
        // Try to get indigenous status distribution from city attributes
        if (city.Attributes.TryGetValue(IndigenousStatusDistributionKey, out var data)
            && data is Dictionary<string, double> distribution
            && distribution.Count > 0)
        {
            var random = Random.Shared.NextDouble();
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
            return distribution.Keys.Last();
        }

        // Fall back to national Australian demographics (approx 3.3% indigenous)
        var randomValue = Random.Shared.NextDouble();

        if (randomValue < 0.028)
            return "1"; // Aboriginal but not Torres Strait Islander
        else if (randomValue < 0.030)
            return "2"; // Torres Strait Islander but not Aboriginal
        else if (randomValue < 0.032)
            return "3"; // Both Aboriginal and Torres Strait Islander
        else
            return "4"; // Neither Aboriginal nor Torres Strait Islander
    }
}
