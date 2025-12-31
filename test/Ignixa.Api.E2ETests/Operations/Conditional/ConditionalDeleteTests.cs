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
/// E2E tests for FHIR conditional delete operations.
/// Tests validate the behavior defined in FHIR R4 Section 3.1.0.6 (Conditional Delete).
/// </summary>
/// <remarks>
/// Conditional delete enables deleting resources based on search criteria:
///
/// Single Mode (no _count parameter):
/// - 0 matches: 404 Not Found
/// - 1 match: 204 No Content (deletes resource)
/// - Multiple matches: 412 Precondition Failed
///
/// Multiple Mode (with _count parameter):
/// - 0 matches: 404 Not Found
/// - 1+ matches: 200 OK with OperationOutcome showing deleted count
///
/// Test Coverage:
/// - Single mode tests (no _count param)
/// - Multiple mode tests (with _count param)
/// - Edge cases (no search criteria, Bundle rejection)
///
/// Reference:
/// - http://hl7.org/fhir/R4/http.html#cdelete
/// </remarks>
public class ConditionalDeleteTests : CapabilityDrivenTestBase
{
    public ConditionalDeleteTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests conditional delete when no matching resources exist.
    /// Expected: Returns 404 Not Found.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: If the search returns zero matches, the server returns 404 Not Found.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalDelete_WhenNoMatch_ThenReturnsNotFound()
    {
        // Arrange
        var uniqueIdentifier = Guid.NewGuid().ToString();

        // Act - DELETE /Patient?identifier=system|nonexistent-value
        var response = await Harness.DeleteWithQueryAsync(
            "Patient",
            $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "server should return 404 when search returns 0 matches");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldContain("OperationOutcome", customMessage: "error response should include OperationOutcome");
    }

    /// <summary>
    /// Tests conditional delete when exactly one matching resource exists.
    /// Expected: Deletes the resource and returns 204 No Content.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: If one match is found, the server deletes the resource
    /// and returns 204 No Content.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalDelete_WhenOneMatch_ThenDeletesResource()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("DeleteTest")
            .WithGivenName("Alice")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        var createdResources = await Harness.CreateResourcesAsync([patient]);
        var patientId = createdResources[0].Id;

        // Act - DELETE /Patient?identifier=system|value (1 match)
        var response = await Harness.DeleteWithQueryAsync(
            "Patient",
            $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, "server should return 204 when deleting 1 match in single mode");

        // Verify resource is deleted - search should return 0 results
        var searchResults = await Harness.SearchAsync("Patient", $"_id={patientId}");
        searchResults.Length.ShouldBe(0, "deleted resource should not be found in search");
    }

    /// <summary>
    /// Tests conditional delete when multiple matching resources exist in single mode.
    /// Expected: Returns 412 Precondition Failed without deleting anything.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: If multiple matches are found in single mode (no _count),
    /// the server returns 412 Precondition Failed with an OperationOutcome.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalDelete_WhenMultipleMatchesInSingleMode_ThenReturnsPreconditionFailed()
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
            .WithGivenName("Bob")
            .Build();

        var patient2 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Charlie")
            .Build();

        var createdResources = await Harness.CreateResourcesAsync([patient1, patient2]);

        // Act - DELETE /Patient?family=SharedFamily&_tag=... (2 matches within this test's tag, no _count parameter)
        var response = await Harness.DeleteWithQueryAsync(
            "Patient",
            $"family={sharedFamilyName}&_tag={tag}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, "server should return 412 when multiple matches found in single mode");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldContain("OperationOutcome", customMessage: "error response should include OperationOutcome");

        // Verify no resources were deleted - both should still exist
        var searchResults = await Harness.SearchAsync("Patient", $"_tag={tag}");
        searchResults.Length.ShouldBe(2, "no resources should be deleted when multiple matches occur in single mode");
    }

    /// <summary>
    /// Tests conditional delete with _count parameter when matches exist.
    /// Expected: Deletes up to _count resources and returns 200 OK with OperationOutcome.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: When _count parameter is provided (multiple mode),
    /// the server deletes up to _count matching resources and returns 200 OK with OperationOutcome.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalDeleteWithCount_WhenMatchesExist_ThenDeletesUpToCount()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var sharedFamilyName = $"MultiDelete{Guid.NewGuid().ToString("N")[..8]}";

        // Create 3 patients with the same family name
        var patient1 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("David")
            .Build();

        var patient2 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Eve")
            .Build();

        var patient3 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Frank")
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - DELETE /Patient?family=MultiDelete&_tag=...&_count=2 (3 matches within this test's tag, delete 2)
        var response = await Harness.DeleteWithQueryAsync(
            "Patient",
            $"family={sharedFamilyName}&_tag={tag}&_count=2");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "server should return 200 OK in multiple mode");

        // Verify OperationOutcome is returned
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldContain("OperationOutcome");
        responseJson.ShouldContain("Deleted 2", customMessage: "OperationOutcome should show 2 resources deleted");

        // Verify exactly 2 resources were deleted - 1 should remain
        var searchResults = await Harness.SearchAsync("Patient", $"_tag={tag}");
        searchResults.Length.ShouldBe(1, "exactly 1 resource should remain after deleting 2 of 3");
    }

    /// <summary>
    /// Tests conditional delete with _count parameter when fewer matches exist than count.
    /// Expected: Deletes all matching resources and returns 200 OK with OperationOutcome.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: If fewer matches exist than _count, all matches are deleted.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalDeleteWithCount_WhenFewerMatchesThanCount_ThenDeletesAll()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier1 = Guid.NewGuid().ToString();
        var uniqueIdentifier2 = Guid.NewGuid().ToString();
        var uniqueIdentifier3 = Guid.NewGuid().ToString();

        var patient1 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("LimitedDelete")
            .WithGivenName("George")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier1)
            .Build();

        var patient2 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("LimitedDelete")
            .WithGivenName("Hannah")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier2)
            .Build();

        var patient3 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("LimitedDelete")
            .WithGivenName("Ivan")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier3)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - DELETE /Patient?_tag={tag}&_count=10 (3 matches, count=10, delete all 3)
        var response = await Harness.DeleteWithQueryAsync(
            "Patient",
            $"_tag={tag}&_count=10");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "server should return 200 OK in multiple mode");

        // Verify OperationOutcome shows all 3 deleted
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldContain("Deleted 3", customMessage: "OperationOutcome should show 3 resources deleted");

        // Verify all resources were deleted
        var searchResults = await Harness.SearchAsync("Patient", $"_tag={tag}");
        searchResults.Length.ShouldBe(0, "all 3 resources should be deleted");
    }

    /// <summary>
    /// Tests conditional delete without search criteria.
    /// Expected: Returns 400 Bad Request.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: Conditional operations require search parameters.
    /// DELETE /Patient with no query string should be rejected.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalDelete_WhenNoSearchCriteria_ThenReturnsBadRequest()
    {
        // Act - DELETE /Patient (no query string)
        var response = await Harness.DeleteWithQueryAsync("Patient", string.Empty);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "server should return 400 when no search criteria provided");
    }

    /// <summary>
    /// Tests conditional delete on Bundle resource type.
    /// Expected: Returns 400 Bad Request.
    /// </summary>
    /// <remarks>
    /// FHIR R4 Section 3.1.0.6: Bundle resources cannot be used in conditional operations.
    /// This prevents accidental deletion of transaction bundles.
    /// </remarks>
    [Fact]
    public async Task GivenConditionalDelete_WhenBundle_ThenReturnsBadRequest()
    {
        // Act - DELETE /Bundle?identifier=xxx
        var response = await Harness.DeleteWithQueryAsync(
            "Bundle",
            $"identifier=http://example.org|bundle-123");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "server should return 400 when attempting conditional delete on Bundle");

        // Verify error message mentions selectivity
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldContain("not selective enough", customMessage: "error message should indicate Bundle is not allowed");
    }

    /// <summary>
    /// Tests conditional delete with _count=0.
    /// Expected: Returns 200 OK with 0 resources deleted.
    /// </summary>
    [Fact]
    public async Task GivenConditionalDeleteWithCount_WhenCountIsZero_ThenDeletesNothing()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var sharedFamilyName = $"ZeroCount{Guid.NewGuid().ToString("N")[..8]}";

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Jane")
            .Build();

        await Harness.CreateResourcesAsync([patient]);

        // Act - DELETE /Patient?family=ZeroCount&_tag=...&_count=0 (1 match within this test's tag, count=0, delete 0)
        var response = await Harness.DeleteWithQueryAsync(
            "Patient",
            $"family={sharedFamilyName}&_tag={tag}&_count=0");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "server should return 200 OK even with _count=0");

        // Verify OperationOutcome shows 0 deleted
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldContain("Deleted 0", customMessage: "OperationOutcome should show 0 resources deleted");

        // Verify resource still exists
        var searchResults = await Harness.SearchAsync("Patient", $"_tag={tag}");
        searchResults.Length.ShouldBe(1, "resource should not be deleted when _count=0");
    }

    /// <summary>
    /// Tests conditional delete with _count=1 behaves like multiple mode.
    /// Expected: Returns 200 OK with OperationOutcome (not 204).
    /// </summary>
    [Fact]
    public async Task GivenConditionalDeleteWithCount_WhenCountIsOne_ThenReturnsOperationOutcome()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var uniqueIdentifier = Guid.NewGuid().ToString();

        var patient = CreatePatient()
            .WithTag(tag)
            .WithFamilyName("CountOne")
            .WithGivenName("Kate")
            .WithIdentifier("http://hospital.example.org/mrn", uniqueIdentifier)
            .Build();

        await Harness.CreateResourcesAsync([patient]);

        // Act - DELETE /Patient?identifier=system|value&_count=1 (1 match, count=1)
        var response = await Harness.DeleteWithQueryAsync(
            "Patient",
            $"identifier=http://hospital.example.org/mrn|{uniqueIdentifier}&_count=1");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "server should return 200 OK in multiple mode (even with _count=1)");

        // Verify OperationOutcome is returned (not 204 No Content)
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldContain("OperationOutcome");
        responseJson.ShouldContain("Deleted 1", customMessage: "OperationOutcome should show 1 resource deleted");

        // Verify resource is deleted
        var searchResults = await Harness.SearchAsync("Patient", $"_tag={tag}");
        searchResults.Length.ShouldBe(0, "resource should be deleted");
    }

    /// <summary>
    /// Tests conditional delete with complex search criteria (multiple parameters).
    /// Expected: Deletes only resources matching all criteria.
    /// </summary>
    [Fact]
    public async Task GivenConditionalDelete_WhenComplexSearchCriteria_ThenDeletesMatchingResources()
    {
        // Capability check
        RequireSearchParameters("Patient", "family", "given");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var sharedFamilyName = $"Complex{Guid.NewGuid().ToString("N")[..8]}";

        // Create 3 patients: 2 with family+given match, 1 with only family match
        var patient1 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Leo")
            .Build();

        var patient2 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Leo")
            .Build();

        var patient3 = CreatePatient()
            .WithTag(tag)
            .WithFamilyName(sharedFamilyName)
            .WithGivenName("Maria")  // Different given name
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - DELETE /Patient?family=Complex&given=Leo&_tag=...&_count=10 (2 matches within this test's tag)
        var response = await Harness.DeleteWithQueryAsync(
            "Patient",
            $"family={sharedFamilyName}&given=Leo&_tag={tag}&_count=10");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "server should return 200 OK");

        // Verify 2 deleted
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldContain("Deleted 2", customMessage: "OperationOutcome should show 2 resources deleted");

        // Verify only 1 resource remains (Maria)
        var searchResults = await Harness.SearchAsync("Patient", $"_tag={tag}");
        searchResults.Length.ShouldBe(1, "only 1 resource should remain (Maria)");

        var remainingGivenName = searchResults[0].MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        remainingGivenName.ShouldBe("Maria", "only Maria should remain");
    }
}
