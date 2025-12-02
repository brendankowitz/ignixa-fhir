// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.FhirFakes;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Specification.Generated;

namespace Ignixa.Api.E2ETests;

/// <summary>
/// E2E tests for basic FHIR resource operations (CRUD).
/// Uses AAA pattern and BDD naming convention.
/// </summary>
internal class BasicResourceTests : IClassFixture<IgnixaApiFixture>
{
    private readonly HttpClient _client;
    private readonly SchemaBasedFhirResourceFaker _faker;

    public BasicResourceTests(IgnixaApiFixture fixture)
    {
        _client = fixture.CreateClient();
        _faker = new SchemaBasedFhirResourceFaker(new R4CoreSchemaProvider());
    }

    #region Patient Tests

    [Fact]
    public async Task GivenAPatient_WhenPosting_ThenReturnsCreated()
    {
        // Arrange
        var patient = _faker.CreatePatient();
        var patientJson = patient.SerializeToString();
        var content = new StringContent(patientJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        // Act
        var response = await _client.PostAsync("/Patient", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var locationHeader = response.Headers.Location!.ToString();
        locationHeader.Should().Contain("/Patient/");

        var responseBody = await response.Content.ReadAsStringAsync();
        var createdPatient = JsonSourceNodeFactory.Parse(responseBody);
        createdPatient.ResourceType.Should().Be("Patient");
        createdPatient.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenAPatient_WhenGettingById_ThenReturnsResource()
    {
        // Arrange - Create a patient first
        var patient = _faker.CreatePatient();
        var patientJson = patient.SerializeToString();
        var content = new StringContent(patientJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        var createResponse = await _client.PostAsync("/Patient", content);
        createResponse.EnsureSuccessStatusCode();

        var locationHeader = createResponse.Headers.Location!.ToString();
        var patientId = locationHeader.Split('/').Last();

        // Act - Retrieve the patient
        var getResponse = await _client.GetAsync($"/Patient/{patientId}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseBody = await getResponse.Content.ReadAsStringAsync();
        var retrievedPatient = JsonSourceNodeFactory.Parse(responseBody);
        retrievedPatient.ResourceType.Should().Be("Patient");
        retrievedPatient.Id.Should().Be(patientId);
    }

    [Fact]
    public async Task GivenAPatient_WhenUpdating_ThenReturnsOk()
    {
        // Arrange - Create a patient first
        var patient = _faker.CreatePatient();
        var patientJson = patient.SerializeToString();
        var content = new StringContent(patientJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        var createResponse = await _client.PostAsync("/Patient", content);
        createResponse.EnsureSuccessStatusCode();

        var locationHeader = createResponse.Headers.Location!.ToString();
        var patientId = locationHeader.Split('/').Last();

        // Get the created patient
        var getResponse = await _client.GetAsync($"/Patient/{patientId}");
        var existingPatientJson = await getResponse.Content.ReadAsStringAsync();
        var existingPatient = JsonSourceNodeFactory.Parse(existingPatientJson);

        // Modify the patient
        existingPatient.MutableNode["active"] = false;

        // Act - Update the patient
        var updatedJson = existingPatient.SerializeToString();
        var updateContent = new StringContent(updatedJson, Encoding.UTF8);
        updateContent.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");
        var updateResponse = await _client.PutAsync($"/Patient/{patientId}", updateContent);

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseBody = await updateResponse.Content.ReadAsStringAsync();
        var updatedPatient = JsonSourceNodeFactory.Parse(responseBody);
        updatedPatient.Id.Should().Be(patientId);

        var active = updatedPatient.MutableNode["active"]?.GetValue<bool>();
        active.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAPatient_WhenDeleting_ThenReturnsNoContent()
    {
        // Arrange - Create a patient first
        var patient = _faker.CreatePatient();
        var patientJson = patient.SerializeToString();
        var content = new StringContent(patientJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        var createResponse = await _client.PostAsync("/Patient", content);
        createResponse.EnsureSuccessStatusCode();

        var locationHeader = createResponse.Headers.Location!.ToString();
        var patientId = locationHeader.Split('/').Last();

        // Act - Delete the patient
        var deleteResponse = await _client.DeleteAsync($"/Patient/{patientId}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion - should return 404 or 410
        var getResponse = await _client.GetAsync($"/Patient/{patientId}");
        getResponse.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Gone);
    }

    #endregion

    #region Bundle Tests

    [Fact]
    public async Task GivenABundle_WhenPosting_ThenAllResourcesCreated()
    {
        // Arrange
        var bundle = _faker.CreatePatientCompartmentBundle(observationCount: 2, conditionCount: 1, encounterCount: 1);
        var bundleJson = bundle.SerializeToString();
        var content = new StringContent(bundleJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        // Act
        var response = await _client.PostAsync("/", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseBody = await response.Content.ReadAsStringAsync();
        var responseBundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(responseBody);
        responseBundle.Should().NotBeNull();
        responseBundle!.Type.Should().Be(BundleJsonNode.BundleType.TransactionResponse);

        // Verify all entries were processed
        responseBundle.Entry.Should().HaveCount(5); // 1 Patient + 2 Observations + 1 Condition + 1 Encounter

        // Verify all entries have successful status codes (201 Created)
        foreach (var entry in responseBundle.Entry)
        {
            entry.Response.Should().NotBeNull();
            entry.Response!.Status.Should().StartWith("201");
        }
    }

    [Fact]
    public async Task GivenASearchableBundle_WhenSearchingForPatient_ThenReturnsResults()
    {
        // Arrange - Create a patient with a unique family name
        var patient = _faker.CreatePatient();
        var familyName = patient.MutableNode["name"]?[0]?["family"]?.GetValue<string>();
        var patientJson = patient.SerializeToString();
        var content = new StringContent(patientJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        var createResponse = await _client.PostAsync("/Patient", content);
        createResponse.EnsureSuccessStatusCode();

        // Wait a bit for indexing (if async indexing is used)
        await Task.Delay(500);

        // Act - Search for the patient by family name
        var searchResponse = await _client.GetAsync($"/Patient?family={familyName}");

        // Assert
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseBody = await searchResponse.Content.ReadAsStringAsync();
        var searchBundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(responseBody);
        searchBundle.Should().NotBeNull();
        searchBundle!.Type.Should().Be(BundleJsonNode.BundleType.Searchset);
        searchBundle.Entry.Should().HaveCountGreaterOrEqualTo(1);
    }

    #endregion

    #region Multi-tenancy Tests

    [Fact]
    public async Task GivenATenant_WhenCreatingPatient_ThenResourceIsolated()
    {
        // Arrange
        var patient = _faker.CreatePatient();
        var patientJson = patient.SerializeToString();
        var content = new StringContent(patientJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        // Act - Create in tenant 1
        var createResponse = await _client.PostAsync("/tenant/1/Patient", content);

        // Assert
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var locationHeader = createResponse.Headers.Location!.ToString();
        locationHeader.Should().Contain("/tenant/1/Patient/");

        var patientId = locationHeader.Split('/').Last();

        // Verify we can access in tenant 1
        var getTenant1Response = await _client.GetAsync($"/tenant/1/Patient/{patientId}");
        getTenant1Response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify we cannot access in tenant 2 (resource isolation)
        var getTenant2Response = await _client.GetAsync($"/tenant/2/Patient/{patientId}");
        getTenant2Response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenPartitionZero_WhenAccessing_ThenReturnsBadRequest()
    {
        // Arrange
        var patient = _faker.CreatePatient();
        var patientJson = patient.SerializeToString();
        var content = new StringContent(patientJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");

        // Act - Try to create in partition 0 (reserved for system)
        var createResponse = await _client.PostAsync("/tenant/0/Patient", content);

        // Assert - Should be blocked by middleware
        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}
