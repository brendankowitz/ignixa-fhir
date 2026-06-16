// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Generates culturally appropriate names based on Bogus locale.
/// </summary>
/// <remarks>
/// Uses Bogus's built-in locale support for realistic, culturally appropriate name generation.
/// Supports 40+ locales out of the box without custom name lists.
///
/// Common Bogus Locales:
/// - "en" - English
/// - "es_MX" - Mexican Spanish
/// - "zh_CN" - Simplified Chinese
/// - "en_IND" - Indian English
/// - "vi" - Vietnamese
/// - "ko" - Korean
/// - "ja" - Japanese
/// - "ar" - Arabic
/// - "de" - German
/// - "fr" - French
/// - "nl" - Dutch
/// - "pt_BR" - Brazilian Portuguese
/// </remarks>
public class LocalBasedNameGenerator
{
    /// <summary>
    /// Generates a culturally appropriate name based on Bogus locale and gender.
    /// </summary>
    /// <param name="locale">Bogus locale code (e.g., "en", "es_MX", "zh_CN", "ja")</param>
    /// <param name="gender">Gender ("male" or "female")</param>
    /// <param name="randomizer">Seeded randomizer used to make name generation deterministic</param>
    /// <returns>Tuple of (FirstName, LastName)</returns>
    /// <example>
    /// var generator = new LocalBasedNameGenerator();
    /// var (first, last) = generator.GenerateName("es_MX", "female", new Bogus.Randomizer(42));
    /// // Result: ("Maria", "Garcia")
    /// </example>
    public (string FirstName, string LastName) GenerateName(string locale, string gender, Bogus.Randomizer randomizer)
    {
        var faker = new Faker(locale) { Random = randomizer };

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
