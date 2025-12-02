// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Generates culturally appropriate names based on race/ethnicity using Bogus locales.
/// </summary>
/// <remarks>
/// Uses Bogus's built-in locale support for realistic, culturally appropriate name generation.
/// Supports 40+ locales out of the box without custom name lists.
///
/// Race to Locale Mapping:
/// - White → en (English)
/// - Hispanic → es_MX (Mexican Spanish)
/// - Black → en (English - fallback)
/// - Asian-Chinese → zh_CN (Simplified Chinese)
/// - Asian-Indian → en_IND (Indian English)
/// - Asian-Vietnamese → vi (Vietnamese)
/// - Asian-Korean → ko (Korean)
/// - Asian-Japanese → ja (Japanese)
/// - Arab → ar (Arabic)
/// </remarks>
public class EthnicNameGenerator
{
    private readonly Dictionary<string, string> _raceToLocale = new()
    {
        ["White"] = "en",
        ["Hispanic"] = "es_MX",
        ["Black"] = "en",
        ["Asian"] = "zh_CN",
        ["Asian-Chinese"] = "zh_CN",
        ["Asian-Indian"] = "en_IND",
        ["Asian-Filipino"] = "en",
        ["Asian-Vietnamese"] = "vi",
        ["Asian-Korean"] = "ko",
        ["Asian-Japanese"] = "ja",
        ["NativeAmerican"] = "en",
        ["PacificIslander"] = "en",
        ["Arab"] = "ar",
        ["Other"] = "en"
    };

    /// <summary>
    /// Generates a culturally appropriate name based on race and gender.
    /// </summary>
    /// <param name="race">US Census race category (e.g., "White", "Hispanic", "Asian-Chinese")</param>
    /// <param name="gender">Gender ("male" or "female")</param>
    /// <returns>Tuple of (FirstName, LastName)</returns>
    /// <example>
    /// var generator = new EthnicNameGenerator();
    /// var (first, last) = generator.GenerateName("Hispanic", "female");
    /// // Result: ("María", "García")
    /// </example>
    public (string FirstName, string LastName) GenerateName(string race, string gender)
    {
        var locale = _raceToLocale.GetValueOrDefault(race, "en");
        var faker = new Faker(locale);

        var bogusGender = gender.ToUpperInvariant() switch
        {
            "MALE" => Bogus.DataSets.Name.Gender.Male,
            "FEMALE" => Bogus.DataSets.Name.Gender.Female,
            _ => (Bogus.DataSets.Name.Gender?)null
        };

        var firstName = bogusGender.HasValue
            ? faker.Name.FirstName(bogusGender.Value)
            : faker.Name.FirstName();

        var lastName = faker.Name.LastName();

        return (firstName, lastName);
    }
}
