// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for number search tests.
/// Creates Patient test data with various multipleBirth values for testing
/// number search parameters with comparison operators (eq, gt, ge, lt, le).
/// </summary>
/// <remarks>
/// FHIR Number Search Semantics (http://hl7.org/fhir/search.html#number):
/// - Number search parameters support comparison prefixes (eq, gt, ge, lt, le)
/// - The Patient.multipleBirth element can be either integer (birth order) or boolean (is multiple birth)
/// - Number searches only match against integer values, not boolean values
///
/// Test Data Setup:
/// Index mapping:
/// [0] = MultipleBirth integer 1 (first born)
/// [1] = MultipleBirth integer 2 (second born)
/// [2] = MultipleBirth integer 3 (third born)
/// [3] = MultipleBirth integer 4 (fourth born - quadruplet)
/// [4] = MultipleBirth boolean true (is from multiple birth, but order unknown)
/// [5] = MultipleBirth boolean false (singleton)
/// [6] = No MultipleBirth value (null/missing)
/// </remarks>
public class NumberSearchTestFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public NumberSearchTestFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Patient test data with various multipleBirth patterns.
    /// See class-level remarks for index mapping.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Patients { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Create 7 patients with various multipleBirth patterns
        var patients = new[]
        {
            // [0] - First born of multiple birth
            CreatePatientWithInteger(1, "Smith"),

            // [1] - Second born of multiple birth
            CreatePatientWithInteger(2, "Johnson"),

            // [2] - Third born of multiple birth (triplet)
            CreatePatientWithInteger(3, "Williams"),

            // [3] - Fourth born of multiple birth (quadruplet)
            CreatePatientWithInteger(4, "Brown"),

            // [4] - Multiple birth boolean true (twin/triplet, order unknown)
            CreatePatientWithBoolean(true, "Davis"),

            // [5] - Multiple birth boolean false (singleton)
            CreatePatientWithBoolean(false, "Miller"),

            // [6] - No multipleBirth value
            CreatePatientWithoutMultipleBirth("Wilson")
        };

        Patients = await _apiFixture.Harness.CreateResourcesAsync(patients);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }

    private ResourceJsonNode CreatePatientWithInteger(int order, string family)
    {
        return PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
            .WithMultipleBirth(order)
            .WithFamilyName(family)
            .WithTag(Tag)
            .Build();
    }

    private ResourceJsonNode CreatePatientWithBoolean(bool isMultipleBirth, string family)
    {
        return PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
            .WithMultipleBirth(isMultipleBirth)
            .WithFamilyName(family)
            .WithTag(Tag)
            .Build();
    }

    private ResourceJsonNode CreatePatientWithoutMultipleBirth(string family)
    {
        return PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
            .WithFamilyName(family)
            .WithTag(Tag)
            .Build();
    }
}
