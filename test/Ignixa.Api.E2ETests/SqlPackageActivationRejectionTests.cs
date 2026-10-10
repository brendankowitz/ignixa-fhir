using System.Data;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Ignixa.Api.E2ETests;

/// <summary>
/// A package whose activation is rejected must fail the load request with the rejection reason; the package
/// stays stored but none of its definitions activate.
/// </summary>
public class SqlPackageActivationRejectionTests
{
    private const string PackageId = "test.mixed-base";
    private const string Version = "1";
    private static readonly TimeSpan HostStartupTimeout = TimeSpan.FromSeconds(90);

    [SqlFact]
    public async Task GivenPackageWhoseParameterShadowsDifferentBaseRoots_WhenLoadedThroughTheApi_ThenItReturns422WithTheReason()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var database = $"IgnixaActivationRejection_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = database }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quoted = names.QuoteIdentifier(database);
        await ExecuteAsync(master, $"CREATE DATABASE {quoted}");
        try
        {
            await AssertRejectedLoadAsync(connectionString);
        }
        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteAsync(master, $"DROP DATABASE {quoted}");
        }
    }

    private static async Task AssertRejectedLoadAsync(string connectionString)
    {
        await using var template = new StoredPackageFixture();
        await using var host = template.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenants:Configurations:1:Storage:ConnectionString"] = connectionString,
                ["Tenants:Configurations:1:Storage:Type"] = "SqlServer",
                ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
                ["Conformance:SyncIntervalSeconds"] = "3600"
            })));
        using var client = host.CreateClient();
        using var startup = new CancellationTokenSource(HostStartupTimeout);
        var initializer = host.Services.GetServices<IHostedService>().OfType<ConformanceStateInitializerService>().Single();
        await (initializer.ExecuteTask ?? throw new InvalidOperationException("The conformance initializer was not started."))
            .WaitAsync(startup.Token);
        await StoreMixedBaseParameterAsync(host.Services);
        var state = host.Services.GetRequiredService<ConformanceState>();
        long positionBefore = state.LastProcessedEventId;
        long eventsBefore = await CountEventsAsync(connectionString);

        using var request = new StringContent(
            $$"""{"packageId":"{{PackageId}}","version":"{{Version}}"}""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync("/tenant/1/admin/packages/load", request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, body);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/fhir+json");
        var outcome = JsonNode.Parse(body)!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        var issue = outcome["issue"]!.AsArray().ShouldHaveSingleItem()!;
        issue["code"]!.GetValue<string>().ShouldBe("business-rule");
        issue["details"]!["coding"]![0]!["code"]!.GetValue<string>().ShouldBe("SP_MIXED_BASE_SHADOW");
        issue["diagnostics"]!.GetValue<string>().ShouldContain("resolves to different storage roots");
        state.Packages.ShouldNotContainKey($"{PackageId}@{Version}");
        state.LastProcessedEventId.ShouldBe(positionBefore);
        (await CountEventsAsync(connectionString)).ShouldBe(eventsBefore);
    }

    // Patient.identifier and Practitioner.identifier are different R4 base canonicals, so one parameter cannot
    // shadow both: the API is the only way a user reaches this rejection.
    private static async Task StoreMixedBaseParameterAsync(IServiceProvider services)
    {
        const string canonical = "http://example.org/SearchParameter/mixed-identifier";
        var parameter = new JsonObject
        {
            ["resourceType"] = "SearchParameter", ["id"] = "mixed-identifier", ["url"] = canonical,
            ["version"] = Version, ["name"] = "MixedIdentifier", ["status"] = "active", ["code"] = "identifier",
            ["base"] = new JsonArray("Patient", "Practitioner"), ["type"] = "token",
            ["expression"] = "Patient.identifier | Practitioner.identifier"
        };
        await services.GetRequiredService<IPackageResourceRepository>().UpsertAsync(new PackageResource
        {
            PackageId = PackageId, PackageVersion = Version, ResourceType = "SearchParameter",
            ResourceId = "mixed-identifier", Canonical = canonical, Version = Version, FhirVersion = "4.0.1",
            ResourceJson = parameter.ToJsonString()
        }, CancellationToken.None);
    }

    private static async Task<long> CountEventsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.SourceEvents", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXEC sys.sp_executesql @statement", connection);
        command.Parameters.Add("@statement", SqlDbType.NVarChar, -1).Value = statement;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Serves the test package from what the test already stored instead of downloading it from NPM.
    /// </summary>
    private sealed class StoredPackageFixture : IgnixaApiFixture
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseServiceProviderFactory(new AutofacServiceProviderFactory(container =>
                container.RegisterDecorator<IImplementationGuideProvider>((_, _, inner) => new StoredPackageProvider(inner))));
            return base.CreateHost(builder);
        }
    }

    private sealed class StoredPackageProvider(IImplementationGuideProvider inner) : IImplementationGuideProvider
    {
        public Task<PackageImportResult> LoadPackageAsync(
            string tenantId,
            string packageId,
            string version,
            CancellationToken cancellationToken) =>
            packageId == PackageId
                ? Task.FromResult(new PackageImportResult
                {
                    PackageId = packageId,
                    PackageVersion = version,
                    TotalResources = 1,
                    UpdatedResources = 1
                })
                : inner.LoadPackageAsync(tenantId, packageId, version, cancellationToken);

        public Task<PackageImportResult> LoadPackageWithDependenciesAsync(
            string tenantId,
            string packageId,
            string version,
            CancellationToken cancellationToken) =>
            inner.LoadPackageWithDependenciesAsync(tenantId, packageId, version, cancellationToken);

        public Task<IReadOnlyList<(string PackageId, string Version)>> ListLoadedPackagesAsync(
            string tenantId,
            CancellationToken cancellationToken) =>
            inner.ListLoadedPackagesAsync(tenantId, cancellationToken);

        public Task<int> UnloadPackageAsync(
            string tenantId,
            string packageId,
            string version,
            CancellationToken cancellationToken) =>
            inner.UnloadPackageAsync(tenantId, packageId, version, cancellationToken);
    }

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires SQL-backed conformance activation.";
            }
        }
    }
}
