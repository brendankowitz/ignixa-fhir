using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Shouldly;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class ViewDefinitionHotLoadTests(IgnixaApiFixture fixture)
{
    private const string Canonical = "https://sql-on-fhir.org/ig/StructureDefinition/ViewDefinition";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenWarmApplication_WhenLoadingEmbeddedModel_ThenRoutesValidatesAndUpdatesCapabilitiesWithoutRestart(
        bool metadataFirst)
    {
        var client = fixture.Client;
        string invalidId = "hotload-invalid-" + Guid.NewGuid().ToString("N");
        var before = await ReadMetadataAsync(client);
        FindModel(before).ShouldBeNull();
        using var absent = await client.GetAsync($"/tenant/1/ViewDefinition/{invalidId}");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var patientBody = Fhir("""{"resourceType":"Patient","active":true}""");
        using var patientCreated = await client.PostAsync("/tenant/1/Patient", patientBody);
        patientCreated.StatusCode.ShouldBe(HttpStatusCode.Created, await patientCreated.Content.ReadAsStringAsync());
        string patientId = JsonNode.Parse(await patientCreated.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();
        using var patientDeleted = await client.DeleteAsync($"/tenant/1/Patient/{patientId}");
        patientDeleted.IsSuccessStatusCode.ShouldBeTrue(await patientDeleted.Content.ReadAsStringAsync());

        using var package = new StringContent("""
            {"packageId":"local.ignixa.sqlonfhir","version":"2.1.0","includeDependencies":false}
            """, Encoding.UTF8, "application/json");
        using var loaded = await client.PostAsync("/tenant/1/admin/packages/load", package);
        loaded.StatusCode.ShouldBe(HttpStatusCode.OK, await loaded.Content.ReadAsStringAsync());

        if (metadataFirst)
        {
            await AssertModelAdvertisedAsync(client);
        }

        using var valid = Fhir("""
            {"resourceType":"ViewDefinition","status":"active","resource":"Patient",
             "meta":{"tag":[{"system":"https://example.org/hotload","code":"model"}]},
             "select":[{"column":[{"name":"id","path":"id"}]}]}
            """);
        using var created = await client.PostAsync("/tenant/1/ViewDefinition", valid);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var resource = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        string id = resource["id"]!.GetValue<string>();
        await AssertModelAdvertisedAsync(client);
        using var invalid = Fhir("""
            {"resourceType":"ViewDefinition","status":"active","resource":"Patient",
             "select":[{"column":[{"name":"id"}]}]}
            """);
        using var rejected = await client.PutAsync($"/tenant/1/ViewDefinition/{invalidId}", invalid);
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await rejected.Content.ReadAsStringAsync());
        var outcome = JsonNode.Parse(await rejected.Content.ReadAsStringAsync())!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        rejected.Content.Headers.ContentType!.MediaType.ShouldBe("application/fhir+json");
        using var read = await client.GetAsync($"/tenant/1/ViewDefinition/{id}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        using var missing = await client.GetAsync($"/tenant/1/ViewDefinition/{invalidId}");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var deleted = await client.DeleteAsync($"/tenant/1/ViewDefinition/{id}");
        deleted.IsSuccessStatusCode.ShouldBeTrue(await deleted.Content.ReadAsStringAsync());
        using var unloaded = await client.DeleteAsync("/tenant/1/admin/packages/local.ignixa.sqlonfhir/2.1.0");
        unloaded.StatusCode.ShouldBe(HttpStatusCode.OK, await unloaded.Content.ReadAsStringAsync());
        FindModel(await ReadMetadataAsync(client)).ShouldBeNull();
        using var afterUnload = await client.GetAsync($"/tenant/1/ViewDefinition/{id}");
        afterUnload.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonNode.Parse(await afterUnload.Content.ReadAsStringAsync())!["resourceType"]!.GetValue<string>()
            .ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenInstalledPackage_WhenNewApplicationStarts_ThenMetadataAdvertisesModelBeforeAnyModelRoute()
    {
        using var package = new StringContent("""
            {"packageId":"local.ignixa.sqlonfhir","version":"2.1.0","includeDependencies":false}
            """, Encoding.UTF8, "application/json");
        using var loaded = await fixture.Client.PostAsync("/tenant/1/admin/packages/load", package);
        loaded.StatusCode.ShouldBe(HttpStatusCode.OK, await loaded.Content.ReadAsStringAsync());
        try
        {
            await using var coldApplication = fixture.WithWebHostBuilder(_ => { });
            using var coldClient = coldApplication.CreateClient();

            await AssertModelAdvertisedAsync(coldClient);
        }
        finally
        {
            using var unloaded = await fixture.Client.DeleteAsync("/tenant/1/admin/packages/local.ignixa.sqlonfhir/2.1.0");
            unloaded.StatusCode.ShouldBe(HttpStatusCode.OK, await unloaded.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task GivenUnsupportedType_WhenRouting_ThenReturnsFhirOutcomeInsteadOfEmptyJson()
    {
        using var response = await fixture.Client.GetAsync("/tenant/1/NeverInstalledModel/missing");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/fhir+json");
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!.GetValue<string>()
            .ShouldBe("OperationOutcome");
    }

    private static StringContent Fhir(string json) => new(json, Encoding.UTF8, "application/fhir+json");

    private static async Task AssertModelAdvertisedAsync(HttpClient client)
    {
        var advertised = FindModel(await ReadMetadataAsync(client));
        advertised.ShouldNotBeNull();
        advertised["profile"]!.GetValue<string>().ShouldBe(Canonical);
    }

    private static async Task<JsonNode> ReadMetadataAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/tenant/1/metadata");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static JsonNode? FindModel(JsonNode statement)
        => statement["rest"]![0]!["resource"]!.AsArray()
            .SingleOrDefault(resource => resource!["type"]!.GetValue<string>() == "ViewDefinition");
}
