// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Strategy interface for generating culturally appropriate patient names.
/// </summary>
/// <remarks>
/// This decouples name generation from US-specific racial categories, allowing each profile
/// to define how names are generated based on profile-specific attributes (like race for US)
/// or country-specific considerations (like country code for international profiles).
/// </remarks>
public interface INameGenerationStrategy
{
    /// <summary>
    /// Generates a culturally appropriate name based on gender, profile attributes, and country code.
    /// </summary>
    /// <param name="gender">The patient's gender (e.g., "male", "female", "other", "unknown")</param>
    /// <param name="profileAttributes">Profile-specific attributes (e.g., race for US Core, indigenous status for AU Base)</param>
    /// <param name="countryCode">The country code from the profile (e.g., "US", "AU", "NL")</param>
    /// <param name="randomizer">Seeded randomizer used to make name generation deterministic</param>
    /// <returns>A tuple containing the generated given (first) name and family (last) name</returns>
    (string GivenName, string FamilyName) GenerateName(
        string gender,
        IReadOnlyDictionary<string, object> profileAttributes,
        string? countryCode,
        Bogus.Randomizer randomizer);
}
