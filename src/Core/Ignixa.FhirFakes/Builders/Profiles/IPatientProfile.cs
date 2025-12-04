// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Interface for FHIR patient profiles that define country/region-specific extensions and identifiers.
/// </summary>
/// <remarks>
/// Each implementation represents a national or regional patient profile (e.g., US Core, AU Base).
/// The profile determines:
/// - What extensions are added to the patient (e.g., race, ethnicity, indigenous status)
/// - What attributes are required from demographics sampling
/// - What identifiers are generated (e.g., SSN, Medicare number)
/// - How patient names are generated (via the NameGenerationStrategy)
///
/// This design keeps profile-specific data in an Attributes dictionary rather than
/// first-classing any particular country's demographics.
/// </remarks>
public interface IPatientProfile
{
    /// <summary>
    /// Gets the name generation strategy for this profile.
    /// </summary>
    /// <remarks>
    /// The strategy determines how patient names are generated based on profile-specific
    /// attributes and country code. For example:
    /// - US Core uses race from profile attributes to generate ethnically appropriate names
    /// - AU Base uses English locale names appropriate for Australia
    /// - Default uses country code to determine appropriate name localization
    /// </remarks>
    INameGenerationStrategy NameGenerationStrategy { get; }

    /// <summary>
    /// Gets the canonical URL of this profile (e.g., "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient").
    /// </summary>
    string ProfileUrl { get; }

    /// <summary>
    /// Gets the country code this profile applies to (e.g., "US", "AU", "NL").
    /// </summary>
    string CountryCode { get; }

    /// <summary>
    /// Gets the attribute keys this profile needs from demographics sampling.
    /// </summary>
    /// <remarks>
    /// These keys are used by DemographicsDataProvider to know what profile-specific
    /// attributes to sample. For example:
    /// - US Core: "race", "ethnicity"
    /// - AU Base: "indigenousStatus"
    /// </remarks>
    IEnumerable<string> RequiredAttributes { get; }

    /// <summary>
    /// Builds profile-specific extensions from the provided attributes.
    /// </summary>
    /// <param name="attributes">Profile-specific attributes sampled from demographics</param>
    /// <param name="bmi">Optional BMI value (included in extensions if provided)</param>
    /// <returns>Collection of extension JsonObjects to add to the patient resource</returns>
    IEnumerable<JsonObject> BuildExtensions(
        IReadOnlyDictionary<string, object> attributes,
        decimal? bmi);

    /// <summary>
    /// Builds profile-specific identifiers from the provided attributes.
    /// </summary>
    /// <param name="attributes">Profile-specific attributes sampled from demographics</param>
    /// <returns>Collection of identifier JsonObjects to add to the patient resource, or null if none</returns>
    IEnumerable<JsonObject>? BuildIdentifiers(IReadOnlyDictionary<string, object> attributes);

    /// <summary>
    /// Validates that all required attributes are present.
    /// </summary>
    /// <param name="attributes">Attributes to validate</param>
    /// <returns>True if all required attributes are present, false otherwise</returns>
    bool ValidateAttributes(IReadOnlyDictionary<string, object> attributes);

    /// <summary>
    /// Samples profile-specific attributes from city demographics.
    /// </summary>
    /// <param name="city">The city demographics to sample from</param>
    /// <returns>Dictionary of attribute key-value pairs for this profile</returns>
    /// <remarks>
    /// Each profile implementation is responsible for sampling its own required attributes.
    /// For example:
    /// - US Core samples ethnicity from the city's ethnicity distribution
    /// - AU Base samples indigenous status from the city's indigenous status distribution
    /// - Default profile returns an empty dictionary
    /// </remarks>
    Dictionary<string, object> SampleProfileAttributes(CityDemographics city);
}
