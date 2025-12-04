// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Population;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Factory for creating PatientBuilder instances with different capabilities.
/// </summary>
/// <remarks>
/// Provides two factory methods:
/// 1. CreateSimple() - Basic Bogus-based random generation for simple tests
/// 2. CreateRealistic() - Sophisticated US demographics with ethnically appropriate names
///
/// The realistic builder uses lazy-loaded singleton instances of DemographicsDataProvider
/// and EthnicNameGenerator for performance.
/// </remarks>
public static class PatientBuilderFactory
{
    private static readonly Lazy<DemographicsDataProvider> _demographics =
        new(() => DemographicsDataProvider.CreateDefault());
    private static readonly Lazy<EthnicNameGenerator> _nameGenerator =
        new(() => new EthnicNameGenerator());

    /// <summary>
    /// Creates a simple builder with Bogus-based random generation.
    /// Suitable for basic tests where demographic realism is not critical.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for the desired FHIR version</param>
    /// <returns>A PatientBuilder instance with simple randomization</returns>
    /// <example>
    /// <code>
    /// var patient = PatientBuilderFactory.CreateSimple(schemaProvider)
    ///     .WithAge(45)
    ///     .WithGender("male")
    ///     .WithGivenName("John")
    ///     .WithFamilyName("Smith")
    ///     .Build();
    /// </code>
    /// </example>
    public static PatientBuilder CreateSimple(IFhirSchemaProvider schemaProvider)
    {
        return new PatientBuilder(schemaProvider);
    }

    /// <summary>
    /// Creates a sophisticated builder with real US demographics.
    /// Suitable for population generation and realistic test scenarios.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for the desired FHIR version</param>
    /// <returns>A PatientBuilder instance with realistic demographics support</returns>
    /// <example>
    /// <code>
    /// var patient = PatientBuilderFactory.CreateRealistic(schemaProvider)
    ///     .FromCity("Boston", "Massachusetts")  // Auto: race, age, gender, zip, area code, name
    ///     .WithAge(45)                          // Override age if desired
    ///     .WithRealisticBMI()
    ///     .Build();
    /// </code>
    /// </example>
    public static PatientBuilder CreateRealistic(IFhirSchemaProvider schemaProvider)
    {
        return new PatientBuilder(
            schemaProvider,
            _demographics.Value,
            _nameGenerator.Value);
    }
}
