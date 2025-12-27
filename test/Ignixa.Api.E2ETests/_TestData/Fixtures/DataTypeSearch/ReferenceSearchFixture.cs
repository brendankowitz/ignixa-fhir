// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for reference search tests.
/// Creates Patients, Organizations, Practitioners, and Observations with various reference patterns.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.ReferenceSearchTests
/// </summary>
public class ReferenceSearchFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public ReferenceSearchFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Patient resources for reference testing.
    /// Index mapping:
    /// [0] = Patient with organization reference (used in multiple observations)
    /// [1] = Patient with organization reference and general practitioner
    /// [2] = Patient with different organization
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Patients { get; private set; } = null!;

    /// <summary>
    /// Organization resources for reference testing.
    /// Index mapping:
    /// [0] = Organization referenced by Patient[0] and Patient[1]
    /// [1] = Organization referenced by Patient[2]
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Organizations { get; private set; } = null!;

    /// <summary>
    /// Practitioner resources for reference testing.
    /// Index mapping:
    /// [0] = Practitioner referenced by Patient[1] as general practitioner
    /// [1] = Practitioner referenced by Observation performers
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Practitioners { get; private set; } = null!;

    /// <summary>
    /// Observation resources with various reference patterns.
    /// Index mapping:
    /// [0] = subject: Patient[0]
    /// [1] = subject: Patient[1]
    /// [2] = subject: Patient[2]
    /// [3] = subject: Patient[0], performer: Practitioner[1]
    /// [4] = subject: Patient[1], performer: Organization[0]
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Observations { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Create Organizations first (referenced by Patients and Observations)
        var organizations = new[]
        {
            // [0] - Main hospital organization
            OrganizationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithName("General Hospital")
                .WithType("prov", display: "Healthcare Provider")
                .Build(),

            // [1] - Clinic organization
            OrganizationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithName("Family Clinic")
                .WithType("prov", display: "Healthcare Provider")
                .Build()
        };

        Organizations = await _apiFixture.Harness.CreateResourcesAsync(organizations);

        // Create Practitioners (referenced by Patients and Observations)
        var practitioners = new[]
        {
            // [0] - General practitioner
            PractitionerBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithName("Alice", "Anderson")
                .WithNpi("1234567890")
                .Build(),

            // [1] - Observation performer
            PractitionerBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithName("Bob", "Brown")
                .WithNpi("9876543210")
                .Build()
        };

        Practitioners = await _apiFixture.Harness.CreateResourcesAsync(practitioners);

        // Create Patients with references
        var patients = new[]
        {
            // [0] - Patient with organization reference only
            PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithGivenName("John")
                .WithFamilyName("Doe")
                .WithGender("male")
                .WithAge(45)
                .WithManagingOrganization(Organizations[0].Id!)
                .Build(),

            // [1] - Patient with organization and general practitioner
            PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithGivenName("Jane")
                .WithFamilyName("Smith")
                .WithGender("female")
                .WithAge(32)
                .WithManagingOrganization(Organizations[0].Id!)
                .WithGeneralPractitioner(Practitioners[0].Id!)
                .Build(),

            // [2] - Patient with different organization
            PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithGivenName("Bob")
                .WithFamilyName("Johnson")
                .WithGender("male")
                .WithAge(55)
                .WithManagingOrganization(Organizations[1].Id!)
                .Build()
        };

        Patients = await _apiFixture.Harness.CreateResourcesAsync(patients);

        // Create Observations with various subject and performer references
        var observations = new[]
        {
            // [0] - subject: Patient[0]
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("29463-7", "http://loinc.org", "Body Weight")
                .WithQuantityValue(185, "[lb_av]")
                .WithSubject(Patients[0].Id!)
                .Build(),

            // [1] - subject: Patient[1]
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("29463-7", "http://loinc.org", "Body Weight")
                .WithQuantityValue(140, "[lb_av]")
                .WithSubject(Patients[1].Id!)
                .Build(),

            // [2] - subject: Patient[2]
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("29463-7", "http://loinc.org", "Body Weight")
                .WithQuantityValue(200, "[lb_av]")
                .WithSubject(Patients[2].Id!)
                .Build(),

            // [3] - subject: Patient[0], performer: Practitioner[1]
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("8867-4", "http://loinc.org", "Heart rate")
                .WithQuantityValue(72, "/min")
                .WithSubject(Patients[0].Id!)
                .WithPractitionerPerformer(Practitioners[1].Id!)
                .Build(),

            // [4] - subject: Patient[1], performer: Organization[0]
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("8867-4", "http://loinc.org", "Heart rate")
                .WithQuantityValue(68, "/min")
                .WithSubject(Patients[1].Id!)
                .WithOrganizationPerformer(Organizations[0].Id!)
                .Build()
        };

        Observations = await _apiFixture.Harness.CreateResourcesAsync(observations);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
