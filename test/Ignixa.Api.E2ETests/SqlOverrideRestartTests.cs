using System.Data;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.SqlServer.Indexing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Api.E2ETests;

public class SqlOverrideRestartTests
{
    private const string OriginalUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string OverrideUrl = "http://example.org/SearchParameter/startup-identifier";
    private const string IdentifierSystem = "urn:sql-startup";

    [SqlFact]
    public async Task GivenPersistedOverride_WhenTheWebHostRestarts_ThenItsFirstQueryAndNewWritesUseTheOriginalIdentity()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var database = $"IgnixaSqlOverrideRestart_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = database }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quotedDatabase = names.QuoteIdentifier(database);
        await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE {quotedDatabase}");
        try
        {
            await AssertRestartAsync(connectionString);
        }
        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteDatabaseCommandAsync(master, $"DROP DATABASE {quotedDatabase}");
        }
    }

    private static async Task AssertRestartAsync(string connectionString)
    {
        await using var template = new IgnixaApiFixture();
        var identifier = Guid.NewGuid().ToString("N");
        var beforeId = $"startup-before-{identifier}";
        var afterId = $"startup-after-{identifier}";
        short originalId;
        ConformanceState initialState;
        SqlServerSearchIndexReferenceDataCache initialCache;
        await using (var initialHost = CreateHost(template, connectionString))
        {
            using var initialClient = initialHost.CreateClient();
            await PutPatientAsync(initialClient, beforeId, identifier);
            await AssertPatientsAsync(initialClient, identifier, beforeId);
            initialState = initialHost.Services.GetRequiredService<ConformanceState>();
            initialState.IsInitialized.ShouldBeTrue();

            initialCache = await initialHost.Services.GetRequiredService<SqlServerSearchIndexCacheRegistry>()
                .GetOrCreateAsync(1, CancellationToken.None);
            originalId = await initialCache.GetSearchParamIdAsync(OriginalUrl, CancellationToken.None)
                ?? throw new InvalidOperationException("The core identifier search parameter was not seeded.");
            await initialCache.SyncSearchParametersToDatabaseAsync([OverrideUrl], null, CancellationToken.None);
            short physicalOverrideId = await initialCache.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None)
                ?? throw new InvalidOperationException("The overriding canonical was not inserted.");
            physicalOverrideId.ShouldNotBe(originalId);

            var eventStore = initialHost.Services.GetRequiredService<ISourceEventStore>();
            await eventStore.AppendAsync(
            [
                new NewSourceEvent("startup-override", nameof(SearchParameterActivated),
                    new SearchParameterActivated(OverrideUrl, "identifier", "Patient", "Patient.identifier",
                        SearchParamType.Token, "startup.package@1.0", new OverrideInfo(OriginalUrl, originalId),
                        originalId, null, null, null, null))
            ], CancellationToken.None);
        }

        // Persisted events, not manually applied in-memory state, must initialize the next real Program.
        await using var restartedHost = CreateHost(template, connectionString);
        using var client = restartedHost.CreateClient();

        await AssertPatientsAsync(client, identifier, beforeId);
        var restartedState = restartedHost.Services.GetRequiredService<ConformanceState>();
        ReferenceEquals(restartedState, initialState).ShouldBeFalse();
        restartedState.IsInitialized.ShouldBeTrue();
        restartedState.FindByCanonical(OverrideUrl).ShouldNotBeNull();
        var definitions = restartedHost.Services.GetRequiredService<IFhirVersionContext>()
            .GetSearchParameterDefinitionManager(FhirVersion.R4, 1);
        definitions.GetSearchParameter("Patient", "identifier").Url.ShouldBe(new Uri(OverrideUrl));
        var restartedCache = await restartedHost.Services.GetRequiredService<SqlServerSearchIndexCacheRegistry>()
            .GetOrCreateAsync(1, CancellationToken.None);
        ReferenceEquals(restartedCache, initialCache).ShouldBeFalse();
        (await restartedCache.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None)).ShouldBe(originalId);

        await PutPatientAsync(client, afterId, identifier);
        await AssertPatientsAsync(client, identifier, beforeId, afterId);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(
            """
            SELECT COUNT(*), COUNT(DISTINCT SearchParamId), MIN(SearchParamId)
            FROM dbo.TokenSearchParam WHERE Code = @Code;
            """, connection);
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 256).Value = identifier;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(2);
        reader.GetInt32(1).ShouldBe(1);
        reader.GetInt16(2).ShouldBe(originalId);
    }

    private static WebApplicationFactory<Program> CreateHost(IgnixaApiFixture template, string connectionString) =>
        template.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenants:Configurations:1:Storage:ConnectionString"] = connectionString,
                    ["Tenants:Configurations:1:Storage:Type"] = "SqlServer",
                    ["Tenants:Configurations:0:Storage:Type"] = "SqlServer"
                })));

    private static async Task ExecuteDatabaseCommandAsync(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXEC sys.sp_executesql @statement", connection);
        command.Parameters.Add("@statement", SqlDbType.NVarChar, -1).Value = statement;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PutPatientAsync(HttpClient client, string id, string identifier)
    {
        using var content = new StringContent($$"""
            {"resourceType":"Patient","id":"{{id}}",
             "identifier":[{"system":"{{IdentifierSystem}}","value":"{{identifier}}"}]}
            """, Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/1/Patient/{id}", content);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        JsonNode.Parse(body)!["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
    }

    private static async Task AssertPatientsAsync(HttpClient client, string identifier, params string[] ids)
    {
        using var response = await client.GetAsync(
            $"/tenant/1/Patient?identifier={Uri.EscapeDataString($"{IdentifierSystem}|{identifier}")}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var entries = JsonNode.Parse(body)!["entry"]!.AsArray();
        entries.Select(entry => entry!["resource"]!["id"]!.GetValue<string>()).Order()
            .ShouldBe(ids.Order());
    }

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires SQL Server startup and persisted reference identities.";
            }
        }
    }
}
