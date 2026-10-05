using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.Domain.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class TransactionProviderCapabilityTests(IgnixaApiFixture fixture)
{
    [Fact]
    public async Task GivenSqlProvider_WhenReadingMetadata_ThenVersionReadAndAtomicTransactionsAreDeclared()
    {
        var metadata = await ReadAsync(fixture.Client, "/metadata");
        SystemInteractions(metadata).ShouldContain("transaction");
        SystemInteractions(metadata).ShouldContain("batch");
        PatientInteractions(metadata).ShouldContain("vread");
    }

    [Fact]
    public async Task GivenFileSystemProvider_WhenReadingMetadata_ThenUnsupportedInteractionsAreNotDeclared()
    {
        await WithFileSystemAsync(async client =>
        {
            var metadata = await ReadAsync(client, "/metadata");
            SystemInteractions(metadata).ShouldNotContain("transaction");
            SystemInteractions(metadata).ShouldContain("batch");
            PatientInteractions(metadata).ShouldNotContain("vread");
        });
    }

    [Fact]
    public async Task GivenFileSystemProvider_WhenReadingATransactionWithGetAndHead_ThenReadsSucceedWithoutAtomicWrites()
    {
        await WithFileSystemAsync(async client =>
        {
            await SeedAsync(client);
            using var response = await SendBundleAsync(client, "transaction",
                Entry("GET", "Patient/provider-patient"), Entry("HEAD", "Patient/provider-patient"));
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            body["type"]!.GetValue<string>().ShouldBe("transaction-response");
            body["entry"]![0]!["response"]!["status"]!.GetValue<string>().ShouldStartWith("200");
            body["entry"]![1]!["response"]!["status"]!.GetValue<string>().ShouldStartWith("200");
            body["entry"]![1]!["resource"].ShouldBeNull();
            body["entry"]![1]!["response"]!["etag"]!.GetValue<string>().ShouldBe("W/\"1\"");
        });
    }

    [Fact]
    public async Task GivenFileSystemProvider_WhenALaterTransactionReadFails_ThenReturnsOneFailureOutcome()
    {
        await WithFileSystemAsync(async client =>
        {
            await SeedAsync(client);
            using var response = await SendBundleAsync(client, "transaction",
                Entry("GET", "Patient/provider-patient"), Entry("GET", "Patient/missing"));
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
            body["entry"].ShouldBeNull();
        });
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task GivenFileSystemProvider_WhenTransactionContainsAMutation_ThenRejectsBeforeAnyResourceChanges(string method)
    {
        await WithFileSystemAsync(async client =>
        {
            await SeedAsync(client);
            var resource = method switch
            {
                "DELETE" => null,
                "PATCH" => JsonNode.Parse("""{"resourceType":"Parameters","parameter":[{"name":"operation","part":[{"name":"type","valueCode":"replace"},{"name":"path","valueString":"Patient.name[0].family"},{"name":"value","valueString":"changed"}]}]}"""),
                _ => JsonNode.Parse("""{"resourceType":"Patient","id":"provider-patient","name":[{"family":"changed"}]}""")
            };
            var mutation = Entry(method, method == "POST" ? "Patient" : "Patient/provider-patient");
            mutation["resource"] = resource;
            using var response = await SendBundleAsync(client, "transaction",
                Entry("GET", "Patient/provider-patient"), mutation);
            response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
            JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!
                .GetValue<string>().ShouldBe("OperationOutcome");
            var current = await ReadAsync(client, "/Patient/provider-patient");
            current["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
            current["name"]![0]!["family"]!.GetValue<string>().ShouldBe("original");
            var history = await ReadAsync(client, "/Patient/_history");
            history["entry"]!.AsArray().Count.ShouldBe(1);
        });
    }

    [Fact]
    public async Task GivenFileSystemProvider_WhenBatchContainsAMutation_ThenBatchRemainsSupported()
    {
        await WithFileSystemAsync(async client =>
        {
            var write = Entry("PUT", "Patient/batch-patient");
            write["resource"] = JsonNode.Parse("""{"resourceType":"Patient","id":"batch-patient"}""");
            using var response = await SendBundleAsync(client, "batch", write);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await client.GetAsync("/Patient/batch-patient")).StatusCode.ShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task GivenFileSystemProvider_WhenVersionReadIsUnsupported_ThenItNeverReturnsLatestAsAnOlderVersion()
    {
        await WithFileSystemAsync(async client =>
        {
            await SeedAsync(client);
            using var update = await client.PutAsync("/Patient/provider-patient",
                Content("""{"resourceType":"Patient","id":"provider-patient","name":[{"family":"latest"}]}"""));
            update.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var response = await client.GetAsync("/Patient/provider-patient/_history/1");
            response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        });
    }

    private async Task WithFileSystemAsync(Func<HttpClient, Task> test)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "provider-contracts", Guid.NewGuid().ToString("N"));
        try
        {
            using var repository = new FileBasedFhirRepository(directory, NullLogger<FileBasedFhirRepository>.Instance);
            var repositoryFactory = new FixedRepositoryFactory(repository);
            // Keep the conformance catalog on SQL while exercising real filesystem resource storage.
            // This isolates the provider capability contract from unrelated package-store routing.
            var conformanceConnection = fixture.Services.GetRequiredService<IConfiguration>()
                ["Tenants:Configurations:1:Storage:ConnectionString"]!;
            await using var application = new FileSystemResourceFixture(repositoryFactory, conformanceConnection);
            using var client = application.CreateClient();
            (await application.Services.GetRequiredService<IFhirRepositoryFactory>()
                .GetRepositoryAsync(1, CancellationToken.None)).ShouldBeSameAs(repository);
            await test(client);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task SeedAsync(HttpClient client)
    {
        using var response = await client.PutAsync("/Patient/provider-patient",
            Content("""{"resourceType":"Patient","id":"provider-patient","name":[{"family":"original"}]}"""));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonNode> ReadAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static IEnumerable<string> SystemInteractions(JsonNode metadata) =>
        metadata["rest"]![0]!["interaction"]!.AsArray().Select(i => i!["code"]!.GetValue<string>());

    private static IEnumerable<string> PatientInteractions(JsonNode metadata) =>
        metadata["rest"]![0]!["resource"]!.AsArray()
            .Single(r => r!["type"]!.GetValue<string>() == "Patient")!["interaction"]!.AsArray()
            .Select(i => i!["code"]!.GetValue<string>());

    private static JsonObject Entry(string method, string url) =>
        new() { ["request"] = new JsonObject { ["method"] = method, ["url"] = url } };

    private static Task<HttpResponseMessage> SendBundleAsync(HttpClient client, string type, params JsonObject[] entries) =>
        client.PostAsync("/", Content(new JsonObject
        {
            ["resourceType"] = "Bundle", ["type"] = type,
            ["entry"] = new JsonArray(entries.Select(e => (JsonNode)e).ToArray())
        }.ToJsonString()));

    private static StringContent Content(string json) => new(json, Encoding.UTF8, "application/fhir+json");

    private sealed class FixedRepositoryFactory(IFhirRepository repository) : IFhirRepositoryFactory
    {
        public Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(repository);
        }
    }

    private sealed class FileSystemResourceFixture(
        IFhirRepositoryFactory repositoryFactory,
        string conformanceConnection) : IgnixaApiFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((context, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenants:Configurations:1:Storage:ConnectionString"] = conformanceConnection
                }));
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseServiceProviderFactory(new AutofacServiceProviderFactory(container =>
                container.RegisterDecorator<IFhirRepositoryFactory>((context, parameters, inner) => repositoryFactory)));
            return base.CreateHost(builder);
        }
    }
}
