// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Shouldly;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// E2E coverage for the <c>$bulk-delete</c> operation against <see cref="BulkDeleteApiFixture"/>'s tenant 1
/// (SQL storage, SqlServer DurableTask backend -- see that fixture's remarks for why this suite does not
/// use the shared fixture's default FileSystem provider; default <c>BulkDelete:BatchSize</c>). Every test
/// uses a unique <c>_tag</c> code so tests never collide with each other or with other suites sharing this
/// database -- no test here ever issues an unfiltered delete.
/// </summary>
[Collection(BulkDeleteTestCollection.Name)]
public class BulkDeleteTests(BulkDeleteApiFixture fixture)
{
    private readonly BulkDeleteClient _bulkDelete = new(fixture.Client);

    [Fact]
    public async Task GivenTypeLevelSoftDeleteWithTagFilter_WhenCompleted_ThenMatchingPatientsAreGoneAndOthersRemain()
    {
        var tag = NewTag();
        var otherTag = NewTag();
        var deletedId1 = await PutPatientAsync(tag);
        var deletedId2 = await PutPatientAsync(tag);
        var survivorId = await PutPatientAsync(otherTag);

        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(2);

        foreach (var id in new[] { deletedId1, deletedId2 })
        {
            (await GetAsync($"/tenant/1/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.Gone);
            // Soft delete creates a tombstone: the resource's version history still exists.
            (await GetAsync($"/tenant/1/Patient/{id}/_history")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await GetAsync($"/tenant/1/Patient/{survivorId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenSystemLevelHardDeleteWithTypeAndTagFilter_WhenCompleted_ThenBothTypesAreHardDeleted()
    {
        var tag = NewTag();
        var patientId = await PutPatientAsync(tag);
        var observationId = await PutObservationAsync(tag, subjectId: null);

        using var kickoff = await _bulkDelete.KickoffAsync(
            $"/tenant/1/$bulk-delete?_type=Patient,Observation&_tag={tag}",
            bodyJson: """{"resourceType":"Parameters","parameter":[{"name":"hardDelete","valueBoolean":true},{"name":"purgeHistory","valueBoolean":true}]}""");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        var counts = BulkDeleteClient.GetCounts(body);
        counts["Patient"].ShouldBe(1);
        counts["Observation"].ShouldBe(1);

        foreach (var (type, id) in new[] { ("Patient", patientId), ("Observation", observationId) })
        {
            // Hard delete: 404, not 410 (no tombstone remains).
            (await GetAsync($"/tenant/1/{type}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            // Document the server's actual behavior for history of an id that no longer has any
            // record at all, rather than assuming either shape.
            using var history = await GetAsync($"/tenant/1/{type}/{id}/_history");
            history.StatusCode.ShouldBe(HttpStatusCode.OK, await history.Content.ReadAsStringAsync());
            var historyBundle = JsonNode.Parse(await history.Content.ReadAsStringAsync())!;
            (historyBundle["entry"]?.AsArray().Count ?? 0).ShouldBe(0);
        }
    }

    [Fact]
    public async Task GivenHardDeleteViaQueryFlag_WhenCompleted_ThenHistoryIsGone()
    {
        var tag = NewTag();
        var id = await PutPatientAsync(tag);

        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}&_hardDelete=true");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(1);

        (await GetAsync($"/tenant/1/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var history = await GetAsync($"/tenant/1/Patient/{id}/_history");
        history.StatusCode.ShouldBe(HttpStatusCode.OK, await history.Content.ReadAsStringAsync());
        JsonNode.Parse(await history.Content.ReadAsStringAsync())!["entry"]?.AsArray().Count.ShouldBe(0);
    }

    [Fact]
    public async Task GivenPurgeHistory_WhenCompleted_ThenOnlyTheCurrentVersionRemains()
    {
        var tag = NewTag();
        var id = Guid.NewGuid().ToString("N");
        await PutPatientWithIdAsync(id, tag, "v1");
        await PutPatientWithIdAsync(id, tag, "v2");
        await PutPatientWithIdAsync(id, tag, "v3");

        using var currentBefore = await GetAsync($"/tenant/1/Patient/{id}");
        var currentVersionId = currentBefore.Headers.ETag!.Tag.Trim('"').TrimStart('W', '/');

        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}&_purgeHistory=true");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(1);

        using var current = await GetAsync($"/tenant/1/Patient/{id}");
        current.StatusCode.ShouldBe(HttpStatusCode.OK);
        current.Headers.ETag!.Tag.Trim('"').TrimStart('W', '/').ShouldBe(currentVersionId);
        JsonNode.Parse(await current.Content.ReadAsStringAsync())!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("v3");

        using var history = await GetAsync($"/tenant/1/Patient/{id}/_history");
        history.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonNode.Parse(await history.Content.ReadAsStringAsync())!["entry"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenASearchFilter_WhenDeleting_ThenOnlyMatchingResourcesAreRemoved()
    {
        var tag = NewTag();
        var matchingId = await PutPatientAsync(tag, family: "Match");
        var nonMatchingId = await PutPatientAsync(tag, family: "NoMatch");

        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}&family=Match");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(1);

        (await GetAsync($"/tenant/1/Patient/{matchingId}")).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await GetAsync($"/tenant/1/Patient/{nonMatchingId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenExcludedResourceTypesAtSystemLevel_WhenDeleting_ThenExcludedTypeSurvives()
    {
        var tag = NewTag();
        var patientId = await PutPatientAsync(tag);
        var observationId = await PutObservationAsync(tag, subjectId: null);

        using var kickoff = await _bulkDelete.KickoffAsync(
            $"/tenant/1/$bulk-delete?_tag={tag}&excludedResourceTypes=Observation");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        var counts = BulkDeleteClient.GetCounts(body);
        counts["Patient"].ShouldBe(1);
        counts.ContainsKey("Observation").ShouldBeFalse();

        (await GetAsync($"/tenant/1/Patient/{patientId}")).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await GetAsync($"/tenant/1/Observation/{observationId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenRevIncludeAtTypeLevel_WhenHardDeletingPatients_ThenReferencingObservationsAreDeletedToo()
    {
        var tag = NewTag();
        var patientId = await PutPatientAsync(tag);
        var observationId = await PutObservationAsync(NewTag(), subjectId: patientId);

        using var kickoff = await _bulkDelete.KickoffAsync(
            $"/tenant/1/Patient/$bulk-delete?_tag={tag}&_revinclude=Observation:subject&_hardDelete=true");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        var counts = BulkDeleteClient.GetCounts(body);
        counts["Patient"].ShouldBe(1);
        counts["Observation"].ShouldBe(1);

        (await GetAsync($"/tenant/1/Patient/{patientId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync($"/tenant/1/Observation/{observationId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenIncludeFromObservation_WhenHardDeleting_ThenReferencedPatientIsDeletedToo()
    {
        var tag = NewTag();
        var patientId = await PutPatientAsync(NewTag());
        var observationId = await PutObservationAsync(tag, subjectId: patientId);

        using var kickoff = await _bulkDelete.KickoffAsync(
            $"/tenant/1/Observation/$bulk-delete?_tag={tag}&_include=Observation:subject&_hardDelete=true");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        var counts = BulkDeleteClient.GetCounts(body);
        counts["Observation"].ShouldBe(1);
        counts["Patient"].ShouldBe(1);

        (await GetAsync($"/tenant/1/Observation/{observationId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync($"/tenant/1/Patient/{patientId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenRemoveReferences_WhenHardDeletingThePatient_ThenTheReferringObservationIsRewrittenNotDeleted()
    {
        var tag = NewTag();
        var patientId = await PutPatientAsync(tag);
        var observationId = await PutObservationAsync(NewTag(), subjectId: patientId);

        using var kickoff = await _bulkDelete.KickoffAsync(
            $"/tenant/1/Patient/$bulk-delete?_tag={tag}&_hardDelete=true&_remove-references=true");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(1);

        (await GetAsync($"/tenant/1/Patient/{patientId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var observationResponse = await GetAsync($"/tenant/1/Observation/{observationId}");
        observationResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await observationResponse.Content.ReadAsStringAsync());
        var observation = JsonNode.Parse(await observationResponse.Content.ReadAsStringAsync())!;
        observation["subject"]!["reference"].ShouldBeNull();
        observation["subject"]!["display"]!.GetValue<string>().ShouldBe("Referenced resource deleted");
        observation["meta"]!["versionId"]!.GetValue<string>().ShouldBe("2");
    }

    [Fact]
    public async Task GivenNoMatchingResources_WhenCompleted_ThenNoResourceDeletedCountParameterIsPresent()
    {
        var tag = NewTag();

        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = AssertContentLocation(kickoff);

        var (statusCode, body, _) = await _bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body).ShouldBeEmpty();
        (body["parameter"]?.AsArray().Count ?? 0).ShouldBe(0);
    }

    [Fact]
    public async Task GivenUnknownJobId_WhenPollingOrCancelling_ThenBothReturn404()
    {
        var unknownJobId = Guid.NewGuid().ToString();

        (await GetAsync($"/tenant/1/_operations/bulk-delete/{unknownJobId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var cancel = await fixture.Client.DeleteAsync($"/tenant/1/_operations/bulk-delete/{unknownJobId}");
        cancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenCapabilityStatement_WhenRead_ThenItDeclaresBulkDeleteSystemAndPatientOperations()
    {
        using var response = await fixture.Client.GetAsync("/metadata");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var capability = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var rest = capability["rest"]![0]!;

        rest["operation"]!.AsArray().Any(op => op!["name"]!.GetValue<string>() == "bulk-delete").ShouldBeTrue();

        var patientResource = rest["resource"]!.AsArray().Single(r => r!["type"]!.GetValue<string>() == "Patient")!;
        patientResource["operation"]!.AsArray().Any(op => op!["name"]!.GetValue<string>() == "bulk-delete").ShouldBeTrue();
    }

    // ---- Scenario 10: kickoff validation 400s ----

    [Fact]
    public async Task GivenMissingPreferHeader_WhenKickingOff_ThenReturns400()
    {
        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={NewTag()}", preferHeader: null);
        await AssertBadRequestAsync(kickoff);
    }

    [Fact]
    public async Task GivenAnInvalidSearchParameter_WhenKickingOff_ThenReturns400()
    {
        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={NewTag()}&invalidParam=x");
        await AssertBadRequestAsync(kickoff);
    }

    [Fact]
    public async Task GivenAResultShapingCountParameter_WhenKickingOff_ThenReturns400()
    {
        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={NewTag()}&_count=10");
        await AssertBadRequestAsync(kickoff);
    }

    [Fact]
    public async Task GivenRemoveReferencesWithoutHardDelete_WhenKickingOff_ThenReturns400()
    {
        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={NewTag()}&_remove-references=true");
        await AssertBadRequestAsync(kickoff);
    }

    [Fact]
    public async Task GivenConflictingHardDeleteFlagsInQueryAndBody_WhenKickingOff_ThenReturns400()
    {
        using var kickoff = await _bulkDelete.KickoffAsync(
            $"/tenant/1/Patient/$bulk-delete?_tag={NewTag()}&_hardDelete=false",
            bodyJson: """{"resourceType":"Parameters","parameter":[{"name":"hardDelete","valueBoolean":true}]}""");
        await AssertBadRequestAsync(kickoff);
    }

    [Fact]
    public async Task GivenAnUnknownExcludedResourceType_WhenKickingOff_ThenReturns400()
    {
        using var kickoff = await _bulkDelete.KickoffAsync(
            $"/tenant/1/$bulk-delete?_tag={NewTag()}&excludedResourceTypes=NotAType");
        await AssertBadRequestAsync(kickoff);
    }

    [Fact]
    public async Task GivenTypeQueryParameterAtTypeLevel_WhenKickingOff_ThenReturns400()
    {
        using var kickoff = await _bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={NewTag()}&_type=Patient");
        await AssertBadRequestAsync(kickoff);
    }

    private static async Task AssertBadRequestAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var outcome = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    private static string AssertContentLocation(HttpResponseMessage kickoff)
    {
        var location = kickoff.Content.Headers.GetValues("Content-Location").Single();
        new Uri(location).IsAbsoluteUri.ShouldBeTrue();
        location.ShouldContain("/_operations/bulk-delete/");
        return location;
    }

    private static string NewTag() => Guid.NewGuid().ToString("N");

    private async Task<string> PutPatientAsync(string tag, string family = "BulkDeleteTest")
    {
        var id = Guid.NewGuid().ToString("N");
        await PutPatientWithIdAsync(id, tag, family);
        return id;
    }

    private async Task PutPatientWithIdAsync(string id, string tag, string family)
    {
        using var content = new StringContent(
            new JsonObject
            {
                ["resourceType"] = "Patient",
                ["id"] = id,
                ["meta"] = new JsonObject { ["tag"] = new JsonArray(new JsonObject { ["code"] = tag }) },
                ["name"] = new JsonArray(new JsonObject { ["family"] = family }),
            }.ToJsonString(),
            Encoding.UTF8, "application/fhir+json");
        using var response = await fixture.Client.PutAsync($"/tenant/1/Patient/{id}", content);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private async Task<string> PutObservationAsync(string tag, string? subjectId)
    {
        var id = Guid.NewGuid().ToString("N");
        var resource = new JsonObject
        {
            ["resourceType"] = "Observation",
            ["id"] = id,
            ["meta"] = new JsonObject { ["tag"] = new JsonArray(new JsonObject { ["code"] = tag }) },
            ["status"] = "final",
            ["code"] = new JsonObject
            {
                ["coding"] = new JsonArray(new JsonObject { ["system"] = "http://loinc.org", ["code"] = "8867-4" }),
            },
        };
        if (subjectId is not null)
        {
            resource["subject"] = new JsonObject { ["reference"] = $"Patient/{subjectId}" };
        }

        using var content = new StringContent(resource.ToJsonString(), Encoding.UTF8, "application/fhir+json");
        using var response = await fixture.Client.PutAsync($"/tenant/1/Observation/{id}", content);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return id;
    }

    private Task<HttpResponseMessage> GetAsync(string url) => fixture.Client.GetAsync(url);
}
