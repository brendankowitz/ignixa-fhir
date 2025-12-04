// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Factory for obtaining the appropriate patient profile based on country code.
/// </summary>
/// <remarks>
/// Centralizes profile selection logic to avoid coupling PatientBuilder
/// directly to specific profile implementations.
/// </remarks>
public static class PatientProfileFactory
{
    private static readonly Dictionary<string, IPatientProfile> ProfilesByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = USCorePatientProfile.Instance,
        ["AU"] = AUBasePatientProfile.Instance,
    };

    /// <summary>
    /// Gets the appropriate profile for a country code.
    /// </summary>
    /// <param name="countryCode">Two-letter country code (e.g., "US", "AU", "NL")</param>
    /// <returns>The profile for the country, or DefaultPatientProfile if not found</returns>
    public static IPatientProfile GetProfile(string? countryCode)
    {
        if (string.IsNullOrEmpty(countryCode))
        {
            return DefaultPatientProfile.Instance;
        }

        return ProfilesByCountry.TryGetValue(countryCode, out var profile)
            ? profile
            : DefaultPatientProfile.Instance;
    }

    /// <summary>
    /// Gets the US Core profile.
    /// </summary>
    public static IPatientProfile USCore => USCorePatientProfile.Instance;

    /// <summary>
    /// Gets the AU Base profile.
    /// </summary>
    public static IPatientProfile AUBase => AUBasePatientProfile.Instance;

    /// <summary>
    /// Gets the default profile (no country-specific extensions).
    /// </summary>
    public static IPatientProfile Default => DefaultPatientProfile.Instance;

    /// <summary>
    /// Registers a custom profile for a country code.
    /// </summary>
    /// <param name="countryCode">Two-letter country code</param>
    /// <param name="profile">Profile implementation</param>
    public static void RegisterProfile(string countryCode, IPatientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(countryCode);
        ArgumentNullException.ThrowIfNull(profile);

        ProfilesByCountry[countryCode] = profile;
    }

    /// <summary>
    /// Gets all registered profiles.
    /// </summary>
    public static IReadOnlyDictionary<string, IPatientProfile> RegisteredProfiles => ProfilesByCountry;
}
