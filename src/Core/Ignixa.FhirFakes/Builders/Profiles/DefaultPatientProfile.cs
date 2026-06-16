// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Default patient profile with no country-specific extensions.
/// </summary>
/// <remarks>
/// Used for:
/// - Patients from countries without a specific profile implementation
/// - Simple test scenarios where profile-specific extensions are not needed
/// - Base FHIR Patient resources without national customizations
///
/// This profile only adds BMI extension if provided, with no country-specific extensions.
/// </remarks>
public sealed class DefaultPatientProfile : IPatientProfile
{
    /// <summary>
    /// Singleton instance of the default profile.
    /// </summary>
    public static readonly DefaultPatientProfile Instance = new();

    /// <inheritdoc />
    public INameGenerationStrategy NameGenerationStrategy => DefaultNameGenerationStrategy.Instance;

    /// <inheritdoc />
    public string ProfileUrl => "http://hl7.org/fhir/StructureDefinition/Patient";

    /// <inheritdoc />
    public string CountryCode => string.Empty;

    /// <inheritdoc />
    public IEnumerable<string> RequiredAttributes => [];

    /// <inheritdoc />
    public IEnumerable<JsonObject> BuildExtensions(
        IReadOnlyDictionary<string, object> attributes,
        decimal? bmi)
    {
        // Only add BMI extension if provided
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
        // Default profile has no specific identifiers
        return null;
    }

    /// <inheritdoc />
    public bool ValidateAttributes(IReadOnlyDictionary<string, object> attributes)
    {
        // No required attributes for default profile
        return true;
    }

    /// <inheritdoc />
    public Dictionary<string, object> SampleProfileAttributes(CityDemographics city, Bogus.Randomizer randomizer)
    {
        // Default profile has no specific attributes to sample
        return new Dictionary<string, object>();
    }
}
