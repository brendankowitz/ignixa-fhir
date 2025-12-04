// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Population;

namespace Ignixa.FhirFakes.Builders.Profiles;

/// <summary>
/// Name generation strategy for Australian Base Patient Profile.
/// </summary>
/// <remarks>
/// Generates culturally appropriate Australian names using the LocalBasedNameGenerator.
/// Uses English locale ("en") as the default for Australian names, which reflects the majority
/// population in Australia. The strategy does NOT use US race categories for name generation.
/// </remarks>
public sealed class AUBaseNameGenerationStrategy : INameGenerationStrategy
{
    private readonly LocalBasedNameGenerator _nameGenerator;

    /// <summary>
    /// Singleton instance of the AU Base name generation strategy.
    /// </summary>
    public static readonly AUBaseNameGenerationStrategy Instance = new(new LocalBasedNameGenerator());

    /// <summary>
    /// Initializes a new instance of the <see cref="AUBaseNameGenerationStrategy"/> class.
    /// </summary>
    /// <param name="nameGenerator">The locale-based name generator to use</param>
    public AUBaseNameGenerationStrategy(LocalBasedNameGenerator nameGenerator)
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
        // For Australian patients, use English locale directly
        // This generates appropriate Anglo-Australian names which represent
        // the majority population demographic in Australia
        return _nameGenerator.GenerateName("en_AU", gender);
    }
}
