using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Application.Events.Package;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Medino;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class TransactionContainmentTests(IgnixaApiFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task GivenDefaultScenarioResourcesWithoutFullUrls_WhenSubmittedAsHarnessPuts_ThenReportsUndeclaredUuidReferences()
    {
        var scenario = new ScenarioBuilder(fixture.SchemaProvider)
            .WithTag(Guid.NewGuid().ToString())
            .WithPatient(patient => patient.FromSeattle())
            .AddEncounter("Test visit")
            .AddObservation(VitalSigns.HeartRate, 72m, "beats/minute", "/min")
            .Build();
        var entries = scenario.AllResources.Select(resource =>
        {
            var entry = Entry("PUT", $"{resource.ResourceType}/{resource.Id}");
            entry["resource"] = resource.MutableNode.DeepClone();
            return entry;
        }).ToArray();

        using var response = await SendAsync(fixture.Client, entries);
        var content = await response.Content.ReadAsStringAsync();
        output.WriteLine(content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        var outcome = JsonNode.Parse(content)!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        outcome["issue"]![0]!["diagnostics"]!.GetValue<string>()
            .ShouldContain("Unresolved transaction reference 'urn:uuid:");
    }

    [Theory]
    [InlineData("admin/packages/{package}/1.0.0")]
    [InlineData("Patient/../admin/packages/{package}/1.0.0")]
    [InlineData("Patient/%2e%2e/admin/packages/{package}/1.0.0")]
    [InlineData("admin\\packages\\{package}\\1.0.0")]
    [InlineData("%61dmin/packages/{package}/1.0.0")]
    [InlineData("admin%2fpackages%2f{package}%2f1.0.0")]
    public async Task GivenAnInstalledPackage_WhenAnAdminRouteIsInsideATransaction_ThenPreflightPreventsDeactivation(
        string route)
    {
        var observer = new PackageEventObserver();
        var connection = fixture.Services.GetRequiredService<IConfiguration>()
            ["Tenants:Configurations:1:Storage:ConnectionString"]!;
        await using var application = new ObservedPackageFixture(observer, connection);
        using var client = application.CreateClient();
        var repository = application.Services.GetRequiredService<IPackageResourceRepository>();
        var packageId = $"transaction-boundary-{Guid.NewGuid():N}";
        var canonical = $"http://example.org/ValueSet/{packageId}";
        var package = new PackageResource
        {
            PackageId = packageId, PackageVersion = "1.0.0", ResourceType = "ValueSet",
            ResourceId = packageId, Canonical = canonical, Version = "1.0.0", FhirVersion = "4.0.1",
            ResourceJson = $$"""{"resourceType":"ValueSet","id":"{{packageId}}","url":"{{canonical}}","status":"active"}""",
            IsActive = true
        };
        await repository.UpsertAsync(package, CancellationToken.None);
        try
        {
            // Prove that this installed package and its administrative route really have side effects.
            (await repository.ListLoadedPackagesAsync()).ShouldContain((packageId, "1.0.0"));
            using var control = await client.DeleteAsync($"/tenant/1/admin/packages/{packageId}/1.0.0");
            control.StatusCode.ShouldBe(HttpStatusCode.OK, await control.Content.ReadAsStringAsync());
            observer.Events.Count.ShouldBe(1);
            (await repository.GetFromPackageAsync(packageId, "1.0.0", canonical)).ShouldBeNull();
            (await repository.ReactivatePackageAsync(packageId, "1.0.0", CancellationToken.None)).ShouldBe(1);
            observer.Events.Clear();

            using var response = await SendAsync(client,
                Entry("DELETE", route.Replace("{package}", packageId, StringComparison.Ordinal)),
                Entry("GET", $"Patient/missing-{Guid.NewGuid():N}"));

            (await repository.GetFromPackageAsync(packageId, "1.0.0", canonical)).ShouldNotBeNull();
            observer.Events.ShouldBeEmpty();
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!
                .GetValue<string>().ShouldBe("OperationOutcome");
        }
        finally
        {
            await repository.DeletePackageAsync(packageId, "1.0.0", CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("Patient?_id={id}")]
    [InlineData("Patient?name={name}")]
    [InlineData("Patient/{id}/_history")]
    [InlineData("Patient/_history")]
    [InlineData("Patient/{id}/_history/2")]
    public async Task GivenAWriteAndUnsupportedReadShape_WhenTransaction_ThenRejectsBeforeChangingResource(string readUrl)
    {
        var id = Guid.NewGuid().ToString();
        var newName = Guid.NewGuid().ToString("N");
        await PutAsync(id, "original");
        using var response = await SendAsync(fixture.Client,
            PatientPut(id, newName),
            Entry("GET", readUrl.Replace("{id}", id, StringComparison.Ordinal).Replace("{name}", newName, StringComparison.Ordinal)));

        var current = await ReadAsync($"Patient/{id}");
        current["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
        current["name"]![0]!["family"]!.GetValue<string>().ShouldBe("original");
        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!
            .GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenANewWriteAndSearch_WhenTransaction_ThenRejectsInsteadOfReturningAStaleSuccessfulSearch()
    {
        var id = Guid.NewGuid().ToString();
        using var response = await SendAsync(fixture.Client,
            PatientPut(id, "new"), Entry("GET", $"Patient?_id={id}"));
        (await fixture.Client.GetAsync($"/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task GivenAWriteAndPointReads_WhenTransaction_ThenGetAndHeadSeeTheStagedVersion()
    {
        var id = Guid.NewGuid().ToString();
        await PutAsync(id, "original");
        using var response = await SendAsync(fixture.Client, PatientPut(id, "updated"),
            Entry("GET", $"Patient/{id}"), Entry("HEAD", $"Patient/{id}"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var entries = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["entry"]!.AsArray();
        entries[1]!["resource"]!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("updated");
        entries[1]!["resource"]!["meta"]!["versionId"]!.GetValue<string>().ShouldBe("2");
        entries[2]!["resource"].ShouldBeNull();
        entries[2]!["response"]!["etag"]!.GetValue<string>().ShouldBe("W/\"2\"");
    }

    [Fact]
    public async Task GivenAReadOnlySearchTransaction_WhenExecuted_ThenTheNormalSearchRemainsSupported()
    {
        var id = Guid.NewGuid().ToString();
        await PutAsync(id, "searchable");
        using var response = await SendAsync(fixture.Client, Entry("GET", $"Patient?_id={id}"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var bundle = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        bundle["entry"]![0]!["resource"]!["entry"]![0]!["resource"]!["id"]!.GetValue<string>().ShouldBe(id);
        var self = bundle["entry"]![0]!["resource"]!["link"]!.AsArray()
            .Single(link => link!["relation"]!.GetValue<string>() == "self")!["url"]!.GetValue<string>();
        new Uri(self).Host.ShouldBe(fixture.Client.BaseAddress!.Host);
    }

    [Fact]
    public async Task GivenAReadOnlyHistoryTransaction_WhenExecuted_ThenTheNormalHistoryRemainsSupported()
    {
        var id = Guid.NewGuid().ToString();
        await PutAsync(id, "historical");
        using var response = await SendAsync(fixture.Client, Entry("GET", $"Patient/{id}/_history"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var bundle = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        bundle["entry"]![0]!["resource"]!["type"]!.GetValue<string>().ShouldBe("history");
        bundle["entry"]![0]!["resource"]!["entry"]![0]!["resource"]!["id"]!.GetValue<string>().ShouldBe(id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAConditionalDeleteSelectingTwoResources_WhenTransaction_ThenEveryWriteIsAtomic(bool laterFailure)
    {
        var firstId = Guid.NewGuid().ToString();
        var secondId = Guid.NewGuid().ToString();
        var name = Guid.NewGuid().ToString("N");
        await PutAsync(firstId, name);
        await PutAsync(secondId, name);
        var entries = new List<JsonObject> { Entry("DELETE", $"Patient?name={name}&_count=2") };
        if (laterFailure)
        {
            var failure = Entry("PUT", $"Patient/{Guid.NewGuid()}");
            failure["resource"] = JsonNode.Parse("""{"resourceType":"Observation","status":"final","code":{"text":"wrong type"}}""");
            entries.Add(failure);
        }
        using var response = await SendAsync(fixture.Client, entries.ToArray());
        if (laterFailure)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ReadAsync($"Patient/{firstId}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
            (await ReadAsync($"Patient/{secondId}"))["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            body["entry"]!.AsArray().Count.ShouldBe(1);
            body["entry"]![0]!["resource"]!["issue"]![0]!["diagnostics"]!.GetValue<string>()
                .ShouldContain("Deleted 2 matching resource(s)");
            body["entry"]![0]!["response"]!["etag"].ShouldBeNull();
            foreach (var id in new[] { firstId, secondId })
            {
                (await fixture.Client.GetAsync($"/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.Gone);
                (await fixture.Client.GetAsync($"/Patient/{id}/_history/2")).StatusCode.ShouldBe(HttpStatusCode.Gone);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAnExplicitPutWithUuidFullUrl_WhenReferencedInTransaction_ThenUsesTheRequestDestination(bool withQuery)
    {
        var patientId = Guid.NewGuid().ToString();
        var observationId = Guid.NewGuid().ToString();
        var alias = $"urn:uuid:{Guid.NewGuid()}";
        var patient = PatientPut(patientId, "known destination");
        patient["fullUrl"] = alias;
        if (withQuery)
        {
            patient["request"]!["url"] = $"Patient/{patientId}?_pretty=true";
        }
        var observation = Entry("PUT", $"Observation/{observationId}");
        observation["resource"] = JsonNode.Parse($$$"""{"resourceType":"Observation","id":"{{{observationId}}}","status":"final","code":{"text":"alias"},"subject":{"reference":"{{{alias}}}"}}""");
        using var response = await SendAsync(fixture.Client, observation, patient);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await ReadAsync($"Observation/{observationId}"))["subject"]!["reference"]!
            .GetValue<string>().ShouldBe($"Patient/{patientId}");
        var search = await ReadAsync($"Observation?_id={observationId}&subject=Patient/{patientId}");
        search["entry"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenContradictoryUuidAliases_WhenTransaction_ThenRejectsBeforeEitherWrite()
    {
        var firstId = Guid.NewGuid().ToString();
        var secondId = Guid.NewGuid().ToString();
        var alias = $"urn:uuid:{Guid.NewGuid()}";
        var first = PatientPut(firstId, "first");
        var second = PatientPut(secondId, "second");
        first["fullUrl"] = alias;
        second["fullUrl"] = alias;
        using var response = await SendAsync(fixture.Client, first, second);
        (await fixture.Client.GetAsync($"/Patient/{firstId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await fixture.Client.GetAsync($"/Patient/{secondId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task PutAsync(string id, string name)
    {
        using var response = await fixture.Client.PutAsync($"/Patient/{id}",
            Content(PatientPut(id, name)["resource"]!.ToJsonString()));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task<JsonNode> ReadAsync(string url)
    {
        using var response = await fixture.Client.GetAsync("/" + url);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static JsonObject PatientPut(string id, string name)
    {
        var entry = Entry("PUT", $"Patient/{id}");
        entry["resource"] = new JsonObject
        {
            ["resourceType"] = "Patient", ["id"] = id,
            ["name"] = new JsonArray(new JsonObject { ["family"] = name })
        };
        return entry;
    }

    private static JsonObject Entry(string method, string url) =>
        new() { ["request"] = new JsonObject { ["method"] = method, ["url"] = url } };

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, params JsonObject[] entries) =>
        client.PostAsync("/", Content(new JsonObject
        {
            ["resourceType"] = "Bundle", ["type"] = "transaction",
            ["entry"] = new JsonArray(entries.Select(e => (JsonNode)e).ToArray())
        }.ToJsonString()));

    private static StringContent Content(string json) => new(json, Encoding.UTF8, "application/fhir+json");

    private sealed class PackageEventObserver : INotificationHandler<PackageUnloadedEvent>
    {
        public ConcurrentQueue<PackageUnloadedEvent> Events { get; } = new();
        public Task HandleAsync(PackageUnloadedEvent notification, CancellationToken cancellationToken)
        {
            Events.Enqueue(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class ObservedPackageFixture(PackageEventObserver observer, string connection) : IgnixaApiFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((context, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenants:Configurations:1:Storage:ConnectionString"] = connection
                }));
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseServiceProviderFactory(new AutofacServiceProviderFactory(container =>
                container.RegisterInstance(observer).As<INotificationHandler<PackageUnloadedEvent>>()));
            return base.CreateHost(builder);
        }
    }
}
