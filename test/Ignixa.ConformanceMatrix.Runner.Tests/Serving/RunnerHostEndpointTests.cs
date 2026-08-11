using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ignixa.ConformanceMatrix.Runner.Serving;
using Ignixa.Specification.Generated;
using Ignixa.TestScript.Client;
using Microsoft.AspNetCore.Builder;
using Shouldly;

namespace Ignixa.ConformanceMatrix.Runner.Tests.Serving;

public class RunnerHostEndpointTests : IAsyncLifetime, IAsyncDisposable
{
    private TempTestsDirectory _workspace = null!;
    private WebApplication _app = null!;
    private FhirTargetCache _targetCache = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _workspace = new TempTestsDirectory();
        _workspace.WriteScript("Search/PatientSearch.json", TempTestsDirectory.ValidScriptJson("PatientSearch"));
        _workspace.WriteScript("Broken.json", "{ not json");

        var registry = TestScriptRegistry.Load(_workspace.Root);

        // Any request from a target's HttpClient — e.g. the /metadata CapabilityStatement fetch —
        // answers 404 so the run never touches a real network hop and capability gating fails open.
        _targetCache = new FhirTargetCache(
            authHeader: null,
            handlerFactory: () => new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var stubProvider = new StubTestRequestProvider(new TestResponse
        {
            StatusCode = 200,
            RawBody = """{"resourceType":"Bundle"}"""
        });

        _app = RunnerHost.Create(
            registry,
            _targetCache,
            new R4CoreSchemaProvider(),
            "127.0.0.1",
            0,
            defaultFhirVersion: null,
            providerFactory: _ => stubProvider);

        await _app.StartAsync();
        var boundUrl = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(boundUrl) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _targetCache.Dispose();
        _workspace.Dispose();
    }

    // xunit's IAsyncLifetime.DisposeAsync (above) already drives teardown; this satisfies CA1001
    // (a type holding IDisposable fields should itself be disposable) without a second teardown path.
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return new ValueTask(DisposeAsync());
    }

    [Fact]
    public async Task GivenRunningHost_WhenHealthChecked_ThenReturnsScriptCounts()
    {
        // Act
        var response = await _client.GetAsync(new Uri("/healthz", UriKind.Relative));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("ok");
        body.GetProperty("scripts").GetInt32().ShouldBe(2);
        body.GetProperty("invalidScripts").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task GivenRunningHost_WhenListingTestScripts_ThenReturnsEachEntryIncludingTheInvalidOne()
    {
        // Act
        var response = await _client.GetAsync(new Uri("/testscripts", UriKind.Relative));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<TestScriptListEntry>>();
        entries.ShouldNotBeNull();
        entries!.Count.ShouldBe(2);
        entries.ShouldContain(e => e.Id == "PatientSearch" && e.Valid);
        entries.ShouldContain(e => e.Id == "Broken" && !e.Valid && e.Error != null);
    }

    [Fact]
    public async Task GivenValidRequest_WhenRunning_ThenReturnsOperationsWithStatusAndDuration()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/run", new { testScriptId = "PatientSearch", fhirBaseUrl = "http://fhir.test/" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RunResponse>();
        result.ShouldNotBeNull();
        result!.TestScriptId.ShouldBe("PatientSearch");
        result.Operations.ShouldNotBeEmpty();
        result.Operations.ShouldAllBe(op => op.StatusCode == 200);
        result.Operations.ShouldAllBe(op => op.DurationMs >= 0);
    }

    [Fact]
    public async Task GivenUnknownTestScriptId_WhenRunning_ThenReturnsNotFound()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/run", new { testScriptId = "DoesNotExist", fhirBaseUrl = "http://fhir.test/" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenStatusOnlyAssertions_WhenRunning_ThenReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/run", new
        {
            testScriptId = "PatientSearch",
            fhirBaseUrl = "http://fhir.test/",
            options = new { assertions = "status-only" }
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenParseFailedScript_WhenRunning_ThenReturnsUnprocessableEntity()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/run", new { testScriptId = "Broken", fhirBaseUrl = "http://fhir.test/" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GivenMissingFhirBaseUrl_WhenRunning_ThenReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/run", new { testScriptId = "PatientSearch", fhirBaseUrl = "" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenInvalidMode_WhenRunning_ThenReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/run", new
        {
            testScriptId = "PatientSearch",
            fhirBaseUrl = "http://fhir.test/",
            mode = "load"
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
