using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

/// <summary>
/// Test fixture providing a real, uniquely-named, freshly-deployed scratch tenant database, backed
/// by a real <see cref="SqlExecutionService"/>. Reused by every SQL-backed integration test in the
/// Phase D write-path plan. Follows the exact fake-<see cref="ITenantConfigurationStore"/> and
/// create/deploy/drop pattern already established in SchemaDeployerUpgradeTests.cs.
/// </summary>
public sealed class TestTenantDatabase
{
    public const int TestTenantId = 1;

    private readonly string _databaseName;

    private TestTenantDatabase(string databaseName, int tenantId, ISqlExecutionService sqlExecutionService)
    {
        _databaseName = databaseName;
        TenantId = tenantId;
        SqlExecutionService = sqlExecutionService;
    }

    public int TenantId { get; }

    public ISqlExecutionService SqlExecutionService { get; }

    public static async Task<TestTenantDatabase> CreateEmptyAsync(CancellationToken cancellationToken = default)
    {
        var databaseName = $"IgnixaDataLayerSqlServerTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);

        await CreateEmptyDatabaseAsync(databaseName, cancellationToken);

        var tenantConfigurationStore = new SingleTenantStore(connectionString);
        var deployer = new SchemaDeployer(
            tenantConfigurationStore,
            new FakeHostEnvironment(),
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            new SchemaVersionResolver(tenantConfigurationStore, NullLogger<SchemaVersionResolver>.Instance),
            NullLogger<SchemaDeployer>.Instance);

        await deployer.DeployIfEmptyAsync(TestTenantId, cancellationToken);

        // dbo.ResourceType has no seed data of its own: the dacpac's post-deployment script only
        // seeds dbo.ResourceChangeType (see Script.PostDeployment.sql), and real deployments only
        // ever populate ResourceType on-demand via the write path's GetOrCreateResourceTypeIdAsync
        // (Task 6, not built yet). Seed the one row cache tests need for a "known resource type"
        // lookup so this fixture is usable before that on-demand-creation path exists.
        await SeedResourceTypeAsync(connectionString, "Patient", cancellationToken);

        var sqlExecutionService = new SqlExecutionService(tenantConfigurationStore, NullLogger<SqlExecutionService>.Instance);

        return new TestTenantDatabase(databaseName, TestTenantId, sqlExecutionService);
    }

    private static async Task SeedResourceTypeAsync(string connectionString, string resourceTypeName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.ResourceType (Name) VALUES (@Name)";
        command.Parameters.AddWithValue("@Name", resourceTypeName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // CA2100 suppressed: this is a test-only raw-SQL helper -- callers pass literal assertion
    // queries, never untrusted input, matching the same suppression rationale used throughout this
    // fixture and SchemaDeployerUpgradeTests.cs for test-controlled SQL text.
    public async Task<T> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(BuildConnectionStringForDatabase(_databaseName));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    // CA2100 suppressed: this is a test-only raw-SQL helper -- callers pass literal assertion
    // queries, never untrusted input, matching the same suppression rationale used throughout this
    // fixture and SchemaDeployerUpgradeTests.cs for test-controlled SQL text.
    public async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(BuildConnectionStringForDatabase(_databaseName));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DisposeAsync() => await DropDatabaseAsync(_databaseName, CancellationToken.None);

    private sealed class SingleTenantStore : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant;

        public SingleTenantStore(string connectionString)
        {
            _tenant = new TenantConfiguration
            {
                TenantId = TestTenantId,
                DisplayName = "Test Tenant",
                FhirVersion = "4.0",
                Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
            };
        }

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == TestTenantId ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });
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
}
