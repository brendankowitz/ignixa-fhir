using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SchemaVersionResolverTests
{
    private sealed class SingleTenantStore : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant;

        public SingleTenantStore(string connectionString)
        {
            _tenant = new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "Test Tenant",
                FhirVersion = "4.0",
                Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
            };
        }

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == 1 ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new(_tenant.Hostnames.Contains(host, StringComparer.OrdinalIgnoreCase) ? _tenant : null);
    }

    // IHostEnvironment.EnvironmentName is settable but the concrete HostingEnvironment
    // implementation lives in the Microsoft.Extensions.Hosting package (not .Abstractions), in the
    // Microsoft.Extensions.Hosting.Internal namespace, and is documented as "not intended to be used
    // directly from your code". A minimal local fake avoids pulling in that extra package.
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlServer.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string GetBaseConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING must be set to run this test (see docker-compose.test.yml).");
        }

        return connectionString;
    }

    private static string BuildConnectionStringForDatabase(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(GetBaseConnectionString())
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task CreateEmptyDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        var masterConnectionString = BuildConnectionStringForDatabase("master");
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        var masterConnectionString = BuildConnectionStringForDatabase("master");
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"""
            IF DB_ID('{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class ThrowingSchemaVersionResolver : ISchemaVersionResolver
    {
        public Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Not expected to be called by DeployIfEmptyAsync.");
    }

    private static SchemaDeployer CreateDeployer(string connectionString)
        => new(
            new SingleTenantStore(connectionString),
            new FakeHostEnvironment { EnvironmentName = "Production" },
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            new ThrowingSchemaVersionResolver(),
            NullLogger<SchemaDeployer>.Instance);

    [Fact]
    public async Task GivenATenantWithAStampedVersion_WhenGetCurrentVersionAsyncCalled_ThenReturnsIt()
    {
        // Arrange -- a real, empty, freshly-created database (unique name per test run), deployed
        // via SchemaDeployer so it's stamped with SchemaVersion the same way a real tenant would be.
        var databaseName = $"SchemaVersionResolverTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            var deployer = CreateDeployer(connectionString);
            await deployer.DeployIfEmptyAsync(1, CancellationToken.None);

            var resolver = new SchemaVersionResolver(new SingleTenantStore(connectionString), NullLogger<SchemaVersionResolver>.Instance);

            // Act
            var version = await resolver.GetCurrentVersionAsync(1, CancellationToken.None);

            // Assert
            version.ShouldBe(SchemaVersionConstants.CurrentVersion);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenATenantWithNoSchemaVersionTableAtAll_WhenGetCurrentVersionAsyncCalled_ThenReturnsZero()
    {
        // Arrange -- a real, empty, freshly-created database that has NEVER had any schema deployed
        // to it, so dbo.SchemaVersion does not exist at all. This is the exact shape of an
        // un-versioned pre-Phase-C tenant (deployed before Task 1 introduced the SchemaVersion
        // table): confirmed empirically that the naive "SELECT ISNULL(MAX(Version), 0) FROM
        // dbo.SchemaVersion" throws SqlException "Invalid object name 'dbo.SchemaVersion'" against
        // a database like this one, rather than returning 0 -- GetCurrentVersionAsync must tolerate
        // "table doesn't exist" as equivalent to "version 0".
        var databaseName = $"SchemaVersionResolverTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            var resolver = new SchemaVersionResolver(new SingleTenantStore(connectionString), NullLogger<SchemaVersionResolver>.Instance);

            // Act
            var version = await resolver.GetCurrentVersionAsync(1, CancellationToken.None);

            // Assert
            version.ShouldBe(0);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }
}
