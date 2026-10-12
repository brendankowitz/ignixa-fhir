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
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.E2ETests;

public class SqlReindexOrchestrationTests
{
    [SqlFact]
    public async Task GivenPreExistingResource_WhenAManualReindexIsPolledAndListed_ThenItCompletesAndSearchReturnsTheResource()
    {
        await using var fixture = new ReindexFixture(reindexAutoStart: false);
        await fixture.InitializeAsync();
        var marker = Guid.NewGuid().ToString("N");
        var patientId = $"reindex-{marker}";
        var observationId = $"reindex-{marker}";
        var code = $"reindex-{marker}";
        var canonical = $"http://example.org/SearchParameter/{code}";
        // An absolute self-reference: reindexing Observation must extract it with the server's base URI, as a write does.
        var subject = $"https://example.test/fhir/Patient/{patientId}";
        await PutPatientAsync(fixture.Client, patientId, marker);
        await PutObservationAsync(fixture.Client, observationId, subject);
        await StoreParameterAsync(fixture.Services, $"test.reindex.{marker}", code, canonical);
        await StoreParameterAsync(
            fixture.Services,
            $"test.reindex.{marker}",
            $"subject-{marker}",
            $"http://example.org/SearchParameter/subject-{marker}",
            resourceType: "Observation",
            expression: "Observation.subject",
            type: "reference");
        var activation = await fixture.Services.GetRequiredService<PackageActivationPipeline>()
            .ActivateAsync($"test.reindex.{marker}", "1.0.0", CancellationToken.None);

        activation.Success.ShouldBeTrue();
        activation.ReindexJobId.ShouldBeNull();
        var state = fixture.Services.GetRequiredService<ConformanceState>();
        state.FindByCanonical(canonical)!.Status.ShouldBe(SearchParameterStatus.Pending);
        RenewLease(fixture.Services);
        (await SearchAsync(fixture.Client, "Patient", code, $"urn:reindex|{marker}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var created = await fixture.Client.PostAsync("/tenant/1/$reindex", ReindexRequestContent());
        var createdBody = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        created.StatusCode.ShouldBe(HttpStatusCode.Created, createdBody.ToJsonString());
        var jobId = ParameterValue(createdBody, "id");
        created.Content.Headers.ContentLocation.ShouldBe(new Uri($"/tenant/1/$reindex/{jobId}", UriKind.Relative));
        await WaitForStatusAsync(fixture.Client, jobId, "Completed");
        using var listResponse = await fixture.Client.GetAsync("/tenant/1/$reindex");
        var list = JsonNode.Parse(await listResponse.Content.ReadAsStringAsync())!;
        list["parameter"]!.AsArray()
            .Where(parameter => parameter!["name"]!.GetValue<string>() == "job")
            .Select(parameter => parameter!["part"]!.AsArray()
                .Single(part => part!["name"]!.GetValue<string>() == "id")!["valueString"]!.GetValue<string>())
            .ShouldContain(jobId);

        state.FindByCanonical(canonical)!.Status.ShouldBe(SearchParameterStatus.Enabled);
        await RefreshConformanceConsumersAsync(fixture.Services);
        RenewLease(fixture.Services);
        (await SearchAsync(fixture.Client, "Patient", code, $"urn:reindex|{marker}")).Ids.ShouldContain(patientId);
        (await SearchAsync(fixture.Client, "Observation", "subject", subject)).Ids.ShouldContain(observationId);
    }

    [SqlFact]
    public async Task GivenRunningReindexJob_WhenCancelled_ThenStatusBecomesCancelled()
    {
        await using var fixture = new ReindexFixture();
        await fixture.InitializeAsync();
        var marker = Guid.NewGuid().ToString("N");
        var code = $"cancel-{marker}";
        var canonical = $"http://example.org/SearchParameter/{code}";

        await StoreParameterAsync(fixture.Services, $"test.cancel.{marker}", code, canonical);
        var activation = await fixture.Services.GetRequiredService<PackageActivationPipeline>()
            .ActivateAsync($"test.cancel.{marker}", "1.0.0", CancellationToken.None);
        var jobId = activation.ReindexJobId.ShouldNotBeNull();
        await WaitForJobAsync(fixture.Services, jobId, job => job.Status == "Running");
        var parameter = fixture.Services.GetRequiredService<ConformanceState>().FindByCanonical(canonical)!;
        parameter.Status.ShouldBe(SearchParameterStatus.Reindexing);
        parameter.ReindexJobId.ShouldBe(jobId);

        using var cancellation = await fixture.Client.DeleteAsync($"/tenant/1/$reindex/{jobId}");

        cancellation.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancellation.Content.ReadAsStringAsync());
        ParameterValue(JsonNode.Parse(await cancellation.Content.ReadAsStringAsync())!, "status").ShouldBe("Cancelled");
        await WaitForStatusAsync(fixture.Client, jobId, "Cancelled");
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [SqlFact]
    public async Task GivenSecondPackageIsActivatedMidJob_WhenFirstCompletes_ThenFollowUpEnablesSecondParameter()
    {
        await using var fixture = new ReindexFixture();
        await fixture.InitializeAsync();
        var marker = Guid.NewGuid().ToString("N");
        var patientId = $"reindex-followup-{marker}";
        var firstCanonical = $"http://example.org/SearchParameter/first-{marker}";
        var secondCode = $"second-{marker}";
        var secondCanonical = $"http://example.org/SearchParameter/{secondCode}";

        await PutPatientAsync(fixture.Client, patientId, marker);
        await StoreParameterAsync(fixture.Services, $"test.first.{marker}", $"first-{marker}", firstCanonical);
        var pipeline = fixture.Services.GetRequiredService<PackageActivationPipeline>();
        var firstJobId = (await pipeline.ActivateAsync($"test.first.{marker}", "1.0.0", CancellationToken.None))
            .ReindexJobId.ShouldNotBeNull();
        await WaitForJobAsync(fixture.Services, firstJobId, job => job.Status == "Running");

        await StoreParameterAsync(fixture.Services, $"test.second.{marker}", secondCode, secondCanonical);
        var secondActivation = await pipeline.ActivateAsync($"test.second.{marker}", "1.0.0", CancellationToken.None);

        secondActivation.ReindexJobId.ShouldBe(firstJobId);
        secondActivation.Issues.ShouldContain(issue => issue.Code == PackageActivationPipeline.ReindexQueuedCode);
        (await WaitForJobAsync(fixture.Services, firstJobId, IsTerminal)).Status.ShouldBe("Completed");
        var followUp = await WaitForJobAsync(
            fixture.Services,
            job => job.JobId != firstJobId && job.Definition.Trigger == "Reconciliation");
        (await WaitForJobAsync(fixture.Services, followUp.JobId, IsTerminal)).Status.ShouldBe("Completed");

        var state = fixture.Services.GetRequiredService<ConformanceState>();
        state.FindByCanonical(firstCanonical)!.Status.ShouldBe(SearchParameterStatus.Enabled);
        state.FindByCanonical(secondCanonical)!.Status.ShouldBe(SearchParameterStatus.Enabled);
        await RefreshConformanceConsumersAsync(fixture.Services);
        RenewLease(fixture.Services);
        (await SearchAsync(fixture.Client, "Patient", secondCode, $"urn:reindex|{marker}")).Ids.ShouldContain(patientId);
    }

    private static bool IsTerminal(BackgroundJob<ReindexJobDefinition> job) =>
        job.Status is "Completed" or "Failed" or "Cancelled";

    private static Task<BackgroundJob<ReindexJobDefinition>> WaitForJobAsync(
        IServiceProvider services,
        string jobId,
        Func<BackgroundJob<ReindexJobDefinition>, bool> condition) =>
        WaitForJobAsync(services, job => job.JobId == jobId && condition(job));

    private static async Task<BackgroundJob<ReindexJobDefinition>> WaitForJobAsync(
        IServiceProvider services,
        Func<BackgroundJob<ReindexJobDefinition>, bool> condition)
    {
        var repository = services.GetRequiredService<IBackgroundJobRepository<ReindexJobDefinition>>();
        var expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < expires)
        {
            var match = (await repository.ListAsync((int)BackgroundJobType.Reindex, CancellationToken.None))
                .SingleOrDefault(condition);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException("No reindex job reached the expected state.");
    }

    private static Task RefreshConformanceConsumersAsync(IServiceProvider services) =>
        services.GetRequiredService<ConformanceRefresher>()
            .RefreshAsync(force: false, CancellationToken.None);

    private static async Task WaitForStatusAsync(HttpClient client, string jobId, string expectedStatus)
    {
        var expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < expires)
        {
            using var response = await client.GetAsync($"/tenant/1/$reindex/{jobId}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            if (ParameterValue(JsonNode.Parse(await response.Content.ReadAsStringAsync())!, "status") == expectedStatus)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Reindex job {jobId} did not reach {expectedStatus}.");
    }

    private static StringContent ReindexRequestContent() =>
        new("""
            {"resourceType":"Parameters","parameter":[
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
        string canonical,
        string resourceType = "Patient",
        string expression = "Patient.identifier",
        string type = "token")
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
            ["base"] = new JsonArray(resourceType),
            ["type"] = type,
            ["expression"] = expression
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

    private static async Task PutObservationAsync(HttpClient client, string id, string subjectReference)
    {
        var observation = new JsonObject
        {
            ["resourceType"] = "Observation",
            ["id"] = id,
            ["status"] = "final",
            ["code"] = new JsonObject { ["text"] = "test" },
            ["subject"] = new JsonObject { ["reference"] = subjectReference }
        };
        using var content = new StringContent(observation.ToJsonString(), Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/1/Observation/{id}", content);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A strict search, so a parameter that is not yet searchable answers 400 instead of being ignored.</summary>
    private static async Task<(HttpStatusCode StatusCode, IReadOnlyList<string> Ids)> SearchAsync(
        HttpClient client,
        string resourceType,
        string code,
        string value)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/tenant/1/{resourceType}?{code}={Uri.EscapeDataString(value)}");
        request.Headers.Add("Prefer", "handling=strict");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, []);
        }

        var ids = JsonNode.Parse(body)!["entry"]?.AsArray()
            .Select(entry => entry!["resource"]!["id"]!.GetValue<string>())
            .ToArray() ?? [];
        return (response.StatusCode, ids);
    }

    private static void RenewLease(IServiceProvider services)
    {
        var lease = services.GetRequiredService<ConformanceLease>();
        lease.Renew(lease.CaptureStart());
    }

    private sealed class ReindexFixture(bool reindexAutoStart = true) : IgnixaApiFixture
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
