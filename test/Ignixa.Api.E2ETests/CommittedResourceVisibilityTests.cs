using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Shouldly;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class CommittedResourceVisibilityTests(IgnixaApiFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenNewlyCommittedCompartmentMember_WhenEverythingSinceIsRequested_ThenItIsReturned(bool transaction)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        var patientId = Guid.NewGuid().ToString();
        var observationId = Guid.NewGuid().ToString();
        using var patient = await fixture.Client.PutAsync($"/Patient/{patientId}",
            Content($$"""{"resourceType":"Patient","id":"{{patientId}}"}"""));
        patient.StatusCode.ShouldBe(HttpStatusCode.Created);
        var observation = JsonNode.Parse($$$"""{"resourceType":"Observation","id":"{{{observationId}}}","status":"final","code":{"text":"visibility"},"subject":{"reference":"Patient/{{{patientId}}}"}}""")!;
        using var write = transaction
            ? await fixture.Client.PostAsync("/", Content(new JsonObject
            {
                ["resourceType"] = "Bundle", ["type"] = "transaction",
                ["entry"] = new JsonArray(new JsonObject
                {
                    ["resource"] = observation,
                    ["request"] = new JsonObject { ["method"] = "PUT", ["url"] = $"Observation/{observationId}" }
                })
            }.ToJsonString()))
            : await fixture.Client.PutAsync($"/Observation/{observationId}", Content(observation.ToJsonString()));
        write.IsSuccessStatusCode.ShouldBeTrue(await write.Content.ReadAsStringAsync());

        using var response = await fixture.Client.GetAsync(
            $"/Patient/{patientId}/$everything?_since={Uri.EscapeDataString(cutoff.ToString("O"))}&_count=100");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var bundle = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var ids = bundle["entry"]!.AsArray().Select(e => e!["resource"]?["id"]?.GetValue<string>()).ToList();
        ids.ShouldContain(patientId);
        ids.ShouldContain(observationId);
    }

    private static StringContent Content(string json) => new(json, Encoding.UTF8, "application/fhir+json");
}
