// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Replays an AHDS-compatible deprovisioning client's call byte-for-byte: an unfiltered,
/// agnostic-route <c>DELETE /$bulk-delete?_type=DiagnosticReport%2CPatient</c> with a JSON
/// <c>Parameters</c> body, then polling the returned <c>Content-Location</c> the way such a
/// client does (honoring <c>Retry-After</c>; 202 means running, any other 2xx means done).
/// </summary>
/// <remarks>
/// The request has no search filter, so it deletes every Patient and DiagnosticReport in the tenant. It
/// therefore runs on its own host whose only tenant (tenant 1; tenant 2 stays inactive) points at a fresh,
/// empty database: the system partition inherits that connection string, so the DurableTask hub and the
/// SQL job repository live there too, and nothing the shared fixture's other tests wrote can be touched.
/// With exactly one active tenant the agnostic routes resolve to it, as in a single-tenant deployment.
/// </remarks>
[Collection(BulkDeleteTestCollection.Name)]
public class BulkDeleteAhdsClientCompatibilityTests(BulkDeleteApiFixture fixture, ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string DeprovisionParametersBody =
        """{"resourceType":"Parameters","parameter":[{"name":"hardDelete","valueBoolean":true},{"name":"purgeHistory","valueBoolean":true}]}""";

    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task GivenASingleTenantHost_WhenAhdsClientDeprovisionsDiagnosticReportsAndPatients_ThenEveryOneIsHardDeleted()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Use the shared validation wrapper.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = $"IgnixaBulkDeleteAhdsClient_{Guid.NewGuid():N}";
        builder.InitialCatalog = databaseName;
        var tenantConnectionString = builder.ConnectionString;
        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
#pragma warning disable CA2100
        using (var create = new SqlCommand($"CREATE DATABASE [{databaseName}]", master))
#pragma warning restore CA2100
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using var application = fixture.WithWebHostBuilder(webHost =>
                webHost.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Tenants:Configurations:1:Storage:ConnectionString"] = tenantConnectionString,
                    })));
            using var client = application.CreateClient();

            var patientIds = new[] { await PutAsync(client, Patient()), await PutAsync(client, Patient()) };
            var reportIds = new[]
            {
                await PutAsync(client, DiagnosticReport(patientIds[0])),
                await PutAsync(client, DiagnosticReport(patientIds[1])),
            };

            using var kickoffRequest = new HttpRequestMessage(HttpMethod.Delete, "/$bulk-delete?_type=DiagnosticReport%2CPatient");
            kickoffRequest.Headers.TryAddWithoutValidation("Prefer", "respond-async");
            kickoffRequest.Content = new StringContent(DeprovisionParametersBody, Encoding.UTF8);
            kickoffRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var kickoff = await client.SendAsync(kickoffRequest);

            kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
            var location = kickoff.Content.Headers.ContentLocation.ShouldNotBeNull();
            location.IsAbsoluteUri.ShouldBeTrue(location.ToString());
            location.AbsolutePath.ShouldStartWith("/_operations/bulk-delete/");
            location.ToString().ShouldNotContain("/tenant/");

            var (statusCode, body) = await PollLikeAhdsClientAsync(client, location);

            statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
            var counts = BulkDeleteClient.GetCounts(body);
            counts["Patient"].ShouldBe(2);
            counts["DiagnosticReport"].ShouldBe(2);

            (await SearchCountAsync(client, "Patient")).ShouldBe(0);
            (await SearchCountAsync(client, "DiagnosticReport")).ShouldBe(0);
            foreach (var id in patientIds)
            {
                (await client.GetAsync($"/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            }

            foreach (var id in reportIds)
            {
                (await client.GetAsync($"/DiagnosticReport/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            }
        }
        finally
        {
            using var tenantConnection = new SqlConnection(tenantConnectionString);
            SqlConnection.ClearPool(tenantConnection);
#pragma warning disable CA2100
            using var drop = new SqlCommand(
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", master);
#pragma warning restore CA2100
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// An AHDS-compatible client's polling contract: 202 means still running and carries
    /// <c>Retry-After</c>, which is waited out in full; the first non-202 response ends the job.
    /// Exercises Retry-After handling when the job is still running at the first poll; the
    /// 202/Retry-After/Progress contract is covered deterministically by unit tests of
    /// BulkDeleteStatusResponseBuilder and endpoint behaviors.
    /// </summary>
    private async Task<(HttpStatusCode StatusCode, JsonObject Body)> PollLikeAhdsClientAsync(HttpClient client, Uri location)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        var count202 = 0;
        while (true)
        {
            using var response = await client.GetAsync(location);
            var text = await response.Content.ReadAsStringAsync();
            var body = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)!.AsObject();
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                _output.WriteLine($"Received {count202} 202 responses during polling");
                return (response.StatusCode, body);
            }

            count202++;
            var retryAfter = response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldNotBeNull();
            if (DateTime.UtcNow + retryAfter >= deadline)
            {
                throw new TimeoutException($"Bulk delete job at '{location}' did not finish within {PollTimeout}. Last body: {body}");
            }

            await Task.Delay(retryAfter);
        }
    }

    private static async Task<int> SearchCountAsync(HttpClient client, string resourceType)
    {
        using var response = await client.GetAsync($"/{resourceType}");
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!["entry"]?.AsArray().Count ?? 0;
    }

    private static async Task<string> PutAsync(HttpClient client, JsonObject resource)
    {
        var type = resource["resourceType"]!.GetValue<string>();
        var id = resource["id"]!.GetValue<string>();
        using var content = new StringContent(resource.ToJsonString(), Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/{type}/{id}", content);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return id;
    }

    private static JsonObject Patient() => new()
    {
        ["resourceType"] = "Patient",
        ["id"] = Guid.NewGuid().ToString("N"),
        ["name"] = new JsonArray(new JsonObject { ["family"] = "Deprovisioned" }),
    };

    private static JsonObject DiagnosticReport(string patientId) => new()
    {
        ["resourceType"] = "DiagnosticReport",
        ["id"] = Guid.NewGuid().ToString("N"),
        ["status"] = "final",
        ["code"] = new JsonObject { ["text"] = "Deprovisioning test report" },
        ["subject"] = new JsonObject { ["reference"] = $"Patient/{patientId}" },
        ["issued"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
    };
}
