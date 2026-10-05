using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class BulkImportCompositionRootTests(IgnixaApiFixture fixture)
{
    [Fact]
    public async Task GivenProviderPathNdjson_WhenImportingThroughApplicationRoot_ThenResourcesArePersistedAndSearchable()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "bulk-import-composition", Guid.NewGuid().ToString("N"));
        try
        {
            await using var application = fixture.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["BackgroundJobs:Repository"] = "SqlServer",
                        ["DurableTask:Provider"] = "SqlServer",
                        ["DurableTask:SqlServer:TaskHubName"] = "bulkimportroot",
                        ["FhirRepository:BaseDirectory"] = Path.Combine(directory, "orchestrations"),
                        ["BlobStorage:Provider"] = "Local",
                        ["BlobStorage:RootDirectory"] = Path.Combine(directory, "blobs"),
                        ["TtlCleanup:Enabled"] = "false",
                        ["Import:MaxConcurrentFiles"] = "1"
                    })));
            using var client = application.CreateClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var cancellationToken = timeout.Token;
            var patientId = $"bulk-root-{Guid.NewGuid():N}";
            var rejectedId = $"bulk-root-rejected-{Guid.NewGuid():N}";
            var inputPath = $"import/{patientId}/Patient.ndjson";
            var ndjson = $$$"""
                {"resourceType":"Patient","id":"{{{patientId}}}","identifier":[{"system":"urn:bulk-root","value":"{{{patientId}}}"}],"name":[{"family":"Imported"}]}
                {"resourceType":"Observation","id":"{{{rejectedId}}}","status":"final","code":{"text":"Wrong input type"}}

                """;
            var blobs = application.Services.GetRequiredService<IBlobStorageClient>();
            await using var input = new MemoryStream(Encoding.UTF8.GetBytes(ndjson));
            await blobs.WriteBlobAsync(inputPath, input, cancellationToken);

            using var request = new StringContent($$"""
                {
                  "resourceType":"Parameters",
                  "parameter":[
                    {"name":"inputFormat","valueCode":"application/fhir+ndjson"},
                    {"name":"mode","valueCode":"IncrementalLoad"},
                    {"name":"input","part":[
                      {"name":"type","valueCode":"Patient"},
                      {"name":"url","valueUri":"{{inputPath}}"}
                    ]}
                  ]
                }
                """, Encoding.UTF8, "application/fhir+json");
            using var started = await client.PostAsync("/tenant/1/$import", request, cancellationToken);
            started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync(cancellationToken));
            var statusUrl = started.Content.Headers.GetValues("Content-Location").Single();
            var jobId = JsonNode.Parse(await started.Content.ReadAsStringAsync(cancellationToken))!["jobId"]!.GetValue<string>();

            var manifest = await PollUntilCompletedAsync(client, statusUrl, cancellationToken);
            manifest["output"]!.AsArray().Count.ShouldBe(1);
            manifest["output"]![0]!["count"]!.GetValue<long>().ShouldBe(1);
            manifest["error"]!.AsArray().Count.ShouldBe(1);
            manifest["error"]![0]!["count"]!.GetValue<long>().ShouldBe(1);

            var jobs = application.Services.GetRequiredService<IBackgroundJobRepository<ImportJobDefinition>>();
            var stored = await jobs.GetAsync(jobId, 1, cancellationToken);
            stored.ShouldNotBeNull();
            stored.Status.ShouldBe("Completed");
            stored.Definition.TenantId.ShouldBe(1);
            stored.Result.ShouldNotBeNull();
            var errorUrl = new Uri(manifest["error"]![0]!["url"]!.GetValue<string>());
            errorUrl.IsFile.ShouldBeTrue();
            var errorLog = await File.ReadAllTextAsync(errorUrl.LocalPath, cancellationToken);
            JsonNode.Parse(errorLog.Trim())!["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");

            using var read = await client.GetAsync($"/tenant/1/Patient/{patientId}", cancellationToken);
            read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
            var patient = JsonNode.Parse(await read.Content.ReadAsStringAsync(cancellationToken))!;
            patient["id"]!.GetValue<string>().ShouldBe(patientId);
            patient["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Imported");

            using var search = await client.GetAsync(
                $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"urn:bulk-root|{patientId}")}", cancellationToken);
            search.StatusCode.ShouldBe(HttpStatusCode.OK, await search.Content.ReadAsStringAsync(cancellationToken));
            var entries = JsonNode.Parse(await search.Content.ReadAsStringAsync(cancellationToken))!["entry"]!.AsArray();
            entries.Count.ShouldBe(1);
            entries[0]!["resource"]!["id"]!.GetValue<string>().ShouldBe(patientId);

            using var rejected = await client.GetAsync($"/tenant/1/Observation/{rejectedId}", cancellationToken);
            rejected.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var repeatedManifest = await PollUntilCompletedAsync(client, statusUrl, cancellationToken);
            JsonNode.DeepEquals(manifest, repeatedManifest).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<JsonNode> PollUntilCompletedAsync(
        HttpClient client, string statusUrl, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var response = await client.GetAsync(statusUrl, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
                return JsonNode.Parse(body)!;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}
