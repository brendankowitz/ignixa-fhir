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
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
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

    [SqlFact]
    public async Task GivenStagedOverride_WhenTransitionGraceElapses_ThenItIsCommittedAutomatically()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var database = $"IgnixaAutomaticTransition_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = database }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quotedDatabase = names.QuoteIdentifier(database);
        await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE {quotedDatabase}");
        try
        {
            await using var template = new IgnixaApiFixture();
            await using var host = CreateHost(template, connectionString, shortTransitionGrace: true);

            await ActivateAsync(host.Services, "hl7.fhir.r4.core", "automatic-base", BaseUrl, null);
            await ActivateAsync(host.Services, PackageId, "automatic", PackageUrl, BaseUrl);

            var state = host.Services.GetRequiredService<ConformanceState>();
            var completed = await SpinWaitAsync(
                () => state.FindByCanonical(PackageUrl)?.Status == Ignixa.Conformance.Events.Models.SearchParameterStatus.Pending,
                TimeSpan.FromSeconds(10));

            completed.ShouldBeTrue("the durable transition orchestration should commit the staged override");
            state.FindByCanonical(BaseUrl)!.Status.ShouldBe(Ignixa.Conformance.Events.Models.SearchParameterStatus.Disabled);
        }
        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteDatabaseCommandAsync(master, $"DROP DATABASE {quotedDatabase}");
        }
    }

    [SqlFact]
    public async Task GivenInProcessBaseParameter_WhenOverrideIsActivatedAndReindexed_ThenItUsesSharedIdentity()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var database = $"IgnixaInProcessOverride_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = database }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quotedDatabase = names.QuoteIdentifier(database);
        await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE {quotedDatabase}");
        try
        {
            await using var template = new IgnixaApiFixture();
            await using var host = CreateHost(template, connectionString, fastReindex: true);
            using var client = host.CreateClient();
            var cache = await host.Services.GetRequiredService<SqlServerSearchIndexCacheRegistry>()
                .GetOrCreateAsync(1, CancellationToken.None);
            var baseId = await cache.GetSearchParamIdAsync(BaseUrl, CancellationToken.None)
                ?? throw new InvalidOperationException("Missing in-process base identifier ID.");
            var beforeId = $"before-override-{Guid.NewGuid():N}";
            var afterId = $"after-override-{Guid.NewGuid():N}";
            await PutPatientAsync(client, beforeId, "shared-identity");

            await ActivateAsync(host.Services, PackageId, "distinct", PackageUrl, BaseUrl);

            var owner = host.Services.GetRequiredService<ConformanceState>()
                .FindByCanonical(PackageUrl)!;
            owner.Status.ShouldBe(Ignixa.Conformance.Events.Models.SearchParameterStatus.Staged);
            owner.OverridesCanonical.ShouldBe(BaseUrl);

            await PutPatientAsync(client, afterId, "shared-identity");
            RenewLease(host.Services);

            using var strictRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"{System}|not-yet-searchable")}");
            strictRequest.Headers.Add("Prefer", "handling=strict");
            using var strictResponse = await client.SendAsync(strictRequest);
            strictResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await strictResponse.Content.ReadAsStringAsync());

            RenewLease(host.Services);
            using var lenientRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"{System}|not-yet-searchable")}");
            lenientRequest.Headers.Add("Prefer", "handling=lenient");
            using var lenientResponse = await client.SendAsync(lenientRequest);
            var lenientBody = await lenientResponse.Content.ReadAsStringAsync();
            lenientResponse.StatusCode.ShouldBe(HttpStatusCode.OK, lenientBody);
            JsonNode.Parse(lenientBody)!["entry"]!.AsArray()
                .Select(entry => entry!["resource"])
                .ShouldContain(resource =>
                    resource!["resourceType"]!.GetValue<string>() == "OperationOutcome" &&
                    resource["issue"]![0]!["severity"]!.GetValue<string>() == "warning");

            (await SpinWaitAsync(
                () => host.Services.GetRequiredService<ConformanceState>()
                    .FindByCanonical(PackageUrl)?.Status ==
                    Ignixa.Conformance.Events.Models.SearchParameterStatus.Pending,
                TimeSpan.FromSeconds(20))).ShouldBeTrue();
            var definitions = host.Services.GetRequiredService<IFhirVersionContext>()
                .GetSearchParameterDefinitionManager(FhirVersion.R4, 1);
            var resolver = new SqlServerSymbolResolver(cache);
            (await resolver.GetSearchParamIdAsync(
                definitions.GetSearchParameter("Patient", "identifier"),
                CancellationToken.None)).ShouldBe(baseId);
            using var reindexResponse = await client.PostAsync(
                "/tenant/1/$reindex",
                new StringContent("""{"resourceType":"Parameters"}""", Encoding.UTF8, "application/fhir+json"));
            var reindexBody = await reindexResponse.Content.ReadAsStringAsync();
            reindexResponse.StatusCode.ShouldBe(HttpStatusCode.Created, reindexBody);
            var jobId = JsonNode.Parse(reindexBody)!["parameter"]!.AsArray()
                .Single(parameter => parameter!["name"]!.GetValue<string>() == "id")!["valueString"]!
                .GetValue<string>();
            await WaitForReindexAsync(client, jobId);
            // Completion no longer refreshes local consumers synchronously (M9); the sync tick does it.
            await host.Services.GetRequiredService<ConformanceRefresher>()
                .RefreshAsync(force: false, CancellationToken.None);
            RenewLease(host.Services);
            var searchableDefinitions = host.Services.GetRequiredService<IFhirVersionContext>()
                .GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1);
            var searchable = searchableDefinitions.GetSearchParameter("Patient", "identifier");
            searchable.OverridesUrl.ShouldBe(new Uri(BaseUrl));
            (await resolver.GetSearchParamIdAsync(searchable, CancellationToken.None)).ShouldBe(baseId);
            var packageId = await cache.GetSearchParamIdAsync(PackageUrl, CancellationToken.None);
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using var command = new SqlCommand(
                    """
                    SELECT token.SearchParamId, resource.ResourceId
                    FROM dbo.TokenSearchParam token
                    JOIN dbo.System system ON system.SystemId = token.SystemId
                    JOIN dbo.Resource resource
                      ON resource.ResourceTypeId = token.ResourceTypeId
                     AND resource.ResourceSurrogateId = token.ResourceSurrogateId
                    WHERE token.Code = @Code AND system.Value = @System
                      AND resource.IsHistory = 0
                      AND resource.IsDeleted = 0
                    """,
                    connection);
                command.Parameters.Add("@Code", SqlDbType.NVarChar, 256).Value = "shared-identity";
                command.Parameters.Add("@System", SqlDbType.NVarChar, 256).Value = System;
                await using var reader = await command.ExecuteReaderAsync();
                var idsBySearchParamId = new Dictionary<short, List<string>>();
                while (await reader.ReadAsync())
                {
                    var searchParamId = reader.GetInt16(0);
                    if (!idsBySearchParamId.TryGetValue(searchParamId, out var ids))
                    {
                        ids = [];
                        idsBySearchParamId[searchParamId] = ids;
                    }
                    ids.Add(reader.GetString(1));
                }
                idsBySearchParamId[baseId].ShouldContain(beforeId);
                idsBySearchParamId[baseId].ShouldContain(afterId);
                if (packageId is { } physicalPackageId)
                {
                    idsBySearchParamId.ShouldNotContainKey(physicalPackageId);
                }
            }

            RenewLease(host.Services);
            using var enabledRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"{System}|shared-identity")}");
            enabledRequest.Headers.Add("Prefer", "handling=strict");
            using var enabledResponse = await client.SendAsync(enabledRequest);
            var enabledBody = await enabledResponse.Content.ReadAsStringAsync();
            enabledResponse.StatusCode.ShouldBe(HttpStatusCode.OK, enabledBody);
            var enabledIds = JsonNode.Parse(enabledBody)!["entry"]!.AsArray()
                .Select(entry => entry!["resource"]!["id"]!.GetValue<string>())
                .ToArray();
            enabledIds.ShouldContain(beforeId);
            enabledIds.ShouldContain(afterId);
        }

        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteDatabaseCommandAsync(master, $"DROP DATABASE {quotedDatabase}");
        }
    }

    private static async Task PutPatientAsync(
        HttpClient client,
        string patientId,
        string value,
        HttpStatusCode expectedStatus = HttpStatusCode.Created)
    {
        using var body = new StringContent($$"""
            {"resourceType":"Patient","id":"{{patientId}}","identifier":[{"system":"{{System}}","value":"{{value}}"}]}
            """, Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/1/Patient/{patientId}", body);
        response.StatusCode.ShouldBe(expectedStatus, await response.Content.ReadAsStringAsync());
    }

    private static async Task WaitForReindexAsync(HttpClient client, string jobId)
    {
        var expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < expires)
        {
            using var response = await client.GetAsync($"/tenant/1/$reindex/{jobId}");
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            var status = JsonNode.Parse(body)!["parameter"]!.AsArray()
                .Single(parameter => parameter!["name"]!.GetValue<string>() == "status")!["valueString"]!
                .GetValue<string>();
            if (status == "Completed")
            {
                return;
            }
            if (status is "Failed" or "Cancelled")
            {
                throw new InvalidOperationException($"Reindex job {jobId} ended as {status}: {body}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"Reindex job {jobId} did not complete.");
    }

    private static void RenewLease(IServiceProvider services)
    {
        var lease = services.GetRequiredService<ConformanceLease>();
        lease.Renew(lease.CaptureStart());
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
            await AssertRedefiningSearchAsync(client, marker);
            await SearchParameterLifecycleTestHelper.CommitTransitionAsync(host.Services, PackageUrl);
            await SearchParameterLifecycleTestHelper.CompleteReindexAsync(host.Services, PackageUrl);
            await WriteAndAssertAsync(client, marker, ids);
            await ActivateAsync(host.Services, PackageId, "2", PackageUrl, BaseUrl);
            await AssertRedefiningSearchAsync(client, marker);
            await SearchParameterLifecycleTestHelper.CommitTransitionAsync(host.Services, PackageUrl);
            await SearchParameterLifecycleTestHelper.CompleteReindexAsync(host.Services, PackageUrl);
            await WriteAndAssertAsync(client, marker, ids);
            await ActivateAsync(host.Services, "test.override.chain", "1", ChainedUrl, PackageUrl);
            await AssertRedefiningSearchAsync(client, marker);
            await SearchParameterLifecycleTestHelper.CommitTransitionAsync(host.Services, ChainedUrl);
            await SearchParameterLifecycleTestHelper.CompleteReindexAsync(host.Services, ChainedUrl);
            await WriteAndAssertAsync(client, marker, ids);
            await AssertCatalogAndResolutionAsync(
                cache,
                host.Services.GetRequiredService<IFhirVersionContext>()
                    .GetSearchParameterDefinitionManager(FhirVersion.R4, 1),
                rootId);

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
        await AssertPatientAsync(restartedClient, marker, ids[^1]);
        var replayed = restarted.Services.GetRequiredService<ConformanceState>();
        replayed.GetSearchParameter("Patient", "identifier")!.OverridesCanonical.ShouldBe(BaseUrl);
        var freshCache = await restarted.Services.GetRequiredService<SqlServerSearchIndexCacheRegistry>()
            .GetOrCreateAsync(1, CancellationToken.None);
        await AssertCatalogAndResolutionAsync(
            freshCache,
            restarted.Services.GetRequiredService<IFhirVersionContext>()
                .GetSearchParameterDefinitionManager(FhirVersion.R4, 1),
            rootId);
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
            services.GetRequiredService<ILogger<PackageLoadedSearchParameterSyncHandler>>());
        await handler.HandleAsync(new PackageLoadedEvent(packageId, version, 1, DateTimeOffset.UtcNow), CancellationToken.None);
    }

    private static async Task AssertCatalogAndResolutionAsync(
        SqlServerSearchIndexReferenceDataCache cache,
        ISearchParameterDefinitionManager definitions,
        short rootId)
    {
        (await cache.GetSearchParamIdAsync(BaseUrl, CancellationToken.None)).ShouldBe(rootId);
        foreach (var canonical in new[] { PackageUrl, ChainedUrl })
        {
            (await cache.GetSearchParamIdAsync(canonical, CancellationToken.None)).ShouldNotBe(rootId, canonical);
        }

        var resolver = new SqlServerSymbolResolver(cache);
        (await resolver.GetSearchParamIdAsync(
            definitions.GetSearchParameter("Patient", "identifier"),
            CancellationToken.None)).ShouldBe(rootId);
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

    private static WebApplicationFactory<Program> CreateHost(
        IgnixaApiFixture template,
        string connectionString,
        bool shortTransitionGrace = false,
        bool fastReindex = false) =>
        template.WithWebHostBuilder(builder =>
        {
            if (shortTransitionGrace)
            {
                builder.UseSetting("Conformance:MaxStaleness", "00:00:00.050");
                builder.UseSetting("Conformance:TransitionGrace", "00:00:00.100");
                builder.UseSetting("Conformance:TransitionSafetyMargin", "00:00:00.050");
                builder.UseSetting("Reindex:BarrierDelay", "00:00:00.050");
            }
            else if (fastReindex)
            {
                builder.UseSetting("Conformance:MaxStaleness", "00:00:10");
                builder.UseSetting("Conformance:TransitionGrace", "00:00:15");
                builder.UseSetting("Conformance:TransitionSafetyMargin", "00:00:05");
                builder.UseSetting("Reindex:BarrierDelay", "00:00:10");
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenants:Configurations:1:Storage:ConnectionString"] = connectionString,
                    ["Tenants:Configurations:1:Storage:Type"] = "SqlServer",
                    ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
                    ["Conformance:SyncIntervalSeconds"] = "3600"
                }));
        });

    private static async Task<bool> SpinWaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var expires = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < expires)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        return condition();
    }

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
        // Synthetic lifecycle completion is intentional here; this assertion isolates post-enable identity reuse.
        await AssertPatientAsync(client, marker, id);
    }

    private static async Task AssertRedefiningSearchAsync(HttpClient client, string marker)
    {
        using var strictRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"{System}|{marker}")}");
        strictRequest.Headers.Add("Prefer", "handling=strict");
        using var strictResponse = await client.SendAsync(strictRequest);
        strictResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await strictResponse.Content.ReadAsStringAsync());

        using var lenientRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"{System}|{marker}")}");
        lenientRequest.Headers.Add("Prefer", "handling=lenient");
        using var lenientResponse = await client.SendAsync(lenientRequest);
        var lenientBody = await lenientResponse.Content.ReadAsStringAsync();
        lenientResponse.StatusCode.ShouldBe(HttpStatusCode.OK, lenientBody);
        lenientBody.ShouldContain("Search parameter 'identifier' is being redefined and was ignored.");
    }

    private static async Task AssertPatientAsync(HttpClient client, string marker, string id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"{System}|{marker}")}");
        request.Headers.Add("Prefer", "handling=strict");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var entries = JsonNode.Parse(body)!["entry"]!.AsArray();
        entries.Select(entry => entry!["resource"]!["id"]!.GetValue<string>()).ShouldContain(id);
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

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires SQL-backed package activation and persisted storage identities.";
            }
        }
    }
}
