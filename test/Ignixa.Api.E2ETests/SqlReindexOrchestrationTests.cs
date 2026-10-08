using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Medino;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.E2ETests;

public class SqlReindexOrchestrationTests
{
    [SqlFact]
    public async Task GivenReindexRequest_WhenPollingAndListing_ThenJobCompletesAndIsListed()
    {
        await using var fixture = new ReindexFixture();
        await fixture.InitializeAsync();

        var created = await CreateReindexAsync(fixture.Client);
        using var createdResponse = created.Response;

        created.Response.StatusCode.ShouldBe(HttpStatusCode.Created, created.Body.ToJsonString());
        created.Response.Content.Headers.ContentLocation.ShouldBe(new Uri($"/tenant/1/$reindex/{created.JobId}", UriKind.Relative));
        created.Body["resourceType"]!.GetValue<string>().ShouldBe("Parameters");
        ParameterValue(created.Body, "id").ShouldBe(created.JobId);

        var completed = await WaitForStatusAsync(fixture.Client, created.JobId, "Completed");
        ParameterValue(completed, "status").ShouldBe("Completed");

        using var listResponse = await fixture.Client.GetAsync("/tenant/1/$reindex");
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await listResponse.Content.ReadAsStringAsync());
        var list = JsonNode.Parse(await listResponse.Content.ReadAsStringAsync())!;
        list["resourceType"]!.GetValue<string>().ShouldBe("Parameters");
        list["parameter"]!.AsArray()
            .Where(parameter => parameter!["name"]!.GetValue<string>() == "job")
            .Select(parameter => parameter!["part"]!.AsArray()
                .Single(part => part!["name"]!.GetValue<string>() == "id")!["valueString"]!.GetValue<string>())
            .ShouldContain(created.JobId);
    }

    [SqlFact]
    public async Task GivenActiveReindexJob_WhenCreatingAnother_ThenReturnsConflictForActiveJob()
    {
        await using var fixture = new ReindexFixture();
        await fixture.InitializeAsync();

        var first = await CreateReindexAsync(fixture.Client);
        using var firstResponse = first.Response;
        first.Response.StatusCode.ShouldBe(HttpStatusCode.Created, first.Body.ToJsonString());
        using var secondResponse = await fixture.Client.PostAsync("/tenant/1/$reindex", ReindexRequestContent());

        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict, await secondResponse.Content.ReadAsStringAsync());
        secondResponse.Content.Headers.ContentLocation.ShouldBe(
            new Uri($"/tenant/1/$reindex/{first.JobId}", UriKind.Relative));
        JsonNode.Parse(await secondResponse.Content.ReadAsStringAsync())!["resourceType"]!
            .GetValue<string>().ShouldBe("OperationOutcome");

        using var cleanup = await fixture.Client.DeleteAsync($"/tenant/1/$reindex/{first.JobId}");
        cleanup.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cleanup.Content.ReadAsStringAsync());
    }

    [SqlFact]
    public async Task GivenRunningReindexJob_WhenCancelled_ThenStatusBecomesCancelled()
    {
        await using var fixture = new ReindexFixture();
        await fixture.InitializeAsync();

        var created = await CreateReindexAsync(fixture.Client);
        using var createdResponse = created.Response;
        created.Response.StatusCode.ShouldBe(HttpStatusCode.Created, created.Body.ToJsonString());
        using var cancellation = await fixture.Client.DeleteAsync($"/tenant/1/$reindex/{created.JobId}");

        cancellation.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancellation.Content.ReadAsStringAsync());
        var cancellationBody = JsonNode.Parse(await cancellation.Content.ReadAsStringAsync())!;
        ParameterValue(cancellationBody, "status").ShouldBe("Cancelled");
        var cancelled = await WaitForStatusAsync(fixture.Client, created.JobId, "Cancelled");
        ParameterValue(cancelled, "status").ShouldBe("Cancelled");
    }

    [SqlFact]
    public async Task GivenReindexFeature_WhenReadingMetadataAndDefinition_ThenItIsAdvertisedAndRetrievable()
    {
        await using var fixture = new ReindexFixture();
        await fixture.InitializeAsync();

        using var definitionResponse = await fixture.Client.GetAsync("/tenant/1/OperationDefinition/reindex");
        definitionResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await definitionResponse.Content.ReadAsStringAsync());
        var definition = JsonNode.Parse(await definitionResponse.Content.ReadAsStringAsync())!;
        definition["resourceType"]!.GetValue<string>().ShouldBe("OperationDefinition");
        definition["code"]!.GetValue<string>().ShouldBe("reindex");

        using var metadataResponse = await fixture.Client.GetAsync("/metadata");
        metadataResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await metadataResponse.Content.ReadAsStringAsync());
        var metadata = JsonNode.Parse(await metadataResponse.Content.ReadAsStringAsync())!;
        metadata["rest"]![0]!["operation"]!.AsArray()
            .Select(operation => operation!["name"]!.GetValue<string>())
            .ShouldContain("reindex");
    }

    [SqlFact]
    public async Task GivenPreExistingResource_WhenPackageParameterIsActivatedAndReindexed_ThenSearchReturnsResource()
    {
        await using var fixture = new ReindexFixture();
        await fixture.InitializeAsync();
        var marker = Guid.NewGuid().ToString("N");
        var patientId = $"reindex-{marker}";
        var code = $"reindex-{marker}";
        var canonical = $"http://example.org/SearchParameter/{code}";
        var packageId = $"test.reindex.{marker}";

        await PutPatientAsync(fixture.Client, patientId, marker);
        await StoreParameterAsync(fixture.Services, packageId, code, canonical);
        var activation = await fixture.Services.GetRequiredService<PackageActivationPipeline>()
            .ActivateAsync(packageId, "1.0.0", CancellationToken.None);
        activation.Success.ShouldBeTrue();

        var state = fixture.Services.GetRequiredService<ConformanceState>();
        state.FindByCanonical(canonical)!.Status.ShouldBe(SearchParameterStatus.Pending);
        RenewLease(fixture.Services);
        await AssertPendingAsync(fixture.Client, code, marker);

        var mediator = fixture.Services.GetRequiredService<IMediator>();
        var created = await mediator.SendAsync(new CreateReindexJobCommand
        {
            TargetResourceTypes = ["Patient"],
            MaximumNumberOfResourcesPerQuery = 10,
            MaximumNumberOfResourcesPerWrite = 10,
            MaximumConcurrency = 1
        });
        var jobId = created.ShouldBeOfType<ReindexJobCreatedResult>().JobId;
        var job = await WaitForTerminalJobAsync(fixture.Services, jobId);

        job.Status.ShouldBe("Completed", job.ErrorMessage);
        state.FindByCanonical(canonical)!.Status.ShouldBe(SearchParameterStatus.Enabled);
        RenewLease(fixture.Services);
        await AssertSearchAsync(fixture.Client, code, marker, patientId);
    }

    private static async Task<BackgroundJob<ReindexJobDefinition>> WaitForTerminalJobAsync(
        IServiceProvider services,
        string jobId)
    {
        var repository = services.GetRequiredService<IBackgroundJobRepository<ReindexJobDefinition>>();
        var expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < expires)
        {
            var job = await repository.GetAsync(jobId, 1, CancellationToken.None);
            if (job?.Status is "Completed" or "Failed" or "Cancelled")
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        return await repository.GetAsync(jobId, 1, CancellationToken.None)
            ?? throw new InvalidOperationException($"Reindex job {jobId} was not persisted.");
    }

    private static async Task<(HttpResponseMessage Response, JsonNode Body, string JobId)> CreateReindexAsync(
        HttpClient client)
    {
        var response = await client.PostAsync("/tenant/1/$reindex", ReindexRequestContent());
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var jobId = response.StatusCode == HttpStatusCode.Created
            ? ParameterValue(body, "id")
            : string.Empty;
        return (response, body, jobId);
    }

    private static async Task<JsonNode> WaitForStatusAsync(HttpClient client, string jobId, string expectedStatus)
    {
        var expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < expires)
        {
            using var response = await client.GetAsync($"/tenant/1/$reindex/{jobId}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            if (ParameterValue(body, "status") == expectedStatus)
            {
                return body;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Reindex job {jobId} did not reach {expectedStatus}.");
    }

    private static StringContent ReindexRequestContent() =>
        new("""
            {"resourceType":"Parameters","parameter":[
              {"name":"targetResourceTypes","valueString":"Patient"},
              {"name":"maximumNumberOfResourcesPerQuery","valueInteger":10},
              {"name":"maximumNumberOfResourcesPerWrite","valueInteger":10},
              {"name":"maximumConcurrency","valueInteger":1}
            ]}
            """,
            Encoding.UTF8,
            "application/fhir+json");

    private static string ParameterValue(JsonNode parameters, string name) =>
        parameters["parameter"]!.AsArray()
            .Single(parameter => parameter!["name"]!.GetValue<string>() == name)!["valueString"]!
            .GetValue<string>();

    private static async Task StoreParameterAsync(
        IServiceProvider services,
        string packageId,
        string code,
        string canonical)
    {
        var parameter = new JsonObject
        {
            ["resourceType"] = "SearchParameter",
            ["id"] = code,
            ["url"] = canonical,
            ["version"] = "1.0.0",
            ["name"] = "ReindexMarker",
            ["status"] = "active",
            ["code"] = code,
            ["base"] = new JsonArray("Patient"),
            ["type"] = "token",
            ["expression"] = "Patient.identifier"
        };
        await services.GetRequiredService<IPackageResourceRepository>().UpsertAsync(
            new PackageResource
            {
                PackageId = packageId,
                PackageVersion = "1.0.0",
                ResourceType = "SearchParameter",
                ResourceId = code,
                Canonical = canonical,
                Version = "1.0.0",
                FhirVersion = "4.0.1",
                ResourceJson = parameter.ToJsonString()
            },
            CancellationToken.None);
    }

    private static async Task PutPatientAsync(HttpClient client, string id, string marker)
    {
        using var content = new StringContent(
            $$"""{"resourceType":"Patient","id":"{{id}}","identifier":[{"system":"urn:reindex","value":"{{marker}}"}]}""",
            Encoding.UTF8,
            "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/1/Patient/{id}", content);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertPendingAsync(HttpClient client, string code, string marker)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/tenant/1/Patient?{code}={Uri.EscapeDataString($"urn:reindex|{marker}")}");
        request.Headers.Add("Prefer", "handling=strict");
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertSearchAsync(
        HttpClient client,
        string code,
        string marker,
        string expectedId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/tenant/1/Patient?{code}={Uri.EscapeDataString($"urn:reindex|{marker}")}");
        request.Headers.Add("Prefer", "handling=strict");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var ids = JsonNode.Parse(body)!["entry"]?.AsArray()
            .Select(entry => entry!["resource"]!["id"]!.GetValue<string>())
            .ToArray() ?? [];
        ids.ShouldContain(expectedId);
    }

    private static void RenewLease(IServiceProvider services)
    {
        var lease = services.GetRequiredService<IConformanceLease>();
        lease.Renew(lease.CaptureStart());
    }

    private sealed class ReindexFixture : IgnixaApiFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Conformance:MaxStaleness", "00:00:01");
            builder.UseSetting("Conformance:TransitionGrace", "00:00:02");
            builder.UseSetting("Reindex:BarrierDelay", "00:00:01");
            builder.UseSetting("Reindex:DrainWarningAfter", "00:00:01");
            base.ConfigureWebHost(builder);
        }
    }

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires SQL-backed resources.";
            }
        }
    }
}
