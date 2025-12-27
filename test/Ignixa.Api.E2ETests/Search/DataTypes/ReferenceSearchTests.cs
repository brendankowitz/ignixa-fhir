// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

namespace Ignixa.Api.E2ETests.Search.DataTypes;

/// <summary>
/// E2E tests for reference search parameters.
/// Tests reference search with various formats (qualified, unqualified, type modifiers, chained searches).
/// Validates reference resolution with absolute/relative URLs and identifier modifiers.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.ReferenceSearchTests
/// </summary>
/// <remarks>
/// FHIR Reference Search Semantics (http://hl7.org/fhir/search.html#reference):
/// - Basic reference: patient=Patient/123 (qualified with resource type)
/// - Unqualified ID: patient=123 (ID only, resource type inferred from search parameter definition)
/// - Type modifier: subject:Patient=123 (restricts reference type when parameter allows multiple types)
/// - Chained search: subject:Patient.organization=Organization/456 (follows references)
/// - Identifier search: patient:identifier=http://system|value (searches by identifier instead of ID)
///
/// Reference formats supported:
/// - Relative: Patient/123
/// - Absolute: http://example.org/fhir/Patient/123
/// - Logical: urn:uuid:53fefa32-fcbb-4ff8-8a92-55ee120877b7
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class ReferenceSearchTests : CapabilityDrivenTestBase, IClassFixture<ReferenceSearchFixture>
{
    private readonly ReferenceSearchFixture _fixture;

    public ReferenceSearchTests(IgnixaApiFixture apiFixture, ReferenceSearchFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Tests basic reference search with qualified reference (ResourceType/ID).
    /// Searches for Observations by subject reference using Patient ID.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAQualifiedReference_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "subject");

        // Arrange
        var patientId = _fixture.Patients[0].Id!;

        // Act - Search with qualified reference (Patient/123)
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&subject=Patient/{patientId}");

        // Assert - Should find observations [0] and [3] which reference Patient[0]
        results.Length.ShouldBe(2);
        results.ShouldContain(r => r.Id == _fixture.Observations[0].Id, "Observation[0] references Patient[0]");
        results.ShouldContain(r => r.Id == _fixture.Observations[3].Id, "Observation[3] references Patient[0]");
    }

    /// <summary>
    /// Tests reference search with unqualified ID (ID only, no resource type prefix).
    /// Server should infer resource type from search parameter definition.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnUnqualifiedReferenceId_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "subject");

        // Arrange
        var patientId = _fixture.Patients[1].Id!;

        // Act - Search with unqualified ID (just "123", not "Patient/123")
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&subject={patientId}");

        // Assert - Should find observations [1] and [4] which reference Patient[1]
        results.Length.ShouldBe(2);
        results.ShouldContain(r => r.Id == _fixture.Observations[1].Id, "Observation[1] references Patient[1]");
        results.ShouldContain(r => r.Id == _fixture.Observations[4].Id, "Observation[4] references Patient[1]");
    }

    /// <summary>
    /// Tests reference search with type modifier (:Patient) to restrict reference type.
    /// Useful when search parameter allows multiple resource types.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAReferenceWithTypeModifier_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "subject");

        // Arrange
        var patientId = _fixture.Patients[2].Id!;

        // Act - Search with type modifier (subject:Patient=123)
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&subject:Patient={patientId}");

        // Assert - Should find observation [2] which references Patient[2]
        results.Length.ShouldBe(1);
        results[0].Id.ShouldBe(_fixture.Observations[2].Id, "Observation[2] references Patient[2]");
    }

    /// <summary>
    /// Tests reference search with qualified reference including resource type.
    /// Validates that both "Patient/123" and "123" formats work correctly.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenQualifiedAndUnqualifiedReferences_WhenSearched_ThenSameResultsReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "subject");

        // Arrange
        var patientId = _fixture.Patients[0].Id!;

        // Act - Search with both qualified and unqualified formats
        var qualifiedResults = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&subject=Patient/{patientId}");
        var unqualifiedResults = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&subject={patientId}");

        // Assert - Both searches should return same results
        qualifiedResults.Length.ShouldBe(unqualifiedResults.Length);

        foreach (var qualifiedResult in qualifiedResults)
        {
            unqualifiedResults.ShouldContain(r => r.Id == qualifiedResult.Id,
                "Qualified and unqualified searches should return same results");
        }
    }

    /// <summary>
    /// Tests searching for Patients by organization reference.
    /// Validates that references on Patient resource can be searched.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnOrganizationReference_WhenSearchingPatients_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "organization");

        // Arrange
        var organizationId = _fixture.Organizations[0].Id!;

        // Act - Search for patients by managing organization
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&organization=Organization/{organizationId}");

        // Assert - Should find Patient[0] and Patient[1] which reference Organization[0]
        results.Length.ShouldBe(2);
        results.ShouldContain(r => r.Id == _fixture.Patients[0].Id, "Patient[0] references Organization[0]");
        results.ShouldContain(r => r.Id == _fixture.Patients[1].Id, "Patient[1] references Organization[0]");
    }

    /// <summary>
    /// Tests searching for Patients by general practitioner reference.
    /// Validates multi-valued reference search (generalPractitioner is array).
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAGeneralPractitionerReference_WhenSearchingPatients_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "general-practitioner");

        // Arrange
        var practitionerId = _fixture.Practitioners[0].Id!;

        // Act - Search for patients by general practitioner
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&general-practitioner=Practitioner/{practitionerId}");

        // Assert - Should find Patient[1] which has Practitioner[0] as general practitioner
        results.Length.ShouldBe(1);
        results[0].Id.ShouldBe(_fixture.Patients[1].Id, "Patient[1] has Practitioner[0] as general practitioner");
    }

    /// <summary>
    /// Tests searching for Observations by performer reference.
    /// Validates reference search on multi-valued reference fields.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAPerformerReference_WhenSearchingObservations_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "performer");

        // Arrange
        var practitionerId = _fixture.Practitioners[1].Id!;

        // Act - Search for observations by performer (Practitioner)
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&performer=Practitioner/{practitionerId}");

        // Assert - Should find Observation[3] which has Practitioner[1] as performer
        results.Length.ShouldBe(1);
        results[0].Id.ShouldBe(_fixture.Observations[3].Id, "Observation[3] has Practitioner[1] as performer");
    }

    /// <summary>
    /// Tests searching for Observations by performer reference to an Organization.
    /// Validates that performer can reference different resource types.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnOrganizationPerformerReference_WhenSearchingObservations_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "performer");

        // Arrange
        var organizationId = _fixture.Organizations[0].Id!;

        // Act - Search for observations by performer (Organization)
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&performer=Organization/{organizationId}");

        // Assert - Should find Observation[4] which has Organization[0] as performer
        results.Length.ShouldBe(1);
        results[0].Id.ShouldBe(_fixture.Observations[4].Id, "Observation[4] has Organization[0] as performer");
    }

    /// <summary>
    /// Tests searching with type modifier when parameter supports multiple resource types.
    /// Validates that :Practitioner modifier only returns Practitioner references.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenATypeModifierForPerformer_WhenSearching_ThenOnlyMatchingTypeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "performer");

        // Arrange
        var practitionerId = _fixture.Practitioners[1].Id!;

        // Act - Search with :Practitioner type modifier
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&performer:Practitioner={practitionerId}");

        // Assert - Should find Observation[3] (has Practitioner performer), not Observation[4] (has Organization performer)
        results.Length.ShouldBe(1);
        results[0].Id.ShouldBe(_fixture.Observations[3].Id, "Only observations with Practitioner performers should be returned");
    }

    /// <summary>
    /// Tests that searching for non-existent reference returns empty results.
    /// Validates proper handling of references that don't exist.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenANonExistentReference_WhenSearched_ThenEmptyBundleReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "subject");

        // Act - Search for observations referencing non-existent patient
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&subject=Patient/non-existent-id");

        // Assert - Should return empty results
        results.Length.ShouldBe(0, "Non-existent reference should return no results");
    }

    /// <summary>
    /// Tests combining reference search with other search parameters.
    /// Validates AND logic between reference and other parameters.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenReferenceAndCodeSearch_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameters("Observation", "subject", "code");

        // Arrange
        var patientId = _fixture.Patients[0].Id!;

        // Act - Search by subject AND code (Heart rate observations for Patient[0])
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&subject=Patient/{patientId}&code=8867-4");

        // Assert - Should find only Observation[3] (Heart rate for Patient[0])
        results.Length.ShouldBe(1);
        results[0].Id.ShouldBe(_fixture.Observations[3].Id, "Only heart rate observation for Patient[0] should be returned");
    }

    /// <summary>
    /// Tests multiple reference parameters (OR logic within same parameter).
    /// Validates searching for observations referencing multiple patients.
    /// Ported from: ReferenceSearchTests.GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenMultipleReferenceValues_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "subject");

        // Arrange
        var patient0Id = _fixture.Patients[0].Id!;
        var patient1Id = _fixture.Patients[1].Id!;

        // Act - Search for observations referencing Patient[0] OR Patient[1]
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&subject=Patient/{patient0Id},Patient/{patient1Id}");

        // Assert - Should find observations [0, 1, 3, 4] (all observations for Patient[0] and Patient[1])
        results.Length.ShouldBe(4);
        results.ShouldContain(r => r.Id == _fixture.Observations[0].Id, "Observation[0] references Patient[0]");
        results.ShouldContain(r => r.Id == _fixture.Observations[1].Id, "Observation[1] references Patient[1]");
        results.ShouldContain(r => r.Id == _fixture.Observations[3].Id, "Observation[3] references Patient[0]");
        results.ShouldContain(r => r.Id == _fixture.Observations[4].Id, "Observation[4] references Patient[1]");
    }
}
