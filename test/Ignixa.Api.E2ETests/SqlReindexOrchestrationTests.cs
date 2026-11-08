using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Ignixa.Api.E2ETests._Infrastructure.SearchParameterLifecycleTestHelper;

namespace Ignixa.Api.E2ETests;

/// <summary>Thin end-to-end paths of spec §6-§8: a manual job, cancellation, and the follow-up job (F5, F6).</summary>
public class SqlReindexOrchestrationTests
{
    [SqlFact]
    public async Task GivenPreExistingResource_WhenAManualReindexIsPolledAndListed_ThenItCompletesAndSearchReturnsTheResource()
    {
        await using var fixture = await StartAsync(reindexAutoStart: false);
        var marker = Guid.NewGuid().ToString("N");
        var code = $"reindex-{marker}";
        var canonical = $"http://example.org/SearchParameter/{code}";
        // An absolute self-reference: reindexing Observation must extract it with the server's base URI, as a write does.
        var subject = $"https://example.test/fhir/Patient/reindex-{marker}";
        await PutAsync(fixture.Client, new { resourceType = "Patient", id = $"reindex-{marker}", identifier = new[] { new { system = "urn:reindex", value = marker } } });
        await PutAsync(fixture.Client, new { resourceType = "Observation", id = $"reindex-{marker}", status = "final", code = new { text = "test" }, subject = new { reference = subject } });
        await StoreParameterAsync(fixture.Services, $"test.reindex.{marker}", code, canonical);
        await StoreParameterAsync(fixture.Services, $"test.reindex.{marker}", $"subject-{marker}", $"http://example.org/SearchParameter/subject-{marker}", "Observation", "Observation.subject", "reference");
        var activation = await fixture.Services.GetRequiredService<PackageActivationPipeline>().ActivateAsync($"test.reindex.{marker}", "1.0.0", CancellationToken.None);

        activation.Success.ShouldBeTrue();
        activation.ReindexJobId.ShouldBeNull();
        var state = fixture.Services.GetRequiredService<ConformanceState>();
        state.FindByCanonical(canonical)!.Status.ShouldBe(SearchParameterStatus.Pending);
        RenewLease(fixture.Services);
        (await SearchAsync(fixture.Client, PatientQuery(code, marker))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var created = await fixture.Client.PostAsJsonAsync("/tenant/1/$reindex", new
        {
            resourceType = "Parameters",
            parameter = new object[]
            {
                new { name = "maximumNumberOfResourcesPerQuery", valueInteger = 10 },
                new { name = "maximumNumberOfResourcesPerWrite", valueInteger = 10 },
                new { name = "maximumConcurrency", valueInteger = 1 },
            },
        });
        var createdBody = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        created.StatusCode.ShouldBe(HttpStatusCode.Created, createdBody.ToJsonString());
        var jobId = ParameterValue(createdBody, "id");
        created.Content.Headers.ContentLocation.ShouldBe(new Uri($"/tenant/1/$reindex/{jobId}", UriKind.Relative));
        await WaitForStatusAsync(fixture.Client, jobId, "Completed");
        var list = JsonNode.Parse(await fixture.Client.GetStringAsync("/tenant/1/$reindex"))!;
        list["parameter"]!.AsArray()
            .Where(parameter => parameter!["name"]!.GetValue<string>() == "job")
            .Select(job => ParameterValue(job!, "id", "part"))
            .ShouldContain(jobId);

        state.FindByCanonical(canonical)!.Status.ShouldBe(SearchParameterStatus.Enabled);
        await RefreshAndRenewAsync(fixture.Services);
        (await SearchIdsAsync(fixture.Client, PatientQuery(code, marker))).ShouldContain($"reindex-{marker}");
        (await SearchIdsAsync(fixture.Client, $"/tenant/1/Observation?subject={Uri.EscapeDataString(subject)}")).ShouldContain($"reindex-{marker}");
    }

    [SqlFact]
    public async Task GivenRunningReindexJob_WhenCancelled_ThenStatusBecomesCancelled()
    {
        await using var fixture = await StartAsync();
        var marker = Guid.NewGuid().ToString("N");
        var canonical = $"http://example.org/SearchParameter/cancel-{marker}";
        await StoreParameterAsync(fixture.Services, $"test.cancel.{marker}", $"cancel-{marker}", canonical);
        var activation = await fixture.Services.GetRequiredService<PackageActivationPipeline>().ActivateAsync($"test.cancel.{marker}", "1.0.0", CancellationToken.None);
        var jobId = activation.ReindexJobId.ShouldNotBeNull();
        await WaitForJobAsync(fixture.Services, job => job.JobId == jobId && job.Status == "Running");
        var parameter = fixture.Services.GetRequiredService<ConformanceState>().FindByCanonical(canonical)!;
        parameter.Status.ShouldBe(SearchParameterStatus.Reindexing);
        parameter.ReindexJobId.ShouldBe(jobId);

        using var cancellation = await fixture.Client.DeleteAsync($"/tenant/1/$reindex/{jobId}");

        var body = await cancellation.Content.ReadAsStringAsync();
        cancellation.StatusCode.ShouldBe(HttpStatusCode.Accepted, body);
        ParameterValue(JsonNode.Parse(body)!, "status").ShouldBe("Cancelled");
        await WaitForStatusAsync(fixture.Client, jobId, "Cancelled");
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [SqlFact]
    public async Task GivenSecondPackageIsActivatedMidJob_WhenFirstCompletes_ThenFollowUpEnablesSecondParameter()
    {
        await using var fixture = await StartAsync();
        var marker = Guid.NewGuid().ToString("N");
        var firstCanonical = $"http://example.org/SearchParameter/first-{marker}";
        var secondCanonical = $"http://example.org/SearchParameter/second-{marker}";
        await PutAsync(fixture.Client, new { resourceType = "Patient", id = $"followup-{marker}", identifier = new[] { new { system = "urn:reindex", value = marker } } });
        await StoreParameterAsync(fixture.Services, $"test.first.{marker}", $"first-{marker}", firstCanonical);
        var pipeline = fixture.Services.GetRequiredService<PackageActivationPipeline>();
        var firstJobId = (await pipeline.ActivateAsync($"test.first.{marker}", "1.0.0", CancellationToken.None)).ReindexJobId.ShouldNotBeNull();
        await WaitForJobAsync(fixture.Services, job => job.JobId == firstJobId && job.Status == "Running");

        await StoreParameterAsync(fixture.Services, $"test.second.{marker}", $"second-{marker}", secondCanonical);
        var secondActivation = await pipeline.ActivateAsync($"test.second.{marker}", "1.0.0", CancellationToken.None);

        secondActivation.ReindexJobId.ShouldBe(firstJobId);
        secondActivation.Issues.ShouldContain(issue => issue.Code == PackageActivationPipeline.ReindexQueuedCode);
        (await WaitForJobAsync(fixture.Services, job => job.JobId == firstJobId && IsTerminal(job))).Status.ShouldBe("Completed");
        var followUp = await WaitForJobAsync(fixture.Services, job => job.JobId != firstJobId && job.Definition.Trigger == "Reconciliation");
        (await WaitForJobAsync(fixture.Services, job => job.JobId == followUp.JobId && IsTerminal(job))).Status.ShouldBe("Completed");
        var state = fixture.Services.GetRequiredService<ConformanceState>();
        state.FindByCanonical(firstCanonical)!.Status.ShouldBe(SearchParameterStatus.Enabled);
        state.FindByCanonical(secondCanonical)!.Status.ShouldBe(SearchParameterStatus.Enabled);
        await RefreshAndRenewAsync(fixture.Services);
        (await SearchIdsAsync(fixture.Client, PatientQuery($"second-{marker}", marker))).ShouldContain($"followup-{marker}");
    }

    private static async Task<ReindexFixture> StartAsync(bool reindexAutoStart = true)
    {
        var fixture = new ReindexFixture(reindexAutoStart);
        await fixture.InitializeAsync();
        return fixture;
    }

    private static string PatientQuery(string code, string marker) => $"/tenant/1/Patient?{code}={Uri.EscapeDataString($"urn:reindex|{marker}")}";

    private static bool IsTerminal(BackgroundJob<ReindexJobDefinition> job) => job.Status is "Completed" or "Failed" or "Cancelled";

    private static async Task<BackgroundJob<ReindexJobDefinition>> WaitForJobAsync(
        IServiceProvider services,
        Func<BackgroundJob<ReindexJobDefinition>, bool> condition)
    {
        var repository = services.GetRequiredService<IBackgroundJobRepository<ReindexJobDefinition>>();
        var expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < expires)
        {
            var match = (await repository.ListAsync((int)BackgroundJobType.Reindex, CancellationToken.None)).SingleOrDefault(condition);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException("No reindex job reached the expected state.");
    }

    private static async Task WaitForStatusAsync(HttpClient client, string jobId, string expectedStatus)
    {
        var expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < expires)
        {
            if (ParameterValue(JsonNode.Parse(await client.GetStringAsync($"/tenant/1/$reindex/{jobId}"))!, "status") == expectedStatus)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Reindex job {jobId} did not reach {expectedStatus}.");
    }

    // Completion does not refresh local consumers synchronously (§8.5); the sync tick does, so a test refreshes itself.
    private static async Task RefreshAndRenewAsync(IServiceProvider services)
    {
        await services.GetRequiredService<ConformanceRefresher>().RefreshAsync(force: false, CancellationToken.None);
        RenewLease(services);
    }

    private static string ParameterValue(JsonNode parameters, string name, string array = "parameter") =>
        parameters[array]!.AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == name)!["valueString"]!.GetValue<string>();

    private static async Task StoreParameterAsync(
        IServiceProvider services,
        string packageId,
        string code,
        string canonical,
        string resourceType = "Patient",
        string expression = "Patient.identifier",
        string type = "token") =>
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
                ResourceJson = new JsonObject
                {
                    ["resourceType"] = "SearchParameter", ["id"] = code, ["url"] = canonical, ["version"] = "1.0.0",
                    ["name"] = "ReindexMarker", ["status"] = "active", ["code"] = code, ["base"] = new JsonArray(resourceType),
                    ["type"] = type, ["expression"] = expression,
                }.ToJsonString(),
            },
            CancellationToken.None);

    private static async Task PutAsync(HttpClient client, object resource)
    {
        var node = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(resource))!;
        using var response = await client.PutAsJsonAsync($"/tenant/1/{node["resourceType"]}/{node["id"]}", resource);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private sealed class ReindexFixture(bool reindexAutoStart) : IgnixaApiFixture
    {
        protected override bool ReindexAutoStart => reindexAutoStart;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Conformance:MaxStaleness", "00:00:01");
            builder.UseSetting("Conformance:TransitionGrace", "00:00:02");
            builder.UseSetting("Conformance:TransitionSafetyMargin", "00:00:01");
            builder.UseSetting("Reindex:BarrierDelay", "00:00:01");
            // Follow-up jobs start from the periodic reconciliation tick.
            builder.UseSetting("Conformance:SyncIntervalSeconds", "1");
            builder.UseSetting("Reindex:DrainWarningAfter", "00:00:01");
            builder.UseSetting("Fhir:BaseUri", "https://example.test/fhir");
            base.ConfigureWebHost(builder);
        }
    }
}
