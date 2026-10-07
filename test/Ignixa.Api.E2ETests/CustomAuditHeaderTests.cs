// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._Infrastructure.Exceptions;
using Ignixa.Domain.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Ignixa.Api.E2ETests;

/// <summary>
/// Custom audit headers (<c>X-IGNIXA-AUDIT-*</c>, plus the Azure Health Data Services compatible
/// <c>X-MS-AZUREFHIR-AUDIT-*</c>): captured onto audit events, limited to 10 headers of at most 2048 characters
/// (431 otherwise), carried onto every bundle entry, and carried from a bulk job's kick-off request onto its
/// completion audit event.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class CustomAuditHeaderTests(CustomAuditHeaderTests.AuditCaptureFixture fixture)
    : IClassFixture<CustomAuditHeaderTests.AuditCaptureFixture>
{
    private const string OperationIdHeader = "X-IGNIXA-AUDIT-OPERATIONID";
    private const string BundleIdHeader = "X-IGNIXA-AUDIT-BUNDLEID";
    private const string AzureFhirOperationIdHeader = "X-MS-AZUREFHIR-AUDIT-OPERATIONID";
    private const string AzureFhirBundleIdHeader = "X-MS-AZUREFHIR-AUDIT-BUNDLEID";

    private static readonly bool UsesFileSystem =
        Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    [Fact]
    public async Task GivenAzureFhirCompatibilityAuditHeaders_WhenPostingATransaction_ThenEveryAuditEventCarriesThem()
    {
        var operationId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tenant/1")
        {
            Content = new StringContent("""
                {
                  "resourceType": "Bundle",
                  "type": "transaction",
                  "entry": [
                    {
                      "resource": {"resourceType":"Patient","name":[{"family":"AhdsCompatAudited"}]},
                      "request": {"method":"POST","url":"Patient"}
                    }
                  ]
                }
                """, Encoding.UTF8, "application/fhir+json")
        };
        request.Headers.Add(AzureFhirOperationIdHeader, operationId);
        request.Headers.Add(AzureFhirBundleIdHeader, "ahds-bundle");

        using var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var events = fixture.Capture.HttpEvents
            .Where(e => e.CustomHeaders.TryGetValue(AzureFhirOperationIdHeader, out var value) && value == operationId)
            .ToList();
        events.Count.ShouldBe(2, "the bundle request plus its entry");
        events.ShouldAllBe(e => e.CustomHeaders[AzureFhirBundleIdHeader] == "ahds-bundle");
    }

    [Fact]
    public async Task GivenCustomAuditHeaders_WhenCreatingAndReadingAResource_ThenEachAuditEventCarriesThem()
    {
        var operationId = Guid.NewGuid().ToString("N");
        using var create = Request(HttpMethod.Post, "/tenant/1/Patient", operationId,
            """{"resourceType":"Patient","name":[{"family":"Audited"}]}""");
        using var created = await fixture.Client.SendAsync(create);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();

        using var read = Request(HttpMethod.Get, $"/tenant/1/Patient/{id}", operationId);
        using var readResponse = await fixture.Client.SendAsync(read);
        readResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var events = fixture.Capture.HttpEventsFor(operationId);
        events.Count.ShouldBe(2);
        events.Select(e => e.Method).ShouldBe(["POST", "GET"], ignoreOrder: true);
        events.ShouldAllBe(e => e.CustomHeaders[BundleIdHeader] == "bundle-" + operationId);
        events.ShouldAllBe(e => !e.CustomHeaders.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task GivenElevenCustomAuditHeaders_WhenRequesting_Then431OperationOutcomeAndRejectionIsAudited()
    {
        var path = $"/tenant/1/Patient/too-many-{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        for (var i = 0; i < 11; i++)
        {
            request.Headers.Add($"X-IGNIXA-AUDIT-H{i}", "v");
        }

        using var response = await fixture.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.RequestHeaderFieldsTooLarge, body);
        var issue = JsonNode.Parse(body)!["issue"]![0]!;
        issue["severity"]!.GetValue<string>().ShouldBe("error");
        issue["code"]!.GetValue<string>().ShouldBe("invalid");
        issue["diagnostics"]!.GetValue<string>().ShouldBe(
            "The maximum number of custom audit headers allowed is 10. The number of custom audit headers supplied is 11.");
        var rejection = fixture.Capture.HttpEvents.Single(e => e.Path == path);
        rejection.StatusCode.ShouldBe(431);
        rejection.CustomHeaders.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenOversizedCustomAuditHeaderValue_WhenRequesting_Then431WithoutEchoingTheValue()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant/1/Patient?_count=1");
        var value = "private-" + new string('x', 2048);
        request.Headers.Add("X-IGNIXA-AUDIT-SITE", value);

        using var response = await fixture.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.RequestHeaderFieldsTooLarge, body);
        JsonNode.Parse(body)!["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldBe(
            $"The maximum length of a custom audit header value is 2048. The supplied custom audit header 'X-IGNIXA-AUDIT-SITE' has length of {value.Length}.");
        body.ShouldNotContain("private-");
    }

    [Theory]
    [InlineData("transaction")]
    [InlineData("batch")]
    public async Task GivenBundleWithCustomAuditHeaders_WhenPosted_ThenEveryEntryAuditEventCarriesThem(string bundleType)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var putId = Guid.NewGuid().ToString("N");
        var bundle = $$"""
            {
              "resourceType": "Bundle",
              "type": "{{bundleType}}",
              "entry": [
                {
                  "resource": {"resourceType":"Patient","name":[{"family":"BundleAudited"}]},
                  "request": {"method":"POST","url":"Patient"}
                },
                {
                  "resource": {"resourceType":"Patient","id":"{{putId}}","name":[{"family":"BundleAudited"}]},
                  "request": {"method":"PUT","url":"Patient/{{putId}}"}
                }
              ]
            }
            """;
        using var request = Request(HttpMethod.Post, "/tenant/1", operationId, bundle);

        using var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var events = fixture.Capture.HttpEventsFor(operationId);
        events.Count.ShouldBe(3, "the bundle request plus one event per entry");
        events.Select(e => e.Path).ShouldBe(
            ["/tenant/1", "/tenant/1/Patient", $"/tenant/1/Patient/{putId}"], ignoreOrder: true);
        events.ShouldAllBe(e => e.CustomHeaders[BundleIdHeader] == "bundle-" + operationId);
    }

    [Fact]
    public async Task GivenExportWithCustomAuditHeaders_WhenItCompletes_ThenKickoffAndCompletionAreAuditedWithThem()
    {
        if (UsesFileSystem)
        {
            throw new SkipException("Bulk jobs complete in-process only on the SQL Server DurableTask provider");
        }

        var operationId = Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var kickoff = Request(HttpMethod.Post, "/tenant/1/$export?_type=Patient", operationId);

        using var started = await fixture.Client.SendAsync(kickoff, timeout.Token);

        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync(timeout.Token));
        fixture.Capture.HttpEventsFor(operationId).ShouldHaveSingleItem().Path.ShouldBe("/tenant/1/$export");
        var jobId = JsonNode.Parse(await started.Content.ReadAsStringAsync(timeout.Token))!["jobId"]!.GetValue<string>();
        var statusUrl = started.Content.Headers.GetValues("Content-Location").Single();
        string status;
        while (true)
        {
            using var poll = await fixture.Client.GetAsync(statusUrl, timeout.Token);
            status = $"{(int)poll.StatusCode} {await poll.Content.ReadAsStringAsync(timeout.Token)}";
            if (poll.StatusCode != HttpStatusCode.Accepted)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }

        status.ShouldStartWith("200", customMessage: status);
        var completion = await fixture.Capture.WaitForJobAsync(jobId, timeout.Token);

        completion.JobType.ShouldBe("Export");
        completion.Status.ShouldBe("Completed");
        completion.Outcome.ShouldBe("0");
        completion.CustomHeaders[OperationIdHeader].ShouldBe(operationId);
        completion.CustomHeaders[BundleIdHeader].ShouldBe("bundle-" + operationId);

        // Repeated polls and activity replays must not emit a second completion event.
        using var repoll = await fixture.Client.GetAsync(statusUrl, timeout.Token);
        await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
        fixture.Capture.JobEventsFor(jobId).Count.ShouldBe(1);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string operationId, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(OperationIdHeader, operationId);
        request.Headers.Add(BundleIdHeader, "bundle-" + operationId);
        request.Headers.Add("Authorization", "Bearer not-an-audit-header");
        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/fhir+json");
        }

        return request;
    }

    public sealed class AuditCapture
    {
        private readonly ConcurrentQueue<HttpRequestAuditEvent> _httpEvents = new();
        private readonly ConcurrentQueue<BackgroundJobAuditEvent> _jobEvents = new();

        public IReadOnlyCollection<HttpRequestAuditEvent> HttpEvents => _httpEvents;

        public IReadOnlyList<HttpRequestAuditEvent> HttpEventsFor(string operationId) =>
            _httpEvents.Where(e => e.CustomHeaders.TryGetValue(OperationIdHeader, out var value) && value == operationId).ToList();

        public void Add(HttpRequestAuditEvent auditEvent) => _httpEvents.Enqueue(auditEvent);

        public void Add(BackgroundJobAuditEvent auditEvent) => _jobEvents.Enqueue(auditEvent);

        public IReadOnlyList<BackgroundJobAuditEvent> JobEventsFor(string jobId) =>
            _jobEvents.Where(e => e.JobId == jobId).ToList();

        public async Task<BackgroundJobAuditEvent> WaitForJobAsync(string jobId, CancellationToken cancellationToken)
        {
            while (true)
            {
                var completion = _jobEvents.FirstOrDefault(e => e.JobId == jobId);
                if (completion != null)
                {
                    return completion;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
    }

    public sealed class AuditCaptureFixture : IgnixaApiFixture
    {
        private readonly string _orchestrationDirectory =
            Path.Combine(AppContext.BaseDirectory, "custom-audit-header-e2e", Guid.NewGuid().ToString("N"));

        public AuditCaptureFixture() : base("IgnixaE2EAudit")
        {
        }

        public AuditCapture Capture { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Bulk jobs complete in-process on the SQL providers (as in BulkImportCompositionRootTests).
            if (!UsesFileSystem)
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["BackgroundJobs:Repository"] = "SqlServer",
                        ["DurableTask:Provider"] = "SqlServer",
                        ["DurableTask:SqlServer:TaskHubName"] = "customauditheaders",
                        ["FhirRepository:BaseDirectory"] = _orchestrationDirectory,
                        ["TtlCleanup:Enabled"] = "false"
                    }));
            }
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseServiceProviderFactory(new AutofacServiceProviderFactory(container =>
                container.RegisterDecorator<IAuditLogger>((_, _, inner) => new CapturingAuditLogger(inner, Capture))));
            return base.CreateHost(builder);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Directory.Exists(_orchestrationDirectory))
            {
                Directory.Delete(_orchestrationDirectory, recursive: true);
            }
        }
    }

    private sealed class CapturingAuditLogger(IAuditLogger inner, AuditCapture capture) : IAuditLogger
    {
        public void LogTenantAccess(string userId, int tenantId, string operation, string resourceType, string? resourceId, bool authorized) =>
            inner.LogTenantAccess(userId, tenantId, operation, resourceType, resourceId, authorized);

        public void LogHttpRequest(HttpRequestAuditEvent auditEvent)
        {
            capture.Add(auditEvent);
            inner.LogHttpRequest(auditEvent);
        }

        public void LogTtlDeletion(int tenantId, string resourceType, string resourceId, DateTimeOffset expiresAt, bool success) =>
            inner.LogTtlDeletion(tenantId, resourceType, resourceId, expiresAt, success);

        public void LogBackgroundJobCompleted(BackgroundJobAuditEvent auditEvent)
        {
            capture.Add(auditEvent);
            inner.LogBackgroundJobCompleted(auditEvent);
        }
    }
}
