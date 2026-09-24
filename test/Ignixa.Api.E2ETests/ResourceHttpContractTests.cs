using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.DataLayer.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class ResourceHttpContractTests(IgnixaApiFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task GivenAStaleETag_WhenPut_ThenPreconditionFailsWithoutChangingResourceOrSearch()
    {
        var id = Guid.NewGuid().ToString();
        (await PutAsync(id, "original")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PutAsync(id, "winner", "1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await PutAsync(id, "loser", "1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ReadAsync($"Patient/{id}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("2");
        (await Client.GetAsync($"/Patient/{id}/_history/3")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ((await ReadAsync($"Patient?_id={id}&name=loser"))["entry"]?.AsArray().Count ?? 0).ShouldBe(0);
        var winningSearch = await ReadAsync($"Patient?_id={id}&name=winner");
        winningSearch["entry"]!.AsArray().Count.ShouldBe(1);
        winningSearch["entry"]![0]!["resource"]!["id"]!.GetValue<string>().ShouldBe(id);
    }

    [Fact]
    public async Task GivenTwoWritersWithTheSameETag_WhenPutConcurrently_ThenExactlyOneSucceeds()
    {
        var id = Guid.NewGuid().ToString();
        (await PutAsync(id, "original")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var responses = await Task.WhenAll(PutAsync(id, "first", "1"), PutAsync(id, "second", "1"));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed).ShouldBe(1);
        (await ReadAsync($"Patient/{id}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("tenant/1/")]
    public async Task GivenTwoVersions_WhenVread_ThenReturnsTheRequestedPayloadAndETag(string prefix)
    {
        var id = Guid.NewGuid().ToString();
        await PutAsync(id, "original");
        await PutAsync(id, "updated");
        using var response = await Client.GetAsync($"/{prefix}Patient/{id}/_history/1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.ToString().ShouldBe("W/\"1\"");
        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        payload["name"]![0]!["family"]!.GetValue<string>().ShouldBe("original");
        (await Client.GetAsync($"/{prefix}Patient/{id}/_history/99")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetAsync($"/{prefix}Patient/{id}/_history/not-a-version")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetAsync($"/{prefix}Patient/{id}/_history/invalid_version")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await Client.DeleteAsync($"/Patient/{id}");
        (await Client.GetAsync($"/{prefix}Patient/{id}/_history/3")).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await Client.GetAsync($"/{prefix}Patient/{id}/_history/1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Patient.id", "valueString", "changed")]
    [InlineData("Patient.meta.versionId", "valueString", "99")]
    [InlineData("Patient.meta.lastUpdated", "valueInstant", "2000-01-01T00:00:00Z")]
    public async Task GivenProtectedMetadata_WhenPatched_ThenReturnsClientOperationOutcomeWithoutMutation(
        string path, string valueType, string value)
    {
        var id = Guid.NewGuid().ToString();
        await PutAsync(id, "original");
        var before = await ReadAsync($"Patient/{id}");
        var patch = $$"""
            {"resourceType":"Parameters","parameter":[{"name":"operation","part":[
              {"name":"type","valueCode":"replace"},{"name":"path","valueString":"{{path}}"},
              {"name":"value","{{valueType}}":"{{value}}"}]}]}
            """;
        using var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/Patient/{id}")
        {
            Content = Content(patch)
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        (await ReadAsync($"Patient/{id}")).ToJsonString().ShouldBe(before.ToJsonString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenALaterInvalidEntry_WhenTransaction_ThenNoEarlyWriteIsPersisted(bool staleVersion)
    {
        var firstId = Guid.NewGuid().ToString();
        var secondId = Guid.NewGuid().ToString();
        if (staleVersion)
        {
            await PutAsync(secondId, "original");
            await PutAsync(secondId, "updated");
        }
        var second = staleVersion
            ? Entry(secondId, "Patient", "1")
            : Entry(secondId, "NotAResource");
        using var response = await BundleAsync("transaction", Entry(firstId, "Patient"), second);
        response.IsSuccessStatusCode.ShouldBeFalse();
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        (await Client.GetAsync($"/Patient/{firstId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        if (staleVersion)
        {
            (await ReadAsync($"Patient/{secondId}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("2");
        }
    }

    [Fact]
    public async Task GivenAnUnresolvedInternalReference_WhenTransaction_ThenNoWritesArePersisted()
    {
        var id = Guid.NewGuid().ToString();
        var observation = new JsonObject
        {
            ["resource"] = JsonNode.Parse("""{"resourceType":"Observation","status":"final","code":{"text":"test"},"subject":{"reference":"urn:uuid:missing"}}"""),
            ["request"] = new JsonObject { ["method"] = "POST", ["url"] = "Observation" }
        };
        using var response = await BundleAsync("transaction", Entry(id, "Patient"), observation);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Client.GetAsync($"/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenALaterInvalidEntry_WhenBatch_ThenEarlierSuccessRemains()
    {
        var id = Guid.NewGuid().ToString();
        using var response = await BundleAsync("batch", Entry(id, "Patient"), Entry(Guid.NewGuid().ToString(), "NotAResource"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.GetAsync($"/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenAStaleWriteInABatch_WhenExecuted_ThenOnlyThatEntryFails()
    {
        var id = Guid.NewGuid().ToString();
        var fresh = Guid.NewGuid().ToString();
        await PutAsync(id, "original");
        await PutAsync(id, "winner");
        using var response = await BundleAsync("batch", Entry(fresh, "Patient"), Entry(id, "Patient", "1"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["entry"]![0]!["response"]!["status"]!.GetValue<string>().ShouldStartWith("201");
        body["entry"]![1]!["response"]!["status"]!.GetValue<string>().ShouldStartWith("412");
        (await ReadAsync($"Patient/{id}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("2");
        (await Client.GetAsync($"/Patient/{fresh}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenADeletedResource_WhenRecreated_ThenReturnsCreatedAndPreservesVersionSequence()
    {
        var id = Guid.NewGuid().ToString();
        await PutAsync(id, "original");
        (await Client.DeleteAsync($"/Patient/{id}")).IsSuccessStatusCode.ShouldBeTrue();
        using var response = await PutAsync(id, "recreated");
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.ETag!.ToString().ShouldBe("W/\"3\"");
    }

    [Fact]
    public async Task GivenAReferenceToALaterEntry_WhenTransaction_ThenBothResourcesCommitWithResolvedReference()
    {
        var urn = $"urn:uuid:{Guid.NewGuid()}";
        var observationId = Guid.NewGuid().ToString();
        var observation = new JsonObject
        {
            ["resource"] = JsonNode.Parse($$$"""{"resourceType":"Observation","id":"{{{observationId}}}","status":"final","code":{"text":"test"},"subject":{"reference":"{{{urn}}}"}}"""),
            ["request"] = new JsonObject { ["method"] = "PUT", ["url"] = $"Observation/{observationId}" }
        };
        var patient = new JsonObject
        {
            ["fullUrl"] = urn,
            ["resource"] = new JsonObject { ["resourceType"] = "Patient" },
            ["request"] = new JsonObject { ["method"] = "POST", ["url"] = "Patient" }
        };
        using var response = await BundleAsync("transaction", observation, patient);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reference = (await ReadAsync($"Observation/{observationId}"))["subject"]!["reference"]!.GetValue<string>();
        reference.ShouldStartWith("Patient/");
        (await Client.GetAsync("/" + reference)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAConditionalCreateWithUuidReferences_WhenTransaction_ThenReferencesUseTheActualResourceId(bool existing)
    {
        var patientId = Guid.NewGuid().ToString();
        if (existing)
        {
            await PutAsync(patientId, "existing");
        }
        var urn = $"urn:uuid:{Guid.NewGuid()}";
        var observationId = Guid.NewGuid().ToString();
        var observation = new JsonObject
        {
            ["resource"] = JsonNode.Parse($$$"""{"resourceType":"Observation","id":"{{{observationId}}}","status":"final","code":{"text":"test"},"subject":{"reference":"{{{urn}}}"}}"""),
            ["request"] = new JsonObject { ["method"] = "PUT", ["url"] = $"Observation/{observationId}" }
        };
        var patient = new JsonObject
        {
            ["fullUrl"] = urn,
            ["resource"] = new JsonObject { ["resourceType"] = "Patient" },
            ["request"] = new JsonObject { ["method"] = "POST", ["url"] = "Patient", ["ifNoneExist"] = $"_id={patientId}" }
        };
        using var response = await BundleAsync("transaction", observation, patient);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reference = (await ReadAsync($"Observation/{observationId}"))["subject"]!["reference"]!.GetValue<string>();
        reference.ShouldStartWith("Patient/");
        if (existing)
        {
            reference.ShouldBe($"Patient/{patientId}");
        }
        (await Client.GetAsync("/" + reference)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var search = await ReadAsync($"Observation?_id={observationId}&subject={reference}");
        search["entry"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenACoreSqlFailure_WhenTransaction_ThenEarlierResourceAndIndexesRollBack()
    {
        var sql = fixture.Services.GetRequiredService<ISqlExecutionService>();
        using var install = new SqlCommand("""
            CREATE TRIGGER dbo.RejectHttpTransactionCoreWrite ON dbo.Resource AFTER INSERT AS
            BEGIN
              IF EXISTS (SELECT 1 FROM inserted WHERE ResourceId = 'reject-http-transaction-core-write')
                THROW 51000, 'Injected HTTP transaction core write failure', 1;
            END
            """);
        await sql.ExecuteNonQueryAsync(1, install, CancellationToken.None);
        var id = Guid.NewGuid().ToString();
        try
        {
            using var response = await BundleAsync("transaction", Entry(id, "Patient"), Entry("reject-http-transaction-core-write", "Patient"));
            (await Client.GetAsync($"/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            response.IsSuccessStatusCode.ShouldBeFalse();
            JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
            ((await ReadAsync($"Patient?_id={id}"))["entry"]?.AsArray().Count ?? 0).ShouldBe(0);
        }
        finally
        {
            using var remove = new SqlCommand("DROP TRIGGER dbo.RejectHttpTransactionCoreWrite");
            await sql.ExecuteNonQueryAsync(1, remove, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenADeleteBeforeAFailedEntry_WhenTransaction_ThenTheResourceIsNotDeleted()
    {
        var id = Guid.NewGuid().ToString();
        await PutAsync(id, "survivor");
        var delete = new JsonObject { ["request"] = new JsonObject { ["method"] = "DELETE", ["url"] = $"Patient/{id}" } };
        using var response = await BundleAsync("transaction", delete, Entry(Guid.NewGuid().ToString(), "NotAResource"));
        response.IsSuccessStatusCode.ShouldBeFalse();
        (await ReadAsync($"Patient/{id}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public async Task GivenAStaleETag_WhenPatch_ThenPreconditionFailsAndSearchRetainsTheWinningValue()
    {
        var id = Guid.NewGuid().ToString();
        await PutAsync(id, "original");
        await PutAsync(id, "winner");
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/Patient/{id}")
        {
            Content = Content("""{"resourceType":"Parameters","parameter":[{"name":"operation","part":[{"name":"type","valueCode":"replace"},{"name":"path","valueString":"Patient.name[0].family"},{"name":"value","valueString":"loser"}]}]}""")
        };
        request.Headers.TryAddWithoutValidation("If-Match", "W/\"1\"");
        using var response = await Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ReadAsync($"Patient/{id}"))["name"]![0]!["family"]!.GetValue<string>().ShouldBe("winner");
    }

    private Task<HttpResponseMessage> PutAsync(string id, string family, string? version = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/Patient/{id}")
        {
            Content = Content($$"""{"resourceType":"Patient","id":"{{id}}","name":[{"family":"{{family}}"}]}""")
        };
        if (version != null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"W/\"{version}\"");
        }
        return Client.SendAsync(request);
    }

    private async Task<JsonNode> ReadAsync(string path)
    {
        using var response = await Client.GetAsync("/" + path);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private Task<HttpResponseMessage> BundleAsync(string type, params JsonObject[] entries) =>
        Client.PostAsync("/", Content(new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["type"] = type,
            ["entry"] = new JsonArray(entries.Select(e => (JsonNode)e).ToArray())
        }.ToJsonString()));

    private static JsonObject Entry(string id, string resourceType, string? version = null) => new()
    {
        ["resource"] = new JsonObject { ["resourceType"] = resourceType, ["id"] = id },
        ["request"] = new JsonObject
        {
            ["method"] = "PUT", ["url"] = $"{resourceType}/{id}",
            ["ifMatch"] = version == null ? null : $"W/\"{version}\""
        }
    };

    private static StringContent Content(string json) => new(json, Encoding.UTF8, "application/fhir+json");
}
