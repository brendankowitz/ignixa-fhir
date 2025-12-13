// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for quantity search tests.
/// Creates Observation test data with various quantity values and units for testing
/// quantity search parameters with comparison operators (eq, gt, ge, lt, le) and
/// system/unit combinations.
/// </summary>
public class QuantitySearchTestFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public QuantitySearchTestFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Observation test data with various quantity values and units.
    /// Index mapping:
    /// [0] = 180 [lb_av] (Body Weight - below test value)
    /// [1] = 185 [lb_av] (Body Weight - exact match)
    /// [2] = 190 [lb_av] (Body Weight - above test value)
    /// [3] = 120 mmHg (Systolic BP - different unit)
    /// [4] = 185 kg (Body Weight - same value, different unit)
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Observations { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        var faker = _apiFixture.Harness.CreateFaker();
        var schemaProvider = faker.SchemaProvider;

        // Create a patient for all observations
        var patient = PatientBuilderFactory.Create(schemaProvider)
            .WithTag(Tag)
            .WithGivenName("Test")
            .WithFamilyName("Quantity")
            .Build();

        var createdPatient = await _apiFixture.Harness.CreateResourceAsync(patient);

        // Create observations with various quantity values
        var observations = new[]
        {
            // [0] - 180 [lb_av] (Body Weight - below test value)
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("29463-7", "http://loinc.org", "Body Weight")
                .WithQuantityValue(180, "[lb_av]", "http://unitsofmeasure.org")
                .WithSubject(createdPatient.Id!)
                .WithStatus("final")
                .Build(),

            // [1] - 185 [lb_av] (Body Weight - exact match)
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("29463-7", "http://loinc.org", "Body Weight")
                .WithQuantityValue(185, "[lb_av]", "http://unitsofmeasure.org")
                .WithSubject(createdPatient.Id!)
                .WithStatus("final")
                .Build(),

            // [2] - 190 [lb_av] (Body Weight - above test value)
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("29463-7", "http://loinc.org", "Body Weight")
                .WithQuantityValue(190, "[lb_av]", "http://unitsofmeasure.org")
                .WithSubject(createdPatient.Id!)
                .WithStatus("final")
                .Build(),

            // [3] - 120 mmHg (Systolic Blood Pressure - different unit)
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("8480-6", "http://loinc.org", "Systolic blood pressure")
                .WithQuantityValue(120, "mmHg", "http://unitsofmeasure.org")
                .WithSubject(createdPatient.Id!)
                .WithStatus("final")
                .Build(),

            // [4] - 185 kg (Body Weight - same value, different unit)
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("29463-7", "http://loinc.org", "Body Weight")
                .WithQuantityValue(185, "kg", "http://unitsofmeasure.org")
                .WithSubject(createdPatient.Id!)
                .WithStatus("final")
                .Build()
        };

        Observations = await _apiFixture.Harness.CreateResourcesAsync(observations);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }
}
