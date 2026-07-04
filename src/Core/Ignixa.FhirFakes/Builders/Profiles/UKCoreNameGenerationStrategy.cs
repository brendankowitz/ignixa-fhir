// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Name generation strategy for UK Core Patient Profile.
/// </summary>
/// <remarks>
/// Generates culturally appropriate British names using the LocalBasedNameGenerator.
/// Uses the English (Great Britain) locale ("en_GB"), which reflects the majority
/// population in the United Kingdom. The strategy does NOT use US race categories for name generation.
/// </remarks>
public sealed class UKCoreNameGenerationStrategy : INameGenerationStrategy
{
    private readonly LocalBasedNameGenerator _nameGenerator;

    /// <summary>
    /// Singleton instance of the UK Core name generation strategy.
    /// </summary>
    public static readonly UKCoreNameGenerationStrategy Instance = new(new LocalBasedNameGenerator());

    /// <summary>
    /// Initializes a new instance of the <see cref="UKCoreNameGenerationStrategy"/> class.
    /// </summary>
    /// <param name="nameGenerator">The locale-based name generator to use</param>
    public UKCoreNameGenerationStrategy(LocalBasedNameGenerator nameGenerator)
    {
        ArgumentNullException.ThrowIfNull(nameGenerator);
        _nameGenerator = nameGenerator;
    }

    /// <inheritdoc />
    public (string GivenName, string FamilyName) GenerateName(
        string gender,
        IReadOnlyDictionary<string, object> profileAttributes,
        string? countryCode,
        Bogus.Randomizer randomizer)
    {
        // For UK patients, use the British English locale directly.
        // This generates appropriate British names which represent
        // the majority population demographic in the United Kingdom.
        return _nameGenerator.GenerateName("en_GB", gender, randomizer);
    }
}
