using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SchemaDeployerDeploymentTests
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

    private static async Task<List<string>> GetTableNamesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.tables";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tableNames = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    private sealed class ThrowingSchemaVersionResolver : ISchemaVersionResolver
    {
        public Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Not expected to be called by DeployIfEmptyAsync.");
    }

    private static SchemaDeployer CreateDeployer(string connectionString, bool automaticSchemaDeploymentEnabled)
        => new(
            new SingleTenantStore(connectionString),
            new FakeHostEnvironment { EnvironmentName = "Production" },
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = automaticSchemaDeploymentEnabled }),
            new ThrowingSchemaVersionResolver(),
            NullLogger<SchemaDeployer>.Instance);

    [Fact]
    public async Task GivenAnEmptyDatabase_WhenDeployIfEmptyAsyncCalled_ThenCreatesTheExpectedTables()
    {
        // Arrange -- a real, empty, freshly-created database (unique name per test run).
        var databaseName = $"SchemaDeployerTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            var deployer = CreateDeployer(connectionString, automaticSchemaDeploymentEnabled: true);

            // Act
            await deployer.DeployIfEmptyAsync(1, CancellationToken.None);

            // Assert -- golden-shape assertion, not a loose row-count check.
            var tableNames = await GetTableNamesAsync(connectionString, CancellationToken.None);
            tableNames.ShouldContain("Resource");
            tableNames.ShouldContain("TokenSearchParam");
            tableNames.ShouldContain("ResourceType");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT MAX(Version) FROM dbo.SchemaVersion";
            var stampedVersion = (int)(await versionCommand.ExecuteScalarAsync(CancellationToken.None))!;
            stampedVersion.ShouldBe(SchemaVersionConstants.CurrentVersion);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenANonEmptyDatabase_WhenDeployIfEmptyAsyncCalled_ThenDoesNotAttemptDeploy()
    {
        // Arrange -- a database that already has the Resource table (deploy once, then call again).
        var databaseName = $"SchemaDeployerTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            var deployer = CreateDeployer(connectionString, automaticSchemaDeploymentEnabled: true);
            await deployer.DeployIfEmptyAsync(1, CancellationToken.None);

            var tableNamesBefore = await GetTableNamesAsync(connectionString, CancellationToken.None);
            tableNamesBefore.ShouldContain("Resource");

            // Act & Assert -- the second call returns without throwing (no DacServicesException from
            // upgradeExisting: false), proving the emptiness check short-circuits before ever calling
            // DacServices.Deploy.
            await deployer.DeployIfEmptyAsync(1, CancellationToken.None);

            var tableNamesAfter = await GetTableNamesAsync(connectionString, CancellationToken.None);
            tableNamesAfter.ShouldBe(tableNamesBefore, ignoreOrder: true);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenAnEmptyDatabaseAndTheToggleDisabled_WhenDeployIfEmptyAsyncCalled_ThenThrowsAnActionableError()
    {
        // Arrange -- AutomaticSchemaDeploymentEnabled = false, a real empty database.
        var databaseName = $"SchemaDeployerTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            var deployer = CreateDeployer(connectionString, automaticSchemaDeploymentEnabled: false);

            // Act & Assert -- throws InvalidOperationException mentioning both the config key name
            // and the manual sqlpackage command, not a silent no-op and not a hang.
            var ex = await Should.ThrowAsync<InvalidOperationException>(
                () => deployer.DeployIfEmptyAsync(1, CancellationToken.None));

            ex.Message.ShouldContain(nameof(SqlServerOptions.AutomaticSchemaDeploymentEnabled));
            ex.Message.ShouldContain("sqlpackage");
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }
}
