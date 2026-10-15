// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Shouldly;

namespace Ignixa.Api.E2ETests.Formatting;

[Collection(ElementOrderingTestCollection.Name)]
public class ElementOrderingOnWriteTests(ElementOrderingApiFixture fixture)
{
    private static readonly string[] SpecOrder = ["resourceType", "id", "meta", "active", "gender", "birthDate"];

    [Fact]
    public async Task GivenOrderingEnabled_WhenPuttingUnorderedResource_ThenResponseAndReadAreSpecOrdered()
    {
        string id = Guid.NewGuid().ToString("N");
        using var content = Json(ScrambledPatient(id));

        using var created = await fixture.Client.PutAsync($"/tenant/1/Patient/{id}", content);
        using var read = await fixture.Client.GetAsync($"/tenant/1/Patient/{id}");

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PropertyNamesAsync(created)).ShouldBe(SpecOrder);
        (await PropertyNamesAsync(read)).ShouldBe(SpecOrder);
    }

    [Fact]
    public async Task GivenOrderingEnabled_WhenTransactionWritesUnorderedResource_ThenReadIsSpecOrdered()
    {
        string id = Guid.NewGuid().ToString("N");
        using var content = Json($$$"""
            {"resourceType":"Bundle","type":"transaction","entry":[
              {"resource":{{{ScrambledPatient(id)}}},"request":{"method":"PUT","url":"Patient/{{{id}}}"}}]}
            """);

        using var transaction = await fixture.Client.PostAsync("/", content);
        using var read = await fixture.Client.GetAsync($"/Patient/{id}");

        transaction.StatusCode.ShouldBe(HttpStatusCode.OK, await transaction.Content.ReadAsStringAsync());
        (await PropertyNamesAsync(read)).ShouldBe(SpecOrder);
    }

    [Fact]
    public async Task GivenOrderingEnabled_WhenPatchAddsElement_ThenResponseAndReadAreSpecOrdered()
    {
        string id = Guid.NewGuid().ToString("N");
        using var seed = Json($$"""{"resourceType":"Patient","id":"{{id}}","gender":"male"}""");
        using var created = await fixture.Client.PutAsync($"/tenant/1/Patient/{id}", seed);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var patch = Json("""
            {"resourceType":"Parameters","parameter":[{"name":"operation","part":[
              {"name":"type","valueCode":"add"},{"name":"path","valueString":"Patient.name"},
              {"name":"value","valueHumanName":{"given":["Jo"],"family":"Smith"}}]}]}
            """);

        using var patched = await fixture.Client.PatchAsync($"/tenant/1/Patient/{id}", patch);
        using var read = await fixture.Client.GetAsync($"/tenant/1/Patient/{id}");

        patched.StatusCode.ShouldBe(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync());
        string[] expected = ["resourceType", "id", "meta", "name", "gender"];
        (await PropertyNamesAsync(patched)).ShouldBe(expected);
        (await PropertyNamesAsync(read)).ShouldBe(expected);
    }

    [Fact]
    public async Task GivenOrderingEnabled_WhenWritingWithXProvenance_ThenStoredProvenanceIsSpecOrdered()
    {
        string id = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/tenant/1/Patient/{id}") { Content = Json(ScrambledPatient(id)) };
        request.Headers.Add("X-Provenance", """{"agent":[{"who":{"display":"importer"}}],"recorded":"2024-01-01T00:00:00Z","resourceType":"Provenance"}""");

        using var created = await fixture.Client.SendAsync(request);
        using var search = await fixture.Client.GetAsync($"/tenant/1/Provenance?target=Patient/{id}");

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        search.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonObject provenance = JsonNode.Parse(await search.Content.ReadAsStringAsync())!["entry"]![0]!["resource"]!.AsObject();
        provenance.Select(property => property.Key).ShouldBe(["resourceType", "id", "meta", "target", "recorded", "agent"]);
    }

    private static string ScrambledPatient(string id) =>
        $$"""{"birthDate":"1970-01-01","gender":"male","active":true,"id":"{{id}}","resourceType":"Patient"}""";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/fhir+json");

    private static async Task<string[]> PropertyNamesAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        JsonObject resource = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        return resource.Select(property => property.Key).ToArray();
    }
}
