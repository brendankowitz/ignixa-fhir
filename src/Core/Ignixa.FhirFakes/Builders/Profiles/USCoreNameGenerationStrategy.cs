// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Name generation strategy for US Core Patient Profile.
/// </summary>
/// <remarks>
/// Generates names based on US Census ethnicity categories using the LocalBasedNameGenerator.
/// Maps US Census ethnicity categories to appropriate Bogus locales for culturally appropriate names.
/// If ethnicity is not provided in the profile attributes, defaults to "White" for backward compatibility.
///
/// Ethnicity to Locale Mapping:
/// - White -> en (English)
/// - Hispanic -> es_MX (Mexican Spanish)
/// - Black -> en (English - fallback)
/// - Asian-Chinese -> zh_CN (Simplified Chinese)
/// - Asian-Indian -> en_IND (Indian English)
/// - Asian-Vietnamese -> vi (Vietnamese)
/// - Asian-Korean -> ko (Korean)
/// - Asian-Japanese -> ja (Japanese)
/// - Arab -> ar (Arabic)
/// </remarks>
public sealed class USCoreNameGenerationStrategy : INameGenerationStrategy
{
    private readonly LocalBasedNameGenerator _nameGenerator;

    /// <summary>
    /// Maps US Census ethnicity categories to Bogus locale codes.
    /// </summary>
    private static readonly Dictionary<string, string> EthnicityToLocale = new()
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
    /// Singleton instance of the US Core name generation strategy.
    /// </summary>
    public static readonly USCoreNameGenerationStrategy Instance = new(new LocalBasedNameGenerator());

    /// <summary>
    /// Initializes a new instance of the <see cref="USCoreNameGenerationStrategy"/> class.
    /// </summary>
    /// <param name="nameGenerator">The locale-based name generator to use</param>
    public USCoreNameGenerationStrategy(LocalBasedNameGenerator nameGenerator)
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
        // Extract ethnicity from profile attributes, default to "White" for backward compatibility
        var ethnicity = profileAttributes.TryGetValue(USCorePatientProfile.UsCoreRaceAttribute, out var ethnicityValue)
            && ethnicityValue is string ethnicityString
            && !string.IsNullOrEmpty(ethnicityString)
            ? ethnicityString
            : USCorePatientProfile.Race.White;

        // Map ethnicity to Bogus locale
        var locale = EthnicityToLocale.GetValueOrDefault(ethnicity, "en");

        // Delegate to LocalBasedNameGenerator with locale
        return _nameGenerator.GenerateName(locale, gender);
    }
}
