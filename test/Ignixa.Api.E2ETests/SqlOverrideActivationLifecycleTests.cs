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
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
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

public class SqlOverrideActivationLifecycleTests
{
    private const string BaseUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string PackageUrl = "http://example.org/SearchParameter/activation-identifier";
    private const string ChainedUrl = "http://example.org/SearchParameter/chained-identifier";
    private const string PackageId = "test.override.activation";
    private const string System = "urn:override-activation";

    [SqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenSuccessiveRealActivations_WhenUpgradedChainedAndReplayed_ThenEveryWriteKeepsTheRootIdentity(bool legacyEvents)
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var database = $"IgnixaOverrideActivation_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = database }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quotedDatabase = names.QuoteIdentifier(database);
        await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE {quotedDatabase}");
        try
        {
            await AssertLifecycleAsync(connectionString, legacyEvents);
        }
        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteDatabaseCommandAsync(master, $"DROP DATABASE {quotedDatabase}");
        }
    }

    private static async Task AssertLifecycleAsync(string connectionString, bool legacyEvents)
    {
        await using var template = new IgnixaApiFixture();
        var marker = Guid.NewGuid().ToString("N");
        List<string> ids = [];
        short rootId;
        await using (var host = CreateHost(template, connectionString))
        {
            using var client = host.CreateClient();
            await WriteAndAssertAsync(client, marker, ids);
            var registry = host.Services.GetRequiredService<SqlServerSearchIndexCacheRegistry>();
            var cache = await registry.GetOrCreateAsync(1, CancellationToken.None);
            rootId = await cache.GetSearchParamIdAsync(BaseUrl, CancellationToken.None)
                ?? throw new InvalidOperationException("Missing core identifier ID.");

            await ActivateAsync(host.Services, "hl7.fhir.r4.core", "1", BaseUrl, null);
            await ActivateAsync(host.Services, "hl7.fhir.r4.core", "2", BaseUrl, null);
            await ActivateAsync(host.Services, PackageId, "1", PackageUrl, BaseUrl);
            await WriteAndAssertAsync(client, marker, ids);
            await ActivateAsync(host.Services, PackageId, "2", PackageUrl, BaseUrl);
            await WriteAndAssertAsync(client, marker, ids);
            await ActivateAsync(host.Services, "test.override.chain", "1", ChainedUrl, PackageUrl);
            await WriteAndAssertAsync(client, marker, ids);
            await AssertAliasesAsync(cache, rootId);

            var state = host.Services.GetRequiredService<ConformanceState>();
            state.GetSearchParameter("Patient", "identifier")!.OverridesCanonical.ShouldBe(BaseUrl);
            var store = host.Services.GetRequiredService<ISourceEventStore>();
            var upgrades = new List<SearchParameterActivated>();
            await foreach (var row in store.ReadAllAsync(CancellationToken.None))
            {
                if (row.Data is SearchParameterActivated activation && activation.SourcePackage == $"{PackageId}@2")
                {
                    upgrades.Add(activation);
                }
            }
            upgrades.Single().Overrides!.OverridesCanonical.ShouldBe(BaseUrl);
        }

        if (legacyEvents)
        {
            await MakeLegacyOverrideEventAsync(connectionString, $"package:{PackageId}@2", PackageUrl);
            await MakeLegacyOverrideEventAsync(connectionString, "package:test.override.chain@1", PackageUrl);
        }

        await using var restarted = CreateHost(template, connectionString);
        using var restartedClient = restarted.CreateClient();
        await AssertPatientsAsync(restartedClient, marker, ids);
        var replayed = restarted.Services.GetRequiredService<ConformanceState>();
        replayed.GetSearchParameter("Patient", "identifier")!.OverridesCanonical.ShouldBe(BaseUrl);
        var freshCache = await restarted.Services.GetRequiredService<SqlServerSearchIndexCacheRegistry>()
            .GetOrCreateAsync(1, CancellationToken.None);
        await AssertAliasesAsync(freshCache, rootId);
        await WriteAndAssertAsync(restartedClient, marker, ids);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(
            "SELECT COUNT(*), COUNT(DISTINCT SearchParamId), MIN(SearchParamId) FROM dbo.TokenSearchParam WHERE Code = @Code",
            connection);
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 256).Value = marker;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(ids.Count);
        reader.GetInt32(1).ShouldBe(1);
        reader.GetInt16(2).ShouldBe(rootId);
    }

    private static async Task ActivateAsync(IServiceProvider services, string packageId, string version, string canonical, string? derivedFrom)
    {
        var resource = new JsonObject
        {
            ["resourceType"] = "SearchParameter",
            ["id"] = "identifier",
            ["url"] = canonical,
            ["version"] = version,
            ["name"] = "ActivationIdentifier",
            ["status"] = "active",
            ["code"] = "identifier",
            ["base"] = new JsonArray("Patient"),
            ["type"] = "token",
            ["expression"] = "Patient.identifier"
        };
        if (derivedFrom is not null)
        {
            resource["derivedFrom"] = derivedFrom;
        }
        await services.GetRequiredService<IPackageResourceRepository>().UpsertAsync(new PackageResource
        {
            PackageId = packageId, PackageVersion = version, ResourceType = "SearchParameter",
            ResourceId = "identifier", Canonical = canonical, Version = version, FhirVersion = "4.0.1",
            ResourceJson = resource.ToJsonString()
        }, CancellationToken.None);
        var result = await services.GetRequiredService<PackageActivationPipeline>()
            .ActivateAsync(packageId, version, CancellationToken.None);
        result.Success.ShouldBeTrue();

        var handler = new PackageLoadedSearchParameterSyncHandler(
            services.GetRequiredService<IFhirVersionContext>(),
            services.GetRequiredService<SqlServerSearchIndexCacheRegistry>(),
            services.GetRequiredService<ITenantConfigurationStore>(),
            services.GetRequiredService<ICapabilityCacheInvalidator>(),
            services.GetRequiredService<ILogger<PackageLoadedSearchParameterSyncHandler>>());
        await handler.HandleAsync(new PackageLoadedEvent(packageId, version, 1, DateTimeOffset.UtcNow), CancellationToken.None);
    }

    private static async Task AssertAliasesAsync(SqlServerSearchIndexReferenceDataCache cache, short rootId)
    {
        foreach (var canonical in new[] { BaseUrl, PackageUrl, ChainedUrl })
        {
            (await cache.GetSearchParamIdAsync(canonical, CancellationToken.None)).ShouldBe(rootId, canonical);
        }
    }

    private static async Task MakeLegacyOverrideEventAsync(string connectionString, string streamId, string target)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(
            """
            UPDATE dbo.SourceEvents
            SET EventData = JSON_MODIFY(EventData, '$.overrides.overridesCanonical', @Target)
            WHERE StreamId = @StreamId AND EventType = 'SearchParameterActivated';
            """, connection);
        command.Parameters.Add("@Target", SqlDbType.NVarChar, 128).Value = target;
        command.Parameters.Add("@StreamId", SqlDbType.NVarChar, 256).Value = streamId;
        (await command.ExecuteNonQueryAsync()).ShouldBe(1);
    }

    private static WebApplicationFactory<Program> CreateHost(IgnixaApiFixture template, string connectionString) =>
        template.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenants:Configurations:1:Storage:ConnectionString"] = connectionString,
                    ["Tenants:Configurations:1:Storage:Type"] = "SqlServer",
                    ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
                    ["Conformance:SyncIntervalSeconds"] = "3600"
                })));

    private static async Task ExecuteDatabaseCommandAsync(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXEC sys.sp_executesql @statement", connection);
        command.Parameters.Add("@statement", SqlDbType.NVarChar, -1).Value = statement;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WriteAndAssertAsync(HttpClient client, string marker, List<string> ids)
    {
        var id = $"activation-{ids.Count}-{marker}";
        using var body = new StringContent($$"""
            {"resourceType":"Patient","id":"{{id}}","identifier":[{"system":"{{System}}","value":"{{marker}}"}]}
            """, Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/1/Patient/{id}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        ids.Add(id);
        await AssertPatientsAsync(client, marker, ids);
    }

    private static async Task AssertPatientsAsync(HttpClient client, string marker, IReadOnlyList<string> ids)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"{System}|{marker}")}");
        request.Headers.Add("Prefer", "handling=strict");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var entries = JsonNode.Parse(body)!["entry"]!.AsArray();
        entries.Select(entry => entry!["resource"]!["id"]!.GetValue<string>()).Order().ShouldBe(ids.Order());
    }

    private sealed class SqlTheoryAttribute : TheoryAttribute
    {
        public SqlTheoryAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires SQL-backed package activation and persisted storage identities.";
            }
        }
    }
}
