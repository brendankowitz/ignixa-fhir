// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Population;
using Ignixa.Abstractions;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Factory for creating PatientBuilder instances.
/// </summary>
/// <remarks>
/// Creates builders with profile-aware demographics and name generation.
/// Use .FromCity() for automatic demographic sampling, or manually specify fields for precise control.
///
/// Uses lazy-loaded singleton instances of DemographicsDataProvider and LocalBasedNameGenerator for performance.
/// </remarks>
public static class PatientBuilderFactory
{
    private static readonly Lazy<DemographicsDataProvider> _demographics =
        new(() => DemographicsDataProvider.CreateDefault());
    private static readonly Lazy<LocalBasedNameGenerator> _nameGenerator =
        new(() => new LocalBasedNameGenerator());

    /// <summary>
    /// Creates a PatientBuilder with profile-aware demographics and name generation.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for the desired FHIR version</param>
    /// <returns>A PatientBuilder instance with demographics support and profile-aware name generation</returns>
    /// <example>
    /// <code>
    /// // Option 1: Use .FromCity() for automatic demographic sampling
    /// var patient = PatientBuilderFactory.Create(schemaProvider)
    ///     .FromCity("Boston", "Massachusetts")  // Auto: race, age, gender, zip, area code, name
    ///     .WithAge(45)                          // Override age if desired
    ///     .WithRealisticBMI()
    ///     .Build();
    ///
    /// // Option 2: Manually specify all fields for precise control
    /// var patient = PatientBuilderFactory.Create(schemaProvider)
    ///     .WithAge(45)
    ///     .WithGender("male")
    ///     .WithGivenName("John")
    ///     .WithFamilyName("Smith")
    ///     .Build();
    /// </code>
    /// </example>
    public static PatientBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new PatientBuilder(
            schemaProvider,
            _demographics.Value,
            _nameGenerator.Value);
    }
}
