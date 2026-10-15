using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Shouldly;

namespace Ignixa.Api.E2ETests;

/// <summary>
/// PATCH entries in batch/transaction bundles: PUT clinical resources and append references to them on a
/// List with a FHIRPath Patch Parameters resource guarded by ifMatch. JSON Patch, including a JSON Patch
/// wrapped in a Binary entry (the optional R5 bundle shape), is not supported and must be rejected explicitly.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class BundleFhirPathPatchTests(IgnixaApiFixture fixture)
{
    [Fact]
    public async Task GivenMatchingWeakEtag_WhenTransactionPutsResourcesAndAppendsThemToList_ThenListIsPatchedAtomically()
    {
        // Arrange
        var ids = new TestIds();
        var listEtag = await CreateListAsync(ids.List);
        listEtag.ShouldBe("W/\"1\"");

        // Act
        using var response = await SendBundleAsync("transaction",
            PutEntry(Patient(ids.Patient)),
            PutEntry(Encounter(ids.Encounter, ids.Patient)),
            PatchEntry($"List/{ids.List}", listEtag, AppendToList($"Patient/{ids.Patient}", $"Encounter/{ids.Encounter}")));

        // Assert
        var body = await ReadJsonAsync(response, HttpStatusCode.OK);
        body["type"]!.GetValue<string>().ShouldBe("transaction-response");
        var entries = body["entry"]!.AsArray();
        entries.Count.ShouldBe(3);
        entries[0]!["response"]!["status"]!.GetValue<string>().ShouldStartWith("201");
        entries[1]!["response"]!["status"]!.GetValue<string>().ShouldStartWith("201");

        var patchResponse = entries[2]!["response"]!;
        patchResponse["status"]!.GetValue<string>().ShouldStartWith("200");
        patchResponse["etag"]!.GetValue<string>().ShouldBe("W/\"2\"");
        patchResponse["location"]!.GetValue<string>().ShouldEndWith($"/List/{ids.List}/_history/2");

        var list = await ReadAsync($"List/{ids.List}");
        list["meta"]!["versionId"]!.GetValue<string>().ShouldBe("2");
        ListReferences(list).ShouldBe([$"Patient/{ids.Patient}", $"Encounter/{ids.Encounter}"]);
        (await ReadAsync($"Encounter/{ids.Encounter}"))["subject"]!["reference"]!.GetValue<string>()
            .ShouldBe($"Patient/{ids.Patient}");
    }

    [Fact]
    public async Task GivenStaleWeakEtag_WhenTransactionPatchesList_ThenReturnsPreconditionFailedAndCommitsNothing()
    {
        // Arrange
        var ids = new TestIds();
        var staleEtag = await CreateListAsync(ids.List);
        await UpdateListAsync(ids.List);

        // Act
        using var response = await SendBundleAsync("transaction",
            PutEntry(Patient(ids.Patient)),
            PatchEntry($"List/{ids.List}", staleEtag, AppendToList($"Patient/{ids.Patient}")));

        // Assert
        var outcome = await ReadJsonAsync(response, HttpStatusCode.PreconditionFailed);
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        Diagnostics(outcome).ShouldContain(d => d.Contains("If-Match", StringComparison.Ordinal));

        (await fixture.Client.GetAsync($"/Patient/{ids.Patient}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var list = await ReadAsync($"List/{ids.List}");
        list["meta"]!["versionId"]!.GetValue<string>().ShouldBe("2");
        ListReferences(list).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenBinaryJsonPatchEntry_WhenTransactionSubmitted_ThenRejectedAsRequiringFhirPathPatch()
    {
        // Arrange: JSON Patch wrapped in a Binary entry
        var ids = new TestIds();
        var listEtag = await CreateListAsync(ids.List);
        var jsonPatch = new JsonArray(new JsonObject
        {
            ["op"] = "add",
            ["path"] = "/entry/-",
            ["value"] = new JsonObject { ["item"] = new JsonObject { ["reference"] = $"Patient/{ids.Patient}" } },
        }).ToJsonString();
        var binary = new JsonObject
        {
            ["resourceType"] = "Binary",
            ["contentType"] = "application/json-patch+json",
            ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPatch)),
        };

        // Act
        using var response = await SendBundleAsync("transaction",
            PutEntry(Patient(ids.Patient)),
            PatchEntry($"List/{ids.List}", listEtag, binary));

        // Assert
        var outcome = await ReadJsonAsync(response, HttpStatusCode.BadRequest);
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        var diagnostics = Diagnostics(outcome);
        diagnostics.ShouldContain(d => d.Contains("Binary patch payload (contentType 'application/json-patch+json') is not supported", StringComparison.Ordinal));
        diagnostics.ShouldContain(d => d.Contains("Use FHIRPath Patch", StringComparison.Ordinal));
        diagnostics.ShouldNotContain(d => d.Contains("Expected Parameters", StringComparison.Ordinal));

        (await fixture.Client.GetAsync($"/Patient/{ids.Patient}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync($"List/{ids.List}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public async Task GivenBatchWithIndependentPatchEntries_WhenSubmitted_ThenEachEntryReportsItsOwnOutcome()
    {
        // Arrange
        var ids = new TestIds();
        var staleIds = new TestIds();
        var binaryIds = new TestIds();
        var listEtag = await CreateListAsync(ids.List);
        var staleEtag = await CreateListAsync(staleIds.List);
        await UpdateListAsync(staleIds.List);
        var binaryListEtag = await CreateListAsync(binaryIds.List);
        var binary = new JsonObject { ["resourceType"] = "Binary", ["contentType"] = "application/json-patch+json", ["data"] = "W10=" };

        // Act
        using var response = await SendBundleAsync("batch",
            PatchEntry($"List/{ids.List}", listEtag, AppendToList("Patient/batch-a")),
            PatchEntry($"List/{staleIds.List}", staleEtag, AppendToList("Patient/batch-b")),
            PatchEntry($"List/{binaryIds.List}", binaryListEtag, binary));

        // Assert
        var body = await ReadJsonAsync(response, HttpStatusCode.OK);
        body["type"]!.GetValue<string>().ShouldBe("batch-response");
        var entries = body["entry"]!.AsArray();

        var patched = entries[0]!["response"]!;
        patched["status"]!.GetValue<string>().ShouldStartWith("200");
        patched["etag"]!.GetValue<string>().ShouldBe("W/\"2\"");
        patched["location"]!.GetValue<string>().ShouldEndWith($"/List/{ids.List}/_history/2");

        entries[1]!["response"]!["status"]!.GetValue<string>().ShouldStartWith("412");
        var rejected = entries[2]!["response"]!;
        rejected["status"]!.GetValue<string>().ShouldStartWith("400");
        Diagnostics(rejected["outcome"] ?? entries[2]!["resource"]!)
            .ShouldContain(d => d.Contains("Use FHIRPath Patch", StringComparison.Ordinal));

        ListReferences(await ReadAsync($"List/{ids.List}")).ShouldBe(["Patient/batch-a"]);
        ListReferences(await ReadAsync($"List/{staleIds.List}")).ShouldBeEmpty();
        ListReferences(await ReadAsync($"List/{binaryIds.List}")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("application/json-patch+json", """{"op":"add"}""")]
    [InlineData("application/json-patch+json; charset=utf-8", """[{"op":"add","path":"/entry/-","value":{}}]""")]
    [InlineData("application/fhir+json", """ [{"op":"add","path":"/entry/-","value":{}}]""")]
    [InlineData("application/fhir+json", """{"resourceType":"Binary","contentType":"application/xml-patch+xml","data":"PGRpZmYvPg=="}""")]
    [InlineData("application/fhir+json", "")]
    [InlineData("application/fhir+json", """{"resourceType":"Parameters","parameter":[""")]
    [InlineData("application/fhir+json", """<Parameters xmlns="http://hl7.org/fhir"/>""")]
    [InlineData("application/fhir+json", "42")]
    [InlineData("application/fhir+json", "null")]
    public async Task GivenNonFhirPathPatchBody_WhenPatchingDirectly_ThenRejectedAndResourceUnchanged(string contentType, string body)
    {
        // Arrange
        var ids = new TestIds();
        await CreateListAsync(ids.List);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/List/{ids.List}")
        {
            Content = new StringContent(body, Encoding.UTF8)
        };
        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        var outcome = await ReadJsonAsync(response, HttpStatusCode.BadRequest);
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        Diagnostics(outcome).ShouldContain(d => d.Contains("Use FHIRPath Patch", StringComparison.Ordinal));
        (await ReadAsync($"List/{ids.List}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public async Task GivenBinaryBody_WhenConditionallyPatching_ThenRejectedAsRequiringFhirPathPatch()
    {
        // Arrange
        var ids = new TestIds();
        await CreateListAsync(ids.List);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/List?_id={ids.List}")
        {
            Content = FhirContent(new JsonObject { ["resourceType"] = "Binary", ["contentType"] = "application/json-patch+json", ["data"] = "W10=" })
        };

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        var outcome = await ReadJsonAsync(response, HttpStatusCode.BadRequest);
        Diagnostics(outcome).ShouldContain(d => d.Contains("Use FHIRPath Patch", StringComparison.Ordinal));
        (await ReadAsync($"List/{ids.List}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public async Task GivenFhirPathPatch_WhenPatchingDirectly_ThenResponseCarriesVersionedLocationAndEtag()
    {
        // Arrange
        var ids = new TestIds();
        await CreateListAsync(ids.List);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/List/{ids.List}")
        {
            Content = FhirContent(AppendToList("Patient/direct"))
        };

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        await ReadJsonAsync(response, HttpStatusCode.OK);
        response.Headers.ETag!.ToString().ShouldBe("W/\"2\"");
        var location = response.Headers.Location!;
        location.IsAbsoluteUri.ShouldBeTrue();
        location.Host.ShouldBe(fixture.Client.BaseAddress!.Host);
        location.AbsolutePath.ShouldBe($"/List/{ids.List}/_history/2");
        var version = await ReadAsync(location.AbsolutePath.TrimStart('/'));
        ListReferences(version).ShouldBe(["Patient/direct"]);
    }

    private async Task<string> CreateListAsync(string id)
    {
        using var response = await fixture.Client.PutAsync($"/List/{id}", FhirContent(List(id)));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.ETag!.ToString();
    }

    private async Task UpdateListAsync(string id)
    {
        var list = List(id);
        list["title"] = "updated elsewhere";
        using var response = await fixture.Client.PutAsync($"/List/{id}", FhirContent(list));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonNode> ReadAsync(string url)
    {
        using var response = await fixture.Client.GetAsync("/" + url);
        return await ReadJsonAsync(response, HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> SendBundleAsync(string type, params JsonObject[] entries) =>
        fixture.Client.PostAsync("/", FhirContent(new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["type"] = type,
            ["entry"] = new JsonArray(entries.Select(entry => (JsonNode)entry).ToArray()),
        }));

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, content);
        return JsonNode.Parse(content)!;
    }

    private static JsonObject PutEntry(JsonObject resource) => new()
    {
        ["fullUrl"] = $"{resource["resourceType"]}/{resource["id"]}",
        ["resource"] = resource,
        ["request"] = new JsonObject { ["method"] = "PUT", ["url"] = $"{resource["resourceType"]}/{resource["id"]}" },
    };

    private static JsonObject PatchEntry(string url, string ifMatch, JsonObject resource) => new()
    {
        ["resource"] = resource,
        ["request"] = new JsonObject { ["method"] = "PATCH", ["url"] = url, ["ifMatch"] = ifMatch },
    };

    /// <summary>
    /// FHIRPath Patch appending one List.entry per reference; List.entry is a BackboneElement, so each
    /// value is given as nested parts (http://hl7.org/fhir/fhirpatch.html#anonymous).
    /// </summary>
    private static JsonObject AppendToList(params string[] references) => new()
    {
        ["resourceType"] = "Parameters",
        ["parameter"] = new JsonArray(references.Select(reference => (JsonNode)JsonNode.Parse($$$"""
            {
              "name": "operation",
              "part": [
                { "name": "type", "valueCode": "add" },
                { "name": "path", "valueString": "List" },
                { "name": "name", "valueString": "entry" },
                { "name": "value", "part": [ { "name": "item", "valueReference": { "reference": "{{{reference}}}" } } ] }
              ]
            }
            """)!).ToArray()),
    };

    private static JsonObject List(string id) => new()
    {
        ["resourceType"] = "List",
        ["id"] = id,
        ["status"] = "current",
        ["mode"] = "working",
    };

    private static JsonObject Patient(string id) => new()
    {
        ["resourceType"] = "Patient",
        ["id"] = id,
        ["name"] = new JsonArray(new JsonObject { ["family"] = "Bundle-Patch" }),
    };

    private static JsonObject Encounter(string id, string patientId) => new()
    {
        ["resourceType"] = "Encounter",
        ["id"] = id,
        ["status"] = "finished",
        ["class"] = new JsonObject { ["system"] = "http://terminology.hl7.org/CodeSystem/v3-ActCode", ["code"] = "AMB" },
        ["subject"] = new JsonObject { ["reference"] = $"Patient/{patientId}" },
    };

    private static IEnumerable<string> ListReferences(JsonNode list) =>
        list["entry"]?.AsArray().Select(entry => entry!["item"]!["reference"]!.GetValue<string>()) ?? [];

    private static List<string> Diagnostics(JsonNode outcome) =>
        outcome["issue"]!.AsArray().Select(issue => issue!["diagnostics"]?.GetValue<string>() ?? string.Empty).ToList();

    private static StringContent FhirContent(JsonNode resource) =>
        new(resource.ToJsonString(), Encoding.UTF8, "application/fhir+json");

    private sealed class TestIds
    {
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

        public string List => $"session-{_suffix}";

        public string Patient => $"pat-{_suffix}";

        public string Encounter => $"enc-{_suffix}";
    }
}
