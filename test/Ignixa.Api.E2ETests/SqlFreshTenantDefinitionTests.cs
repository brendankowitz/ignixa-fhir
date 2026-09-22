using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Api.E2ETests;

public class SqlFreshTenantDefinitionTests(ITestOutputHelper output)
{
    private const string RootUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string OverrideUrl = "http://example.org/SearchParameter/fresh-identifier";
    private const string CustomUrl = "http://example.org/SearchParameter/fresh-marker";
    private const string CustomCode = "fresh-marker";
    private const string IdentifierSystem = "urn:fresh-tenant";
    private const string PackageId = "test.fresh.tenant";
    private const string PackageVersion = "1.0.0";
    private const int PollSeconds = 3600;

    [SqlFact]
    public async Task GivenPersistedPackageDefinitions_WhenASeparateFreshSqlTenantStarts_ThenItsFirstSearchAndWritesUseAllDefinitions()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var conformanceDatabase = $"IgnixaFreshConformance_{Guid.NewGuid():N}";
        var resourceDatabase = $"IgnixaFreshResource_{Guid.NewGuid():N}";
        var conformanceConnection = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = conformanceDatabase
        }.ConnectionString;
        var resourceConnection = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = resourceDatabase
        }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quotedConformance = names.QuoteIdentifier(conformanceDatabase);
        var quotedResource = names.QuoteIdentifier(resourceDatabase);
        await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE {quotedConformance}");
        output.WriteLine($"Owned shared conformance catalog: {conformanceDatabase}");
        try
        {
            await PersistDefinitionsAsync(conformanceConnection);

            // This catalog did not exist during activation: no learned aliases or package reference rows.
            await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE {quotedResource}");
            output.WriteLine($"Owned fresh resource catalog: {resourceDatabase}");
            try
            {
                await AssertFreshTenantAsync(conformanceConnection, resourceConnection);
            }
            finally
            {
                using var pool = new SqlConnection(resourceConnection);
                SqlConnection.ClearPool(pool);
                await ExecuteDatabaseCommandAsync(master, $"DROP DATABASE {quotedResource}");
                output.WriteLine($"Removed owned fresh resource catalog: {resourceDatabase}");
            }
        }
        finally
        {
            using var pool = new SqlConnection(conformanceConnection);
            SqlConnection.ClearPool(pool);
            await ExecuteDatabaseCommandAsync(master, $"DROP DATABASE {quotedConformance}");
            output.WriteLine($"Removed owned shared conformance catalog: {conformanceDatabase}");
        }
    }

    private static async Task PersistDefinitionsAsync(string connectionString)
    {
        await using var template = new IgnixaApiFixture();
        await using var host = CreateHost(template, connectionString);
        using var client = host.CreateClient();
        await StoreParameterAsync(host.Services, "hl7.fhir.r4.core", "identifier", RootUrl, null);
        var pipeline = host.Services.GetRequiredService<PackageActivationPipeline>();
        (await pipeline.ActivateAsync("hl7.fhir.r4.core", PackageVersion, CancellationToken.None)).Success.ShouldBeTrue();
        await StoreParameterAsync(host.Services, PackageId, "identifier", OverrideUrl, RootUrl);
        await StoreParameterAsync(host.Services, PackageId, CustomCode, CustomUrl, null);
        (await pipeline.ActivateAsync(PackageId, PackageVersion, CancellationToken.None)).Success.ShouldBeTrue();

        var persisted = new List<SearchParameterActivated>();
        await foreach (var sourceEvent in host.Services.GetRequiredService<ISourceEventStore>()
            .ReadAllAsync(CancellationToken.None))
        {
            if (sourceEvent.Data is SearchParameterActivated activation && activation.SourcePackage == $"{PackageId}@{PackageVersion}")
            {
                persisted.Add(activation);
            }
        }

        persisted.Count.ShouldBe(2);
        persisted.Single(parameter => parameter.Canonical == OverrideUrl).Overrides!.OverridesCanonical.ShouldBe(RootUrl);
        persisted.Single(parameter => parameter.Canonical == CustomUrl).Overrides.ShouldBeNull();
    }

    private async Task AssertFreshTenantAsync(string conformanceConnection, string resourceConnection)
    {
        await using var template = new IgnixaApiFixture();
        await using var host = CreateHost(template, conformanceConnection, resourceConnection);
        var elapsed = Stopwatch.StartNew();
        using var client = host.CreateClient();
        var marker = Guid.NewGuid().ToString("N");

        // The first tenant request must work before any metadata request, write, explicit sync or poll.
        await AssertPatientsAsync(client, "identifier", marker);
        await AssertPatientsAsync(client, CustomCode, marker);

        var state = host.Services.GetRequiredService<ConformanceState>();
        state.IsInitialized.ShouldBeTrue();
        state.FindByCanonical(OverrideUrl).ShouldNotBeNull();
        state.FindByCanonical(CustomUrl).ShouldNotBeNull();
        var definitions = host.Services.GetRequiredService<IFhirVersionContext>()
            .GetSearchParameterDefinitionManager(FhirVersion.R4, 2);
        definitions.GetSearchParameter("Patient", "identifier").Url.ShouldBe(new Uri(OverrideUrl));
        definitions.GetSearchParameter("Patient", CustomCode).Url.ShouldBe(new Uri(CustomUrl));
        await AssertMetadataAsync(client);
        var identities = await ReadIdentitiesAsync(resourceConnection);
        identities.ShouldContainKey(RootUrl);
        identities.ShouldContainKey(OverrideUrl);
        identities.ShouldContainKey(CustomUrl);
        identities.ShouldContainKey(definitions.GetSearchParameter("Patient", "family").Url!.ToString());
        identities[OverrideUrl].ShouldNotBe(identities[RootUrl]);
        identities[CustomUrl].ShouldNotBe(identities[RootUrl]);

        var firstId = $"fresh-first-{marker}";
        await PutPatientAsync(client, firstId, marker);
        await AssertPatientsAsync(client, "identifier", marker, firstId);
        await AssertPatientsAsync(client, CustomCode, marker, firstId);
        await AssertPhysicalRowsAsync(resourceConnection, marker, identities, expectedPatients: 1);

        var secondId = $"fresh-second-{marker}";
        await PutPatientAsync(client, secondId, marker);
        await AssertPatientsAsync(client, "identifier", marker, firstId, secondId);
        await AssertPatientsAsync(client, CustomCode, marker, firstId, secondId);
        await AssertPhysicalRowsAsync(resourceConnection, marker, identities, expectedPatients: 2);
        await AssertStoreIsolationAsync(conformanceConnection, resourceConnection, marker);
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(PollSeconds),
            "All assertions must finish before the real hosted poller's first interval.");
        output.WriteLine($"First strict searches, metadata and both indexed writes passed in {elapsed.Elapsed.TotalSeconds:F1}s; first poll is at {PollSeconds}s.");
    }

    private static async Task StoreParameterAsync(
        IServiceProvider services, string packageId, string code, string canonical, string? derivedFrom)
    {
        var parameter = new JsonObject
        {
            ["resourceType"] = "SearchParameter",
            ["id"] = code,
            ["url"] = canonical,
            ["version"] = PackageVersion,
            ["name"] = code == "identifier" ? "FreshIdentifier" : "FreshMarker",
            ["status"] = "active",
            ["code"] = code,
            ["base"] = new JsonArray("Patient"),
            ["type"] = "token",
            ["expression"] = "Patient.identifier"
        };
        if (derivedFrom is not null)
        {
            parameter["derivedFrom"] = derivedFrom;
        }
        await services.GetRequiredService<IPackageResourceRepository>().UpsertAsync(new PackageResource
        {
            PackageId = packageId,
            PackageVersion = PackageVersion,
            ResourceType = "SearchParameter",
            ResourceId = code,
            Canonical = canonical,
            Version = PackageVersion,
            FhirVersion = "4.0.1",
            ResourceJson = parameter.ToJsonString()
        }, CancellationToken.None);
    }

    private static WebApplicationFactory<Program> CreateHost(
        IgnixaApiFixture template, string conformanceConnection, string? resourceConnection = null) =>
        template.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
                    ["Tenants:Configurations:0:Storage:InheritConnectionStringFromTenant"] = "1",
                    ["Tenants:Configurations:1:Storage:Type"] = "SqlServer",
                    ["Tenants:Configurations:1:Storage:ConnectionString"] = conformanceConnection,
                    ["Tenants:Configurations:2:TenantId"] = "2",
                    ["Tenants:Configurations:2:DisplayName"] = "Fresh R4 resource tenant",
                    ["Tenants:Configurations:2:FhirVersion"] = "4.0",
                    ["Tenants:Configurations:2:IsActive"] = resourceConnection is null ? "false" : "true",
                    ["Tenants:Configurations:2:IsSystemPartition"] = "false",
                    ["Tenants:Configurations:2:Storage:Type"] = "SqlServer",
                    ["Tenants:Configurations:2:Storage:ConnectionString"] = resourceConnection,
                    ["Tenants:Configurations:2:Storage:InheritConnectionStringFromTenant"] = null,
                    ["Tenants:Configurations:2:Packages:EnableAutoLoad"] = "false",
                    ["Conformance:SyncIntervalSeconds"] = PollSeconds.ToString(CultureInfo.InvariantCulture)
                })));

    private static async Task AssertPatientsAsync(HttpClient client, string code, string marker, params string[] ids)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/tenant/2/Patient?{code}={Uri.EscapeDataString($"{IdentifierSystem}|{marker}")}");
        request.Headers.Add("Prefer", "handling=strict");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var entries = JsonNode.Parse(body)!["entry"]?.AsArray() ?? [];
        entries.Select(entry => entry!["resource"]!["id"]!.GetValue<string>()).Order().ShouldBe(ids.Order());
    }

    private static async Task PutPatientAsync(HttpClient client, string id, string marker)
    {
        using var content = new StringContent($$"""
            {"resourceType":"Patient","id":"{{id}}",
             "identifier":[{"system":"{{IdentifierSystem}}","value":"{{marker}}"}]}
            """, Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/2/Patient/{id}", content);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        JsonNode.Parse(body)!["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
    }

    private static async Task AssertMetadataAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/tenant/2/metadata");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var resources = JsonNode.Parse(body)!["rest"]!.AsArray()[0]!["resource"]!.AsArray();
        var patient = resources.Single(resource => resource!["type"]!.GetValue<string>() == "Patient");
        var parameters = patient!["searchParam"]!.AsArray();
        parameters.Single(parameter => parameter!["name"]!.GetValue<string>() == "identifier")!
            ["definition"]!.GetValue<string>().ShouldBe(OverrideUrl);
        parameters.Single(parameter => parameter!["name"]!.GetValue<string>() == CustomCode)!
            ["definition"]!.GetValue<string>().ShouldBe(CustomUrl);
    }

    private static async Task<Dictionary<string, short>> ReadIdentitiesAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("SELECT Uri, SearchParamId FROM dbo.SearchParam", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var identities = new Dictionary<string, short>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            identities.Add(reader.GetString(0), reader.GetInt16(1));
        }
        return identities;
    }

    private static async Task AssertPhysicalRowsAsync(
        string connectionString, string marker, Dictionary<string, short> identities, int expectedPatients)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(
            "SELECT SearchParamId, COUNT(*) FROM dbo.TokenSearchParam WHERE Code = @Code GROUP BY SearchParamId",
            connection);
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 256).Value = marker;
        await using var reader = await command.ExecuteReaderAsync();
        var counts = new Dictionary<short, int>();
        while (await reader.ReadAsync())
        {
            counts.Add(reader.GetInt16(0), reader.GetInt32(1));
        }
        counts.Count.ShouldBe(2);
        counts[identities[RootUrl]].ShouldBe(expectedPatients);
        counts[identities[CustomUrl]].ShouldBe(expectedPatients);
        counts.ShouldNotContainKey(identities[OverrideUrl]);
    }

    private static async Task AssertStoreIsolationAsync(string conformanceConnection, string resourceConnection, string marker)
    {
        await using var shared = new SqlConnection(conformanceConnection);
        await shared.OpenAsync();
        using var sharedCommand = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE Code = @Code", shared);
        sharedCommand.Parameters.Add("@Code", SqlDbType.NVarChar, 256).Value = marker;
        Convert.ToInt32(await sharedCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture).ShouldBe(0);

        await using var fresh = new SqlConnection(resourceConnection);
        await fresh.OpenAsync();
        using var freshCommand = new SqlCommand("SELECT COUNT(*) FROM dbo.SourceEvents", fresh);
        Convert.ToInt32(await freshCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture).ShouldBe(0,
            "Conformance events must remain in tenant 1's shared store, not tenant 2's resource database.");
    }

    private static async Task ExecuteDatabaseCommandAsync(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXEC sys.sp_executesql @statement", connection);
        command.Parameters.Add("@statement", SqlDbType.NVarChar, -1).Value = statement;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires separate SQL resource and shared conformance stores.";
            }
        }
    }
}
