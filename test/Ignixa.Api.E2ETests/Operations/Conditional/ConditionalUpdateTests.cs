// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.Api.E2ETests.Operations.Conditional;

/// <summary>
/// E2E tests for FHIR conditional update operations.
/// Tests validate the behavior defined in FHIR R4 Section 3.1.0.6 (Conditional Update).
/// </summary>
/// <remarks>
/// Conditional update enables updates based on search criteria using PUT /{resourceType}?{searchParams}:
/// - 0 matches: Create new resource with client ID if provided, otherwise server-assigned (201 Created)
/// - 1 match: Update existing resource (200 OK)
/// - Multiple matches: Return error (412 Precondition Failed)
///
/// Test Coverage:
/// - Conditional update with no matches (creates new)
/// - Conditional update with no matches but client ID (preserves client ID)
/// - Conditional update with one match (updates existing)
/// - Conditional update with one match and matching ID (updates existing)
/// - Conditional update with one match but mismatched ID (400 Bad Request)
/// - Conditional update with multiple matches (412 Precondition Failed)
/// - Conditional update with no search criteria (400 Bad Request)
/// - Conditional update on Bundle resource type (400 Bad Request)
///
/// Reference:
/// - http://hl7.org/fhir/R4/http.html#cond-update
/// </remarks>
public class ConditionalUpdateTests : CapabilityDrivenTestBase
{
    public ConditionalUpdateTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests conditional update when no matching resources exist.
    /// Expected: Creates a new resource with server-assigned ID and returns 201 Created.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: If the search returns zero matches, the server processes the request
    /// as a create operation, creating a new resource with a server-assigned ID.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalUpdate_WhenNoMatch_ThenCreatesNewResource()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("NoMatchUpdate")
            .WithGivenName("Alice")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        // Act - PUT /{resourceType}?identifier=...
        var response = await Harness.PutResourceWithQueryAsync(
            patient,
            $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "server should create new resource when search returns 0 matches");
        response.Headers.Location.ShouldNotBeNull("Location header should be present");
        response.Headers.Location!.ToString().ShouldContain("Patient/");

        var createdPatient = await Harness.ParseResourceResponseAsync(response);
        createdPatient.ResourceType.ShouldBe("Patient");
        createdPatient.Id.ShouldNotBeNullOrEmpty();

        // Verify the identifier was preserved
        var identifiers = createdPatient.MutableNode["identifier"]?.AsArray();
        identifiers.ShouldNotBeNull();

        var matchingIdentifier = identifiers!.FirstOrDefault(i =>
            i!["system"]?.GetValue<string>() == "http://hospital.example.org/mrn" &&
            i!["value"]?.GetValue<string>() == uniqueIdentifier);
        matchingIdentifier.ShouldNotBeNull("identifier should be preserved in created resource");
    }

    /// <summary>
    /// Tests conditional update when no match exists but client provides an ID in the resource body.
    /// Expected: Creates a new resource with the client-provided ID (201 Created).
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: When no match is found and the resource body contains an ID,
    /// the server should use the client-provided ID for the new resource.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalUpdate_WhenNoMatchWithClientId_ThenCreatesNewResourceWithClientId()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();
        var clientProvidedId = Guid.NewGuid().ToString("N");

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("ClientIdUpdate")
            .WithGivenName("Bob")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        // Set client-provided ID
        patient.Id = clientProvidedId;

        // Act - PUT /{resourceType}?identifier=... (with ID in body)
        var response = await Harness.PutResourceWithQueryAsync(
            patient,
            $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "server should create new resource when search returns 0 matches");

        var createdPatient = await Harness.ParseResourceResponseAsync(response);
        createdPatient.Id.ShouldBe(clientProvidedId, "server should preserve client-provided ID");
    }

    /// <summary>
    /// Tests conditional update when exactly one matching resource exists.
    /// Expected: Updates the existing resource and returns 200 OK.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: If one match is found, the server performs an update on the
    /// matched resource using the content provided in the request.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalUpdate_WhenOneMatch_ThenUpdatesExistingResource()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        // Create the first patient with a unique identifier
        var existingPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("UpdateTest")
            .WithGivenName("Charlie")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        var createdResources = await Harness.CreateResourcesAsync([existingPatient]);
        var existingId = createdResources[0].Id;

        // Create updated patient with same identifier but different given name
        var updatedPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("UpdateTest")
            .WithGivenName("Charles") // Changed name
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        // Clear the auto-generated ID so server uses the matched resource's ID
        updatedPatient.Id = string.Empty;

        // Act - PUT /{resourceType}?identifier=... (should update existing)
        var response = await Harness.PutResourceWithQueryAsync(
            updatedPatient,
            $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "server should return 200 OK when updating existing resource");

        var returnedPatient = await Harness.ParseResourceResponseAsync(response);
        returnedPatient.Id.ShouldBe(existingId, "server should preserve the existing resource ID");

        // Verify the name was updated
        var givenName = returnedPatient.MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        givenName.ShouldBe("Charles", "server should update the resource content");

        // Verify version was incremented
        var versionId = returnedPatient.MutableNode["meta"]?["versionId"]?.GetValue<string>();
        versionId.ShouldNotBeNullOrEmpty();
        int.Parse(versionId!).ShouldBeGreaterThan(1, "version should be incremented after update");
    }

    /// <summary>
    /// Tests conditional update when one match exists and the resource body contains the correct matching ID.
    /// Expected: Updates the existing resource successfully (200 OK).
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: When updating an existing resource, if the body contains an ID
    /// that matches the found resource, the update proceeds normally.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalUpdate_WhenOneMatchWithCorrectId_ThenUpdatesExistingResource()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        // Create existing patient
        var existingPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("CorrectIdTest")
            .WithGivenName("David")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        var createdResources = await Harness.CreateResourcesAsync([existingPatient]);
        var existingId = createdResources[0].Id;

        // Create update with matching ID in body
        var updatedPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("CorrectIdTest")
            .WithGivenName("Dave") // Changed name
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        updatedPatient.Id = existingId; // Set matching ID

        // Act - PUT /{resourceType}?identifier=... (with correct ID in body)
        var response = await Harness.PutResourceWithQueryAsync(
            updatedPatient,
            $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "server should update resource when body ID matches existing ID");

        var returnedPatient = await Harness.ParseResourceResponseAsync(response);
        returnedPatient.Id.ShouldBe(existingId);

        var givenName = returnedPatient.MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        givenName.ShouldBe("Dave", "server should update the resource content");
    }

    /// <summary>
    /// Tests conditional update when one match exists but the resource body contains a different ID.
    /// Expected: Returns 400 Bad Request.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: If the body contains an ID that differs from the matched resource,
    /// the server returns 400 Bad Request (this is a malformed request).
    /// </remarks>
    [Fact]
    public async Task GivenConditionalUpdate_WhenOneMatchWithIncorrectId_ThenReturnsBadRequest()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        // Create existing patient
        var existingPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("MismatchIdTest")
            .WithGivenName("Eve")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        var createdResources = await Harness.CreateResourcesAsync([existingPatient]);
        var existingId = createdResources[0].Id;

        // Create update with DIFFERENT ID in body
        var updatedPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("MismatchIdTest")
            .WithGivenName("Evelyn")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        updatedPatient.Id = Guid.NewGuid().ToString("N"); // Different ID

        // Act - PUT /{resourceType}?identifier=... (with incorrect ID in body)
        var response = await Harness.PutResourceWithQueryAsync(
            updatedPatient,
            $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "server should return 400 when body ID differs from matched resource ID");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson!.ShouldContain("OperationOutcome", customMessage: "error response should include OperationOutcome");
    }

    /// <summary>
    /// Tests conditional update when multiple matching resources exist.
    /// Expected: Returns 412 Precondition Failed with OperationOutcome.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: If multiple matches are found, the server returns 412 Precondition Failed
    /// with an OperationOutcome describing the issue.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalUpdate_WhenMultipleMatches_ThenReturnsPreconditionFailed()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var sharedFamilyName = $"SharedUpdateFamily{Guid.NewGuid().ToString("N")[..8]}";

        // Create 2 patients with the same family name
        var patient1 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Frank")
            .Build();

        var patient2 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Grace")
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2]);

        // Create update request
        var updatedPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Updated")
            .Build();

        // Clear the auto-generated ID so server determines outcome from search results
        updatedPatient.Id = string.Empty;

        // Act - PUT /{resourceType}?family=...&_tag=... (matches 2 resources within this test's tag)
        var response = await Harness.PutResourceWithQueryAsync(
            updatedPatient,
            $"family={sharedFamilyName}&_tag={tag}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, "server should return 412 when search returns multiple matches");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson!.ShouldContain("OperationOutcome", customMessage: "error response should include OperationOutcome");
    }

    /// <summary>
    /// Tests conditional update without search criteria (empty query string).
    /// Expected: Returns 400 Bad Request.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: Conditional update requires search criteria.
    /// PUT /{resourceType} without query parameters is invalid and should return 400.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalUpdate_WhenNoSearchCriteria_ThenReturnsBadRequest()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("NoSearchCriteria")
            .WithGivenName("Henry")
            .Build();

        // Act - PUT /{resourceType} (no query string)
        var response = await Harness.PutResourceWithQueryAsync(patient, "");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "server should return 400 when no search criteria provided");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson!.ShouldContain("OperationOutcome", customMessage: "error response should include OperationOutcome");
    }

    /// <summary>
    /// Tests conditional update on Bundle resource type.
    /// Expected: Returns 400 Bad Request (Bundle not allowed in conditional operations).
    /// </summary>
    /// <remarks>
    /// Bundle resources are not permitted in conditional update operations per FHIR specification.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalUpdate_WhenBundle_ThenReturnsBadRequest()
    {
        // Arrange
        var bundleIdentifier = Guid.NewGuid().ToString();
        var bundleJson = $$"""
        {
            "resourceType": "Bundle",
            "type": "collection",
            "identifier": {
                "system": "http://example.org/bundle-id",
                "value": "{{bundleIdentifier}}"
            }
        }
        """;

        var resourceNode = Ignixa.Serialization.JsonSourceNodeFactory.Parse<Ignixa.Serialization.SourceNodes.ResourceJsonNode>(bundleJson);

        // Act - PUT /Bundle?identifier=... (conditional update on Bundle)
        var response = await Harness.PutResourceWithQueryAsync(
            resourceNode,
            $"identifier=http://example.org/bundle-id|{bundleIdentifier}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "server should reject conditional operations on Bundle resources");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson!.ShouldContain("OperationOutcome", customMessage: "error response should include OperationOutcome");
    }
}
