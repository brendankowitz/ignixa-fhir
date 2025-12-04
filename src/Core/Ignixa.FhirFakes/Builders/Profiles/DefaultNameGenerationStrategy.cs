// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Default name generation strategy for patients without a country-specific profile.
/// </summary>
/// <remarks>
/// Generates culturally appropriate names based on the country code when available.
/// Falls back to neutral English names when no country code is specified.
/// Maps country codes directly to Bogus locales for culturally appropriate name generation.
/// </remarks>
public sealed class DefaultNameGenerationStrategy : INameGenerationStrategy
{
    private readonly LocalBasedNameGenerator _nameGenerator;

    /// <summary>
    /// Singleton instance of the default name generation strategy.
    /// </summary>
    public static readonly DefaultNameGenerationStrategy Instance = new(new LocalBasedNameGenerator());

    /// <summary>
    /// Mapping from country codes to Bogus locale codes.
    /// </summary>
    /// <remarks>
    /// Maps ISO 3166-1 alpha-2 country codes directly to Bogus locales for
    /// culturally appropriate name generation.
    /// </remarks>
    private static readonly Dictionary<string, string> CountryToLocaleMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // European countries
        ["NL"] = "nl",        // Dutch
        ["DE"] = "de",        // German
        ["FR"] = "fr",        // French
        ["GB"] = "en",        // English
        ["UK"] = "en",        // English
        ["IE"] = "en",        // English (Ireland)
        ["BE"] = "nl",        // Dutch (Belgium)
        ["CH"] = "de",        // German (Switzerland)
        ["AT"] = "de",        // German (Austria)
        ["IT"] = "it",        // Italian
        ["ES"] = "es",        // Spanish
        ["PT"] = "pt_PT",     // Portuguese
        ["SE"] = "sv",        // Swedish
        ["NO"] = "nb_NO",     // Norwegian
        ["DK"] = "da",        // Danish
        ["FI"] = "fi",        // Finnish
        ["PL"] = "pl",        // Polish

        // Americas
        ["MX"] = "es_MX",     // Mexican Spanish
        ["BR"] = "pt_BR",     // Brazilian Portuguese
        ["AR"] = "es",        // Spanish (Argentina)
        ["CL"] = "es",        // Spanish (Chile)
        ["CO"] = "es",        // Spanish (Colombia)
        ["CA"] = "en",        // English (Canada)

        // Asia
        ["CN"] = "zh_CN",     // Simplified Chinese
        ["JP"] = "ja",        // Japanese
        ["KR"] = "ko",        // Korean
        ["IN"] = "en_IND",    // Indian English
        ["VN"] = "vi",        // Vietnamese
        ["PH"] = "en",        // English (Philippines)

        // Middle East
        ["SA"] = "ar",        // Arabic (Saudi Arabia)
        ["AE"] = "ar",        // Arabic (UAE)
        ["EG"] = "ar",        // Arabic (Egypt)
        ["IL"] = "en",        // English (Israel) - Bogus doesn't have Hebrew

        // Oceania (AU is handled by AUBaseNameGenerationStrategy)
        ["NZ"] = "en",        // English (New Zealand)

        // Africa
        ["ZA"] = "en",        // English (South Africa)
        ["NG"] = "en",        // English (Nigeria)
        ["KE"] = "en",        // English (Kenya)
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultNameGenerationStrategy"/> class.
    /// </summary>
    /// <param name="nameGenerator">The locale-based name generator to use</param>
    public DefaultNameGenerationStrategy(LocalBasedNameGenerator nameGenerator)
    {
        ArgumentNullException.ThrowIfNull(nameGenerator);
        _nameGenerator = nameGenerator;
    }

    /// <inheritdoc />
    public (string GivenName, string FamilyName) GenerateName(
        string gender,
        IReadOnlyDictionary<string, object> profileAttributes,
        string? countryCode)
    {
        // Map country code to Bogus locale
        var locale = GetLocaleForCountry(countryCode);

        // Delegate to LocalBasedNameGenerator with locale
        return _nameGenerator.GenerateName(locale, gender);
    }

    /// <summary>
    /// Gets the appropriate Bogus locale for name generation based on country code.
    /// </summary>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code (e.g., "NL", "JP", "BR")</param>
    /// <returns>The Bogus locale code to use with LocalBasedNameGenerator</returns>
    private static string GetLocaleForCountry(string? countryCode)
    {
        if (string.IsNullOrEmpty(countryCode))
        {
            // Default to neutral English names
            return "en";
        }

        return CountryToLocaleMapping.GetValueOrDefault(countryCode, "en");
    }
}
