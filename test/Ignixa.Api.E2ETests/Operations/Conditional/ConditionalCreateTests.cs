// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.Api.E2ETests.Operations.Conditional;

/// <summary>
/// E2E tests for FHIR conditional create operations (If-None-Exist header).
/// Tests validate the behavior defined in FHIR R4 Section 3.1.0.5 (Conditional Create).
/// </summary>
/// <remarks>
/// Conditional create enables idempotent resource creation based on search criteria:
/// - 0 matches: Create new resource (201 Created)
/// - 1 match: Return existing resource (200 OK, idempotent)
/// - Multiple matches: Return error (412 Precondition Failed)
///
/// Test Coverage (identified as HIGH priority in e2e-test-gap-analysis.md):
/// - Conditional create with no matches (creates new)
/// - Conditional create with one match (returns existing)
/// - Conditional create with multiple matches (returns 412)
/// - Conditional create with various search parameters (identifier, name, etc.)
///
/// Reference:
/// - docs/investigations/e2e-test-gap-analysis.md (lines 211-269)
/// - docs/investigations/ADR-2525-conditional-operations.md
/// - http://hl7.org/fhir/R4/http.html#ccreate
/// </remarks>
public class ConditionalOperationTests : CapabilityDrivenTestBase
{
    public ConditionalOperationTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Conditional Create - No Match

    /// <summary>
    /// Tests conditional create when no matching resources exist.
    /// Expected: Creates a new resource and returns 201 Created.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.5: If the search returns zero matches, the server processes the create
    /// as per normal FHIR processing, creating a new resource with a server-assigned ID.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalCreate_WhenNoMatch_ThenCreatesNewResource()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("NoMatchTest")
            .WithGivenName("Alice")
            .Build();

        // Add a unique identifier that won't match any existing resource
        patient.MutableNode["identifier"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "http://hospital.example.org/mrn",
                ["value"] = uniqueIdentifier
            }
        };

        // Act - POST with If-None-Exist header using identifier search
        var response = await Harness.PostResourceWithHeadersAsync(
            patient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, "server should create new resource when search returns 0 matches");
        response.Headers.Location.Should().NotBeNull("Location header should be present");
        response.Headers.Location!.ToString().Should().Contain("Patient/", "Location should reference the created Patient resource");

        var createdPatient = await Harness.ParseResourceResponseAsync(response);
        createdPatient.ResourceType.Should().Be("Patient");
        createdPatient.Id.Should().NotBeNullOrEmpty();

        // Verify the identifier was preserved
        var identifiers = createdPatient.MutableNode["identifier"]?.AsArray();
        identifiers.Should().NotBeNull();

        var matchingIdentifier = identifiers!.FirstOrDefault(i =>
            i!["system"]?.GetValue<string>() == "http://hospital.example.org/mrn" &&
            i!["value"]?.GetValue<string>() == uniqueIdentifier);
        matchingIdentifier.Should().NotBeNull("identifier should be preserved in created resource");
    }

    /// <summary>
    /// Tests conditional create with _id parameter when no match exists.
    /// Expected: Creates a new resource with server-assigned ID.
    /// </summary>
    [Fact]
    public async Task GivenConditionalCreate_WhenNoMatchById_ThenCreatesNewResource()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var nonExistentId = Guid.NewGuid().ToString("N");

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("Smith")
            .WithGivenName("Bob")
            .Build();

        // Act - POST with If-None-Exist: _id=guid-that-doesnt-exist
        var response = await Harness.PostResourceWithHeadersAsync(
            patient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"_id={nonExistentId}"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var createdPatient = await Harness.ParseResourceResponseAsync(response);
        createdPatient.Id.Should().NotBe(nonExistentId, "server should assign new ID, not use the search parameter ID");
    }

    #endregion

    #region Conditional Create - One Match

    /// <summary>
    /// Tests conditional create when exactly one matching resource exists.
    /// Expected: Returns the existing resource with 200 OK (idempotent behavior).
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.5: If one match is found, the server returns 200 OK with the existing
    /// resource in the body. No new version is created.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalCreate_WhenOneMatch_ThenReturnsExisting()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        // Create the first patient with a unique identifier
        var existingPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("Jones")
            .WithGivenName("Charlie")
            .Build();

        existingPatient.MutableNode["identifier"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "http://hospital.example.org/mrn",
                ["value"] = uniqueIdentifier
            }
        };

        var createdResources = await Harness.CreateResourcesAsync([existingPatient]);
        var existingId = createdResources[0].Id;
        var existingVersionId = createdResources[0].MutableNode["meta"]?["versionId"]?.GetValue<string>();

        // Create a second patient with the SAME identifier (but different name)
        var duplicatePatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("Jones")
            .WithGivenName("David") // Different given name
            .Build();

        duplicatePatient.MutableNode["identifier"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "http://hospital.example.org/mrn",
                ["value"] = uniqueIdentifier // Same identifier
            }
        };

        // Act - POST with If-None-Exist using the same identifier
        var response = await Harness.PostResourceWithHeadersAsync(
            duplicatePatient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "server should return 200 OK when search returns 1 match (idempotent)");

        var returnedPatient = await Harness.ParseResourceResponseAsync(response);
        returnedPatient.Id.Should().Be(existingId, "server should return the existing resource ID");
        returnedPatient.MutableNode["meta"]?["versionId"]?.GetValue<string>().Should().Be(existingVersionId, "no new version should be created");

        // Verify it returned the ORIGINAL patient (with "Charlie"), not the duplicate request (with "David")
        var givenName = returnedPatient.MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        givenName.Should().Be("Charlie", "server should return the existing resource, not create a new one");
    }

    /// <summary>
    /// Tests conditional create with family name search when one match exists.
    /// Expected: Returns the existing resource with 200 OK.
    /// </summary>
    [Fact]
    public async Task GivenConditionalCreate_WhenOneMatchByName_ThenReturnsExisting()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueFamilyName = $"UniqueFamily{Guid.NewGuid().ToString("N")[..8]}";

        // Create patient with unique family name
        var existingPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(uniqueFamilyName)
            .WithGivenName("Eve")
            .Build();

        var createdResources = await Harness.CreateResourcesAsync([existingPatient]);
        var existingId = createdResources[0].Id;

        // Create duplicate request with same family name
        var duplicatePatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(uniqueFamilyName)
            .WithGivenName("Frank") // Different given name
            .Build();

        // Act - POST with If-None-Exist: family={uniqueName}
        var response = await Harness.PostResourceWithHeadersAsync(
            duplicatePatient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"family={uniqueFamilyName}"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var returnedPatient = await Harness.ParseResourceResponseAsync(response);
        returnedPatient.Id.Should().Be(existingId);

        // Verify original patient returned (Eve, not Frank)
        var givenName = returnedPatient.MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        givenName.Should().Be("Eve");
    }

    #endregion

    #region Conditional Create - Multiple Matches

    /// <summary>
    /// Tests conditional create when multiple matching resources exist.
    /// Expected: Returns 412 Precondition Failed with OperationOutcome.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.5: If multiple matches are found, the server returns 412 Precondition Failed
    /// with an OperationOutcome describing the issue.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalCreate_WhenMultipleMatches_ThenReturnsPreconditionFailed()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var sharedFamilyName = $"SharedFamily{Guid.NewGuid().ToString("N")[..8]}";

        // Create 2 patients with the SAME family name
        var patient1 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("George")
            .Build();

        var patient2 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Hannah")
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2]);

        // Create a third patient request with the same family name
        var newPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Ivan")
            .Build();

        // Act - POST with If-None-Exist: family={sharedName}
        var response = await Harness.PostResourceWithHeadersAsync(
            newPatient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"family={sharedFamilyName}"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, "server should return 412 when search returns multiple matches");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.Should().Contain("OperationOutcome", "error response should include OperationOutcome");
    }

    /// <summary>
    /// Tests conditional create with gender search when multiple matches exist.
    /// Expected: Returns 412 Precondition Failed.
    /// </summary>
    [Fact]
    public async Task GivenConditionalCreate_WhenMultipleMatchesByGender_ThenReturnsPreconditionFailed()
    {
        // Capability check
        RequireSearchParameter("Patient", "gender");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create 3 patients with the same gender
        var patient1 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("Alpha")
            .WithGender(g => g.Female)
            .Build();

        var patient2 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("Beta")
            .WithGender(g => g.Female)
            .Build();

        var patient3 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("Gamma")
            .WithGender(g => g.Female)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Attempt to create another female patient with conditional create
        var newPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("Delta")
            .WithGender(g => g.Female)
            .Build();

        // Act - POST with If-None-Exist: gender=female&_tag={tag}
        // Note: Using _tag to scope the search to this test's resources
        var response = await Harness.PostResourceWithHeadersAsync(
            newPatient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"gender=female&_tag={tag}"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    #endregion

    #region Conditional Create - Complex Search Criteria

    /// <summary>
    /// Tests conditional create with multiple search parameters (AND logic).
    /// Expected: Creates new resource when no match on combined criteria.
    /// </summary>
    [Fact]
    public async Task GivenConditionalCreate_WhenNoMatchOnCombinedCriteria_ThenCreatesNewResource()
    {
        // Capability check
        RequireSearchParameters("Patient", "family", "given");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueFamily = $"Family{Guid.NewGuid().ToString("N")[..8]}";

        // Create patient: family=UniqueFamily, given=John
        var existingPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(uniqueFamily)
            .WithGivenName("John")
            .Build();

        await Harness.CreateResourcesAsync([existingPatient]);

        // Attempt to create: family=UniqueFamily, given=Jane (different given name)
        var newPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(uniqueFamily)
            .WithGivenName("Jane")
            .Build();

        // Act - POST with If-None-Exist: family={family}&given=Jane (AND logic, no match)
        var response = await Harness.PostResourceWithHeadersAsync(
            newPatient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"family={uniqueFamily}&given=Jane"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, "combined criteria should not match existing patient (John != Jane)");

        var createdPatient = await Harness.ParseResourceResponseAsync(response);
        var givenName = createdPatient.MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        givenName.Should().Be("Jane");
    }

    /// <summary>
    /// Tests conditional create with multiple search parameters (AND logic) when match exists.
    /// Expected: Returns existing resource when all criteria match.
    /// </summary>
    [Fact]
    public async Task GivenConditionalCreate_WhenMatchOnCombinedCriteria_ThenReturnsExisting()
    {
        // Capability check
        RequireSearchParameters("Patient", "family", "given");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueFamily = $"Family{Guid.NewGuid().ToString("N")[..8]}";

        // Create patient: family=UniqueFamily, given=Kate
        var existingPatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(uniqueFamily)
            .WithGivenName("Kate")
            .Build();

        var createdResources = await Harness.CreateResourcesAsync([existingPatient]);
        var existingId = createdResources[0].Id;

        // Attempt to create: family=UniqueFamily, given=Kate (exact match)
        var duplicatePatient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(uniqueFamily)
            .WithGivenName("Kate")
            .Build();

        // Act - POST with If-None-Exist: family={family}&given=Kate (AND logic, match)
        var response = await Harness.PostResourceWithHeadersAsync(
            duplicatePatient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"family={uniqueFamily}&given=Kate"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "combined criteria should match existing patient");

        var returnedPatient = await Harness.ParseResourceResponseAsync(response);
        returnedPatient.Id.Should().Be(existingId);
    }

    #endregion

    #region Conditional Create - Edge Cases

    /// <summary>
    /// Tests conditional create without If-None-Exist header.
    /// Expected: Normal create behavior (201 Created).
    /// </summary>
    [Fact]
    public async Task GivenConditionalCreate_WhenNoIfNoneExistHeader_ThenCreatesNewResource()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("NormalCreate")
            .WithGivenName("Leo")
            .Build();

        // Act - POST without If-None-Exist header (normal create)
        var response = await Harness.PostResourceWithHeadersAsync(patient);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var createdPatient = await Harness.ParseResourceResponseAsync(response);
        createdPatient.ResourceType.Should().Be("Patient");
    }

    /// <summary>
    /// Tests that conditional create preserves resource content from request body.
    /// Expected: Created resource matches the request body (not modified by server).
    /// </summary>
    [Fact]
    public async Task GivenConditionalCreate_WhenCreating_ThenPreservesResourceContent()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();
        var birthDate = "1990-05-15";

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("ContentTest")
            .WithGivenName("Maria")
            .Build();

        // Add birthDate and identifier
        patient.MutableNode["birthDate"] = birthDate;
        patient.MutableNode["identifier"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "http://hospital.example.org/mrn",
                ["value"] = uniqueIdentifier
            }
        };

        // Act - POST with If-None-Exist (no match, will create)
        var response = await Harness.PostResourceWithHeadersAsync(
            patient,
            new Dictionary<string, string>
            {
                ["If-None-Exist"] = $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdPatient = await Harness.ParseResourceResponseAsync(response);

        // Verify all content preserved
        createdPatient.MutableNode["birthDate"]?.GetValue<string>().Should().Be(birthDate);
        var familyName = createdPatient.MutableNode["name"]?[0]?["family"]?.GetValue<string>();
        familyName.Should().Be("ContentTest");
        var givenName = createdPatient.MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        givenName.Should().Be("Maria");
    }

    #endregion
}
