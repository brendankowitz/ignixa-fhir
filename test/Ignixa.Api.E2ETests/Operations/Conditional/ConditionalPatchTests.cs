// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Operations.Conditional;

/// <summary>
/// E2E tests for FHIR conditional patch operations.
/// Tests validate the behavior defined in FHIR R4 Section 3.1.0.7 (Conditional Patch).
/// </summary>
/// <remarks>
/// Conditional patch enables targeted resource patching based on search criteria:
/// - 0 matches: Return 404 Not Found (different from conditional update which creates!)
/// - 1 match: Patch existing resource (200 OK)
/// - Multiple matches: Return error (412 Precondition Failed)
/// - No search criteria: Return error (412 Precondition Failed)
///
/// This server only supports FHIRPath Patch (Parameters resource), NOT JSON Patch (RFC 6902).
///
/// Test Coverage:
/// - Conditional patch with no matches (returns 404)
/// - Conditional patch with one match (patches resource)
/// - Conditional patch with multiple matches (returns 412)
/// - Conditional patch with no search criteria (returns 412)
/// - Conditional patch on Bundle resource type (returns 400)
/// - Verification of actual patch application (gender change)
///
/// Reference:
/// - http://hl7.org/fhir/R4/http.html#cond-patch
/// - FHIR R4 Section 3.1.0.7.1 (FHIRPath Patch)
/// </remarks>
public class ConditionalPatchTests : CapabilityDrivenTestBase
{
    public ConditionalPatchTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests conditional patch when no matching resources exist.
    /// Expected: Returns 404 Not Found (different from conditional update which creates!).
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.7: If the search returns zero matches, the server returns 404 Not Found.
    /// This differs from conditional update (PUT) which creates a new resource when no matches exist.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalPatch_WhenNoMatch_ThenReturnsNotFound()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var nonExistentIdentifier = Guid.NewGuid().ToString();

        var patchDocument = CreateGenderPatchDocument("female");

        // Act - PATCH /Patient?identifier=nonexistent-id
        var response = await Harness.PatchWithQueryAsync(
            "Patient",
            $"identifier=http://test.example.org/mrn|{nonExistentIdentifier}",
            patchDocument);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "server should return 404 when search returns 0 matches");
    }

    /// <summary>
    /// Tests conditional patch when exactly one matching resource exists.
    /// Expected: Patches the existing resource and returns 200 OK with the updated resource.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.7: If one match is found, the server applies the patch operations
    /// to the matched resource and returns 200 OK with the updated resource.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalPatch_WhenOneMatch_ThenPatchesResource()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        // Create patient with unique identifier and male gender
        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("Smith")
            .WithGivenName("John")
            .WithGender(g => g.Male)
            .WithIdentifier("http://test.example.org/mrn", uniqueIdentifier)
            .Build();

        var createdPatients = await Harness.CreateResourcesAsync([patient]);
        var existingId = createdPatients[0].Id;

        var patchDocument = CreateGenderPatchDocument("female");

        // Act - PATCH /Patient?identifier=unique-id
        var response = await Harness.PatchWithQueryAsync(
            "Patient",
            $"identifier=http://test.example.org/mrn|{uniqueIdentifier}",
            patchDocument);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "server should return 200 OK when search returns 1 match");

        var patchedPatient = await Harness.ParseResourceResponseAsync(response);
        patchedPatient.Id.ShouldBe(existingId, "patched resource should have same ID as existing resource");

        // Verify gender was changed to female
        var gender = patchedPatient.MutableNode["gender"]?.GetValue<string>();
        gender.ShouldBe("female", "gender should be patched to 'female'");

        // Verify other fields unchanged
        var familyName = patchedPatient.MutableNode["name"]?[0]?["family"]?.GetValue<string>();
        familyName.ShouldBe("Smith", "family name should be unchanged");
    }

    /// <summary>
    /// Tests conditional patch when multiple matching resources exist.
    /// Expected: Returns 412 Precondition Failed (ambiguous condition).
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.7: If multiple matches are found, the server returns 412 Precondition Failed
    /// because the condition is not selective enough to uniquely identify a single resource.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalPatch_WhenMultipleMatches_ThenReturnsPreconditionFailed()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var sharedFamilyName = $"SharedFamily{Guid.NewGuid().ToString("N")[..8]}";

        // Create 2 patients with the same family name
        var patient1 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Alice")
            .WithGender(g => g.Male)
            .Build();

        var patient2 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Bob")
            .WithGender(g => g.Male)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2]);

        var patchDocument = CreateGenderPatchDocument("female");

        // Act - PATCH /Patient?family=shared-name&_tag=... (multiple matches within this test's tag)
        var response = await Harness.PatchWithQueryAsync(
            "Patient",
            $"family={sharedFamilyName}&_tag={tag}",
            patchDocument);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, "server should return 412 when search returns multiple matches");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson!.ShouldContain("OperationOutcome", customMessage: "error response should include OperationOutcome");
    }

    /// <summary>
    /// Tests conditional patch with no search criteria in query string.
    /// Expected: Returns 400 Bad Request (consistent with conditional update/delete).
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.7: Conditional patch requires search parameters in the query string.
    /// Patching without conditions is not allowed (would be ambiguous/dangerous).
    /// Missing search criteria is a malformed request, hence 400 Bad Request (not 412 Precondition Failed).
    /// </remarks>
    [Fact]
    public async Task GivenConditionalPatch_WhenNoSearchCriteria_ThenReturnsBadRequest()
    {
        // Arrange
        var patchDocument = CreateGenderPatchDocument("female");

        // Act - PATCH /Patient (no query string)
        var response = await Harness.PatchWithQueryAsync(
            "Patient",
            string.Empty,
            patchDocument);

        // Assert - 400 Bad Request (consistent with conditional update/delete)
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "server should return 400 when no search criteria provided");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson!.ShouldContain("OperationOutcome", customMessage: "error response should include OperationOutcome");
    }

    /// <summary>
    /// Tests conditional patch on Bundle resource type.
    /// Expected: Returns 400 Bad Request (Bundle cannot be used in conditional operations).
    /// </summary>
    /// <remarks>
    /// FHIR R4: Bundle resources cannot be used in conditional operations because they are
    /// not selective enough (bundles are collections, not individual clinical resources).
    /// </remarks>
    [Fact]
    public async Task GivenConditionalPatch_WhenBundle_ThenReturnsBadRequest()
    {
        // Arrange
        var uniqueIdentifier = Guid.NewGuid().ToString();
        var patchDocument = CreateGenderPatchDocument("female");

        // Act - PATCH /Bundle?identifier=xxx
        var response = await Harness.PatchWithQueryAsync(
            "Bundle",
            $"identifier={uniqueIdentifier}",
            patchDocument);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "server should return 400 for Bundle conditional operations");

        // Verify OperationOutcome mentions "not selective enough"
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson!.ShouldContain("OperationOutcome");
        responseJson!.ShouldContain("not selective enough", Case.Insensitive);
    }

    /// <summary>
    /// Tests that conditional patch actually applies the patch operations correctly.
    /// Verifies the FHIRPath Patch replace operation changes the gender field.
    /// </summary>
    /// <remarks>
    /// This test ensures the patch is not just matching the resource, but actually
    /// applying the FHIRPath Patch operations defined in the Parameters resource.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalPatch_WhenPatchChangesGender_ThenResourceIsUpdated()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueFamilyName = $"UniqueFamily{Guid.NewGuid().ToString("N")[..8]}";

        // Create patient with male gender
        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(uniqueFamilyName)
            .WithGivenName("Charlie")
            .WithGender(g => g.Male)
            .Build();

        var createdPatients = await Harness.CreateResourcesAsync([patient]);
        var existingId = createdPatients[0].Id;
        var originalVersionId = createdPatients[0].MutableNode["meta"]?["versionId"]?.GetValue<string>();

        // Verify original gender is male
        var originalGender = createdPatients[0].MutableNode["gender"]?.GetValue<string>();
        originalGender.ShouldBe("male", "original gender should be male");

        var patchDocument = CreateGenderPatchDocument("female");

        // Act - PATCH /Patient?family=unique-name&_tag=... (single match within this test's tag)
        var response = await Harness.PatchWithQueryAsync(
            "Patient",
            $"family={uniqueFamilyName}&_tag={tag}",
            patchDocument);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var patchedPatient = await Harness.ParseResourceResponseAsync(response);
        patchedPatient.Id.ShouldBe(existingId);

        // Verify gender was changed to female
        var newGender = patchedPatient.MutableNode["gender"]?.GetValue<string>();
        newGender.ShouldBe("female", "gender should be patched to 'female'");

        // Verify version ID was incremented (resource was updated)
        var newVersionId = patchedPatient.MutableNode["meta"]?["versionId"]?.GetValue<string>();
        newVersionId.ShouldNotBe(originalVersionId, "version ID should be incremented after patch");

        // Verify other fields unchanged
        var familyName = patchedPatient.MutableNode["name"]?[0]?["family"]?.GetValue<string>();
        familyName.ShouldBe(uniqueFamilyName);
        var givenName = patchedPatient.MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        givenName.ShouldBe("Charlie");
    }

    /// <summary>
    /// Creates a FHIRPath Patch Parameters document for replacing the Patient.gender field.
    /// </summary>
    /// <param name="newGender">The new gender value (male, female, other, unknown).</param>
    /// <returns>A Parameters resource containing the patch operation.</returns>
    /// <remarks>
    /// FHIRPath Patch format (FHIR R4 Section 3.1.0.7.1):
    /// {
    ///   "resourceType": "Parameters",
    ///   "parameter": [
    ///     {
    ///       "name": "operation",
    ///       "part": [
    ///         { "name": "type", "valueCode": "replace" },
    ///         { "name": "path", "valueString": "Patient.gender" },
    ///         { "name": "value", "valueCode": "female" }
    ///       ]
    ///     }
    ///   ]
    /// }
    /// </remarks>
    private static ResourceJsonNode CreateGenderPatchDocument(string newGender)
    {
        var patchDocument = new ResourceJsonNode();
        patchDocument.MutableNode["resourceType"] = "Parameters";
        patchDocument.MutableNode["parameter"] = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "operation",
                ["part"] = new JsonArray
                {
                    new JsonObject { ["name"] = "type", ["valueCode"] = "replace" },
                    new JsonObject { ["name"] = "path", ["valueString"] = "Patient.gender" },
                    new JsonObject { ["name"] = "value", ["valueCode"] = newGender }
                }
            }
        };

        return patchDocument;
    }
}
