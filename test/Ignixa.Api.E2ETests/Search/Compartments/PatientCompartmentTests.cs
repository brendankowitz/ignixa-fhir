// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Scenarios;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Search.Compartments;

/// <summary>
/// E2E tests for FHIR compartment search functionality.
/// Tests compartment-based resource searches which return all resources linked to a specific compartment owner.
/// </summary>
/// <remarks>
/// <para>
/// Compartment searches are a core FHIR interaction pattern that allow querying all resources
/// associated with a specific resource instance (e.g., all resources for a patient).
/// </para>
/// <para>
/// FHIR Specification: http://hl7.org/fhir/compartmentdefinition.html
/// </para>
/// <para>
/// Common compartment search patterns:
/// <list type="bullet">
/// <item><description>GET /Patient/{id}/* - All resources in patient compartment</description></item>
/// <item><description>GET /Patient/{id}/Observation - Specific resource type in compartment</description></item>
/// <item><description>GET /Patient/{id}/Observation?code=xxx - Compartment search with parameters</description></item>
/// <item><description>POST /Patient/{id}/_search - POST variant of compartment search</description></item>
/// </list>
/// </para>
/// <para>
/// Gap addressed: Identified as HIGH priority in docs/investigations/e2e-test-gap-analysis.md (lines 156-207)
/// </para>
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class CompartmentSearchTests : CapabilityDrivenTestBase
{
    public CompartmentSearchTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests retrieval of all resources in a patient compartment.
    /// Verifies that compartment search returns all resource types linked to the patient.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FHIR Spec: GET /Patient/{id}/* should return all resources in the patient compartment.
    /// This includes Observations, Encounters, Conditions, etc. that reference the patient.
    /// </para>
    /// <para>
    /// Alternative pattern using PatientCompartmentScenario (recommended for complex compartment tests):
    /// <code>
    /// var compartment = SchemaProvider.CreateCompartmentScenario(tag);
    /// await Harness.CreateResourcesAsync([.. compartment.AllResources]);
    /// var results = await Harness.SearchAsync($"Patient/{compartment.Patient.Id}/*", $"_tag={tag}");
    /// </code>
    /// </para>
    /// <para>
    /// SKIPPED: Wildcard compartment search (Patient/{id}/*) is not yet implemented.
    /// Server returns 400 Bad Request for wildcard resource type.
    /// </para>
    /// </remarks>
    [Fact(Skip = "Wildcard compartment search (Patient/{id}/*) not yet implemented")]
    public async Task GivenPatientCompartment_WhenSearchingAllResources_ThenReturnsAllCompartmentResources()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient
        var patient = CreatePatient()
            .FromCity(KnownCities.Seattle)
            .WithFamilyName("Smith")
            .WithGivenName("John")
            .WithTag(tag)
            .Build();

        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create observations linked to patient
        var obs1 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")
            .WithQuantityValue(85m, "kg")
            .WithSubject(createdPatient.Id!)
            .Build();

        var obs2 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("8867-4", "http://loinc.org", "Heart Rate")
            .WithQuantityValue(72m, "beats/minute")
            .WithSubject(createdPatient.Id!)
            .Build();

        await Harness.CreateResourcesAsync([obs1, obs2]);

        // Act: Search patient compartment for all resources
        var results = await Harness.SearchAsync($"Patient/{createdPatient.Id}/*", $"_tag={tag}");

        // Assert: Should return observations linked to this patient
        results.Should().NotBeEmpty("compartment should contain resources linked to the patient");
        results.Should().Contain(r => r.ResourceType == "Observation", "patient compartment should include observations");

        // Verify all returned resources are tagged with our test tag
        results.Should().AllSatisfy(r =>
        {
            var meta = r.MutableNode["meta"];
            meta.Should().NotBeNull();
            var tagArray = meta?["tag"];
            tagArray.Should().NotBeNull();
            var tags = tagArray?.AsArray().Select(t => t?["code"]?.GetValue<string>());
            tags.Should().Contain(tag);
        });
    }

    /// <summary>
    /// Tests retrieval of a specific resource type from a patient compartment.
    /// Verifies that compartment search can filter by resource type.
    /// </summary>
    /// <remarks>
    /// FHIR Spec: GET /Patient/{id}/Observation should return only Observations in the patient compartment.
    /// </remarks>
    [Fact]
    public async Task GivenPatientCompartment_WhenSearchingObservations_ThenReturnsOnlyPatientObservations()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create two patients
        var patient1 = CreatePatient()
            .FromCity(KnownCities.Seattle)
            .WithFamilyName("Smith")
            .WithTag(tag)
            .Build();

        var patient2 = CreatePatient()
            .FromCity(KnownCities.Boston)
            .WithFamilyName("Jones")
            .WithTag(tag)
            .Build();

        var createdResources = await Harness.CreateResourcesAsync([patient1, patient2]);
        var patient1Id = createdResources[0].Id!;
        var patient2Id = createdResources[1].Id!;

        // Create observations for patient1
        var obs1 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")
            .WithQuantityValue(85m, "kg")
            .WithSubject(patient1Id)
            .Build();

        var obs2 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("8867-4", "http://loinc.org", "Heart Rate")
            .WithQuantityValue(72m, "beats/minute")
            .WithSubject(patient1Id)
            .Build();

        // Create observation for patient2 (should NOT appear in patient1 compartment search)
        var obs3 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")
            .WithQuantityValue(90m, "kg")
            .WithSubject(patient2Id)
            .Build();

        var createdObservations = await Harness.CreateResourcesAsync([obs1, obs2, obs3]);
        var obs1Id = createdObservations[0].Id;
        var obs2Id = createdObservations[1].Id;

        // Act: Search patient1 compartment for Observations
        var results = await Harness.SearchAsync($"Patient/{patient1Id}/Observation", $"_tag={tag}");

        // Assert: Should return only observations for patient1
        results.Should().HaveCount(2, "patient1 has exactly 2 observations");
        results.Should().AllSatisfy(r => r.ResourceType.Should().Be("Observation"));

        // Verify the correct observations are returned
        var resultIds = results.Select(r => r.Id).ToArray();
        resultIds.Should().Contain(obs1Id);
        resultIds.Should().Contain(obs2Id);
        resultIds.Should().NotContain(createdObservations[2].Id, "observations from other patients should not be returned");
    }

    /// <summary>
    /// Tests compartment search combined with additional search parameters.
    /// Verifies that compartment searches support filtering with search parameters.
    /// </summary>
    /// <remarks>
    /// FHIR Spec: GET /Patient/{id}/Observation?code=xxx should filter compartment observations by code.
    /// </remarks>
    [Fact]
    public async Task GivenPatientCompartment_WhenSearchingWithParameters_ThenReturnsFilteredResults()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient
        var patient = CreatePatient()
            .FromCity(KnownCities.Seattle)
            .WithFamilyName("Smith")
            .WithTag(tag)
            .Build();

        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create multiple observations with different codes
        var obs1 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")
            .WithQuantityValue(85m, "kg")
            .WithSubject(createdPatient.Id!)
            .Build();

        var obs2 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("8867-4", "http://loinc.org", "Heart Rate")
            .WithQuantityValue(72m, "beats/minute")
            .WithSubject(createdPatient.Id!)
            .Build();

        var obs3 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")  // Same code as obs1
            .WithQuantityValue(87m, "kg")
            .WithSubject(createdPatient.Id!)
            .Build();

        var createdObservations = await Harness.CreateResourcesAsync([obs1, obs2, obs3]);

        // Act: Search patient compartment for Body Weight observations only
        var results = await Harness.SearchAsync(
            $"Patient/{createdPatient.Id}/Observation",
            $"code=29463-7&_tag={tag}");

        // Assert: Should return only Body Weight observations
        results.Should().HaveCount(2, "patient has exactly 2 Body Weight observations");
        results.Should().AllSatisfy(obs =>
        {
            obs.ResourceType.Should().Be("Observation");
            var codeNode = obs.MutableNode["code"]?["coding"]?[0]?["code"];
            codeNode.Should().NotBeNull();
            codeNode!.GetValue<string>().Should().Be("29463-7");
        });

        // Verify correct observations are returned
        var resultIds = results.Select(r => r.Id).ToArray();
        resultIds.Should().Contain(createdObservations[0].Id);
        resultIds.Should().Contain(createdObservations[2].Id);
        resultIds.Should().NotContain(createdObservations[1].Id, "Heart Rate observation should be filtered out");
    }

    /// <summary>
    /// Tests POST-based compartment search (_search endpoint).
    /// Verifies that compartment search supports POST method for complex queries.
    /// </summary>
    /// <remarks>
    /// FHIR Spec: POST /Patient/{id}/_search should work like GET /Patient/{id}/* but allows form-encoded parameters.
    /// This is useful for searches with many parameters that exceed URL length limits.
    /// SKIPPED: POST compartment search is not yet implemented. Server returns 405 Method Not Allowed.
    /// </remarks>
    [Fact(Skip = "POST compartment search not yet implemented")]
    public async Task GivenPatientCompartment_WhenPostSearch_ThenReturnsCompartmentResources()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient
        var patient = CreatePatient()
            .FromCity(KnownCities.Seattle)
            .WithFamilyName("Smith")
            .WithTag(tag)
            .Build();

        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create observations linked to patient
        var obs1 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")
            .WithQuantityValue(85m, "kg")
            .WithSubject(createdPatient.Id!)
            .Build();

        var obs2 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("8867-4", "http://loinc.org", "Heart Rate")
            .WithQuantityValue(72m, "beats/minute")
            .WithSubject(createdPatient.Id!)
            .Build();

        await Harness.CreateResourcesAsync([obs1, obs2]);

        // Act: POST search on patient compartment
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_tag"] = tag
        });

        var response = await Client.PostAsync($"/Patient/{createdPatient.Id}/_search", formContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var bundle = Ignixa.Serialization.JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);

        var results = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!)
            .ToArray();

        // Assert: Should return observations linked to this patient
        results.Should().NotBeEmpty("compartment should contain resources linked to the patient");
        results.Should().Contain(r => r.ResourceType == "Observation", "patient compartment should include observations");

        // Verify all returned resources are tagged with our test tag
        results.Should().AllSatisfy(r =>
        {
            var meta = r.MutableNode["meta"];
            meta.Should().NotBeNull();
            var tagArray = meta?["tag"];
            tagArray.Should().NotBeNull();
            var tags = tagArray?.AsArray().Select(t => t?["code"]?.GetValue<string>());
            tags.Should().Contain(tag);
        });
    }

    /// <summary>
    /// Tests that compartment search returns empty results when no resources match.
    /// Verifies proper handling of empty compartments.
    /// </summary>
    /// <remarks>
    /// FHIR Spec: Compartment search should return empty Bundle when no matching resources exist.
    /// </remarks>
    [Fact]
    public async Task GivenEmptyPatientCompartment_WhenSearching_ThenReturnsEmptyResults()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient with no linked resources
        var patient = CreatePatient()
            .FromCity(KnownCities.Seattle)
            .WithFamilyName("LonelyPatient")
            .WithTag(tag)
            .Build();

        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Act: Search patient compartment for Observations
        var results = await Harness.SearchAsync(
            $"Patient/{createdPatient.Id}/Observation",
            $"_tag={tag}");

        // Assert: Should return empty results
        results.Should().BeEmpty("patient has no observations");
    }

    /// <summary>
    /// Tests compartment search with multiple resource types.
    /// Verifies that compartment wildcard (*) returns resources of different types.
    /// </summary>
    /// <remarks>
    /// FHIR Spec: GET /Patient/{id}/* should return all resource types in the compartment.
    /// This includes Observations, Encounters, Conditions, etc.
    /// SKIPPED: Wildcard compartment search (Patient/{id}/*) is not yet implemented.
    /// </remarks>
    [Fact(Skip = "Wildcard compartment search (Patient/{id}/*) not yet implemented")]
    public async Task GivenPatientCompartmentWithMultipleTypes_WhenSearchingAllResources_ThenReturnsAllTypes()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient
        var patient = CreatePatient()
            .FromCity(KnownCities.Seattle)
            .WithFamilyName("MultiResource")
            .WithTag(tag)
            .Build();

        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create observation
        var obs = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")
            .WithQuantityValue(85m, "kg")
            .WithSubject(createdPatient.Id!)
            .Build();

        // Create encounter (using faker for simplicity)
        var faker = Harness.CreateFaker().WithTag(tag);
        var encounter = faker.Generate("Encounter");

        // Link encounter to patient
        encounter.MutableNode["subject"] = new System.Text.Json.Nodes.JsonObject
        {
            ["reference"] = $"Patient/{createdPatient.Id}"
        };

        await Harness.CreateResourcesAsync([obs, encounter]);

        // Act: Search patient compartment for all resources
        var results = await Harness.SearchAsync($"Patient/{createdPatient.Id}/*", $"_tag={tag}");

        // Assert: Should return resources of different types
        results.Should().NotBeEmpty("compartment should contain resources");

        var resourceTypes = results.Select(r => r.ResourceType).Distinct().ToArray();
        resourceTypes.Should().Contain("Observation", "compartment should include observations");
        resourceTypes.Should().Contain("Encounter", "compartment should include encounters");
    }

    /// <summary>
    /// Tests compartment search pagination.
    /// Verifies that compartment searches support _count parameter for pagination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FHIR Spec: Compartment searches should support standard pagination parameters like _count.
    /// </para>
    /// <para>
    /// This test demonstrates PatientCompartmentScenario.CreateMinimalCompartmentScenario for cleaner test data generation.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GivenPatientCompartmentWithManyResources_WhenSearchingWithCount_ThenReturnsPaginatedResults()
    {
        // Arrange: Create patient with exactly 5 observations using minimal compartment scenario
        var tag = Guid.NewGuid().ToString();
        const int totalObservations = 5;
        const int pageSize = 2;

        // Use PatientCompartmentScenario to create test data - demonstrates pattern
        var compartment = SchemaProvider.CreateMinimalCompartmentScenario(tag, observationCount: totalObservations);

        await Harness.CreateResourcesAsync([.. compartment.AllResources]);

        // Act: Search patient compartment with _count parameter
        var bundle = await Harness.SearchBundleAsync(
            $"Patient/{compartment.Patient.Id}/Observation",
            $"_tag={tag}&_count={pageSize}");

        // Assert: First page should respect count limit
        bundle.Entry.Should().NotBeEmpty();
        bundle.Entry.Count.Should().BeLessOrEqualTo(pageSize, "page size should be respected");

        // Note: Pagination verification (next link) is optional based on server implementation
        // Some servers may not return next link even when there are more results
        // The key assertion is that page size limit is respected
    }

    /// <summary>
    /// Tests that compartment search respects resource compartment membership rules.
    /// Verifies that only resources that belong to the compartment are returned.
    /// </summary>
    /// <remarks>
    /// FHIR Spec: Compartment membership is defined by CompartmentDefinition resources.
    /// For Patient compartment, resources must reference the patient via defined search parameters.
    /// </remarks>
    [Fact]
    public async Task GivenMultiplePatients_WhenSearchingCompartment_ThenReturnsOnlyCompartmentMembers()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create two separate patients
        var patient1 = CreatePatient()
            .FromCity(KnownCities.Seattle)
            .WithFamilyName("Patient1")
            .WithTag(tag)
            .Build();

        var patient2 = CreatePatient()
            .FromCity(KnownCities.Boston)
            .WithFamilyName("Patient2")
            .WithTag(tag)
            .Build();

        var createdPatients = await Harness.CreateResourcesAsync([patient1, patient2]);
        var patient1Id = createdPatients[0].Id!;
        var patient2Id = createdPatients[1].Id!;

        // Create observations for both patients
        var obs1ForPatient1 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")
            .WithQuantityValue(85m, "kg")
            .WithSubject(patient1Id)
            .Build();

        var obs2ForPatient1 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("8867-4", "http://loinc.org", "Heart Rate")
            .WithQuantityValue(72m, "beats/minute")
            .WithSubject(patient1Id)
            .Build();

        var obsForPatient2 = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("29463-7", "http://loinc.org", "Body Weight")
            .WithQuantityValue(90m, "kg")
            .WithSubject(patient2Id)
            .Build();

        var createdObs = await Harness.CreateResourcesAsync([obs1ForPatient1, obs2ForPatient1, obsForPatient2]);

        // Act: Search patient1 compartment
        var patient1Results = await Harness.SearchAsync(
            $"Patient/{patient1Id}/Observation",
            $"_tag={tag}");

        // Act: Search patient2 compartment
        var patient2Results = await Harness.SearchAsync(
            $"Patient/{patient2Id}/Observation",
            $"_tag={tag}");

        // Assert: Each compartment should contain only its own observations
        patient1Results.Should().HaveCount(2, "patient1 has exactly 2 observations");
        patient1Results.Select(r => r.Id).Should().Contain(createdObs[0].Id);
        patient1Results.Select(r => r.Id).Should().Contain(createdObs[1].Id);
        patient1Results.Select(r => r.Id).Should().NotContain(createdObs[2].Id, "patient2's observation should not be in patient1's compartment");

        patient2Results.Should().HaveCount(1, "patient2 has exactly 1 observation");
        patient2Results.Select(r => r.Id).Should().Contain(createdObs[2].Id);
        patient2Results.Select(r => r.Id).Should().NotContain(createdObs[0].Id, "patient1's observations should not be in patient2's compartment");
        patient2Results.Select(r => r.Id).Should().NotContain(createdObs[1].Id, "patient1's observations should not be in patient2's compartment");
    }
}
