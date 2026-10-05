using System.Data;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.Events;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ignixa.Api.E2ETests;

public class SqlActivationPreflightTests
{
    private const string RootOne = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string RootTwo = "http://example.org/SearchParameter/preflight-other";
    private const string OverrideUrl = "http://example.org/SearchParameter/preflight-override";
    private const string ValidNewUrl = "http://example.org/SearchParameter/after-rejection";
    private const string PackageId = "test.preflight";
    private const string Marker = "preflight-marker";

    [SqlTheory]
    [InlineData("canonical-root-change")]
    [InlineData("within-batch-conflict")]
    public async Task GivenInvalidProposedBatch_WhenActivationIsRejected_ThenEventsStateAndSubsequentRestartRemainUsable(string scenario)
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var database = $"IgnixaActivationPreflight_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = database }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quoted = names.QuoteIdentifier(database);
        await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE {quoted}");
        try
        {
            await AssertRejectedBatchAsync(connectionString, scenario);
        }
        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteDatabaseCommandAsync(master, $"DROP DATABASE {quoted}");
        }
    }

    private static async Task AssertRejectedBatchAsync(string connectionString, string scenario)
    {
        await using var template = new IgnixaApiFixture();
        string patientId = $"preflight-{Guid.NewGuid():N}";
        long eventsAfterValid;
        await using (var host = CreateHost(template, connectionString))
        {
            using var client = host.CreateClient();
            var services = host.Services;
            var pipeline = services.GetRequiredService<PackageActivationPipeline>();
            var state = services.GetRequiredService<ConformanceState>();
            await StoreAsync(services, "hl7.fhir.r4.core", "1", "identifier", RootOne, null);
            await StoreAsync(services, "hl7.fhir.r4.core", "1", "other-code", RootTwo, null);
            (await pipeline.ActivateAsync("hl7.fhir.r4.core", "1", CancellationToken.None)).Success.ShouldBeTrue();
            await StoreAsync(services, PackageId, "1", "identifier", OverrideUrl, RootOne);
            (await pipeline.ActivateAsync(PackageId, "1", CancellationToken.None)).Success.ShouldBeTrue();
            await SynchronizeAsync(services, "1");
            await PutAsync(client, patientId);
            using var beforeRead = await client.GetAsync($"/tenant/1/Patient/{patientId}");
            beforeRead.StatusCode.ShouldBe(HttpStatusCode.OK, await beforeRead.Content.ReadAsStringAsync());

            var before = Snapshot(state);
            long cursorBefore = state.LastProcessedEventId;
            long countBefore = await CountEventsAsync(connectionString);
            var packagesBefore = state.Packages.Keys.Order().ToArray();
            int nextId = state.AllSearchParameters.Values.Max(parameter => parameter.SearchParamId) + 1;
            var versions = services.GetRequiredService<IFhirVersionContext>();
            var warmIndexer = versions.GetSearchIndexer(FhirVersion.R4, 1);

            if (scenario == "canonical-root-change")
            {
                await StoreAsync(services, PackageId, "2", "new-before-reject",
                    "http://example.org/SearchParameter/a-preflight-new", null);
                await StoreAsync(services, PackageId, "2", "other-code", OverrideUrl, RootTwo);
            }
            else
            {
                await StoreAsync(services, PackageId, "2", "identifier",
                    "http://example.org/SearchParameter/batch-first", OverrideUrl);
                await StoreAsync(services, PackageId, "2", "identifier",
                    "http://example.org/SearchParameter/batch-second", OverrideUrl);
            }

            var rejected = await pipeline.ActivateAsync(PackageId, "2", CancellationToken.None);

            rejected.Success.ShouldBeFalse();
            rejected.Issues.ShouldNotBeEmpty();
            rejected.Issues.ShouldContain(issue => issue.Code == (scenario == "canonical-root-change"
                ? "SP_STORAGE_IDENTITY" : "SP_CONFLICT"));
            (await CountEventsAsync(connectionString)).ShouldBe(countBefore);
            state.LastProcessedEventId.ShouldBe(cursorBefore);
            Snapshot(state).ShouldBe(before);
            state.Packages.Keys.Order().ShouldBe(packagesBefore);
            ReferenceEquals(versions.GetSearchIndexer(FhirVersion.R4, 1), warmIndexer).ShouldBeTrue();
            await AssertSearchAsync(client, "identifier", patientId);

            await StoreAsync(services, PackageId, "3", "identifier", OverrideUrl, RootOne);
            await StoreAsync(services, PackageId, "3", "after-reject", ValidNewUrl, null);
            (await pipeline.ActivateAsync(PackageId, "3", CancellationToken.None)).Success.ShouldBeTrue();
            state.GetSearchParameter("Patient", "after-reject")!.SearchParamId.ShouldBe(nextId);
            await SynchronizeAsync(services, "3");
            await PutAsync(client, patientId, updated: true);
            await AssertSearchAsync(client, "after-reject", patientId);
            eventsAfterValid = await CountEventsAsync(connectionString);
            eventsAfterValid.ShouldBe(countBefore + 3);
        }

        await using var restarted = CreateHost(template, connectionString);
        using var restartedClient = restarted.CreateClient();
        await AssertSearchAsync(restartedClient, "identifier", patientId);
        await AssertSearchAsync(restartedClient, "after-reject", patientId);
        var replayed = restarted.Services.GetRequiredService<ConformanceState>();
        replayed.IsInitialized.ShouldBeTrue();
        replayed.Packages.ShouldNotContainKey($"{PackageId}@2");
        replayed.Packages.ShouldContainKey($"{PackageId}@3");
        replayed.GetSearchParameter("Patient", "identifier")!.OverridesCanonical.ShouldBe(RootOne);
        (await CountEventsAsync(connectionString)).ShouldBe(eventsAfterValid);
    }

    private static string[] Snapshot(ConformanceState state) =>
        state.AllSearchParameters.Values
            .Select(parameter => $"{parameter.ResourceType}|{parameter.Code}|{parameter.Canonical}|{parameter.SearchParamId}|{parameter.OverridesCanonical}|{parameter.SourcePackage}|{parameter.Status}")
            .Order().ToArray();

    private static async Task StoreAsync(IServiceProvider services, string package, string version,
        string code, string canonical, string? derivedFrom)
    {
        var parameter = new JsonObject
        {
            ["resourceType"] = "SearchParameter", ["id"] = code, ["url"] = canonical, ["version"] = version,
            ["name"] = "PreflightParameter", ["status"] = "active", ["code"] = code,
            ["base"] = new JsonArray("Patient"), ["type"] = "token", ["expression"] = "Patient.identifier"
        };
        if (derivedFrom is not null)
        {
            parameter["derivedFrom"] = derivedFrom;
        }
        await services.GetRequiredService<IPackageResourceRepository>().UpsertAsync(new PackageResource
        {
            PackageId = package, PackageVersion = version, ResourceType = "SearchParameter",
            ResourceId = canonical.Split('/').Last(), Canonical = canonical, Version = version,
            FhirVersion = "4.0.1", ResourceJson = parameter.ToJsonString()
        }, CancellationToken.None);
    }

    private static async Task SynchronizeAsync(IServiceProvider services, string version)
    {
        var handler = new PackageLoadedSearchParameterSyncHandler(
            services.GetRequiredService<IFhirVersionContext>(),
            services.GetRequiredService<SqlServerSearchIndexCacheRegistry>(),
            services.GetRequiredService<ITenantConfigurationStore>(),
            services.GetRequiredService<ICapabilityCacheInvalidator>(),
            services.GetRequiredService<ILogger<PackageLoadedSearchParameterSyncHandler>>());
        await handler.HandleAsync(new PackageLoadedEvent(PackageId, version, 1, DateTimeOffset.UtcNow), CancellationToken.None);
    }

    private static async Task PutAsync(HttpClient client, string id, bool updated = false)
    {
        using var content = new StringContent($$"""
            {"resourceType":"Patient","id":"{{id}}","identifier":[{"value":"{{Marker}}"}]}
            """, Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/1/Patient/{id}", content);
        response.StatusCode.ShouldBe(updated ? HttpStatusCode.OK : HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertSearchAsync(HttpClient client, string code, string id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/tenant/1/Patient?{code}={Marker}");
        request.Headers.Add("Prefer", "handling=strict");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var entries = JsonNode.Parse(body)!["entry"]!.AsArray();
        entries.Count.ShouldBe(1);
        entries[0]!["resource"]!["id"]!.GetValue<string>().ShouldBe(id);
    }

    private static WebApplicationFactory<Program> CreateHost(IgnixaApiFixture template, string connectionString) =>
        template.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenants:Configurations:1:Storage:ConnectionString"] = connectionString,
                ["Tenants:Configurations:1:Storage:Type"] = "SqlServer",
                ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
                ["Conformance:SyncIntervalSeconds"] = "3600"
            })));

    private static async Task<long> CountEventsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.SourceEvents", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteDatabaseCommandAsync(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXEC sys.sp_executesql @statement", connection);
        command.Parameters.Add("@statement", SqlDbType.NVarChar, -1).Value = statement;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SqlTheoryAttribute : TheoryAttribute
    {
        public SqlTheoryAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires real SQL activation and restart.";
            }
        }
    }
}
