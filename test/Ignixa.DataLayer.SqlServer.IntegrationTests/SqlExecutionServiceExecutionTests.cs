using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlExecutionServiceExecutionTests
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
            => new((TenantConfiguration?)null);
    }

    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new SkipException(
                "TEST_SQL_CONNECTION_STRING is not set (see docker-compose.test.yml) -- skipping, not failing.");
        }

        return connectionString;
    }

    private static SqlExecutionService CreateService()
        => new(new SingleTenantStore(GetConnectionString()), NullLogger<SqlExecutionService>.Instance);

    [SkippableFact]
    public async Task GivenASimpleSelectQuery_WhenExecutedViaExecuteReaderAsync_ThenReturnsTheExpectedRow()
    {
        // Arrange
        var service = CreateService();
        await using var command = new SqlCommand("SELECT 1 AS Value, 'hello' AS Text");

        // Act
        var results = await service.ExecuteReaderAsync(
            tenantId: 1,
            command,
            reader => (Value: reader.GetInt32(0), Text: reader.GetString(1)),
            CancellationToken.None);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.ShouldBe(1);
        results[0].Text.ShouldBe("hello");
    }

    [SkippableFact]
    public async Task GivenAQueryWithNoRows_WhenExecutedViaExecuteReaderAsync_ThenReturnsAnEmptyList()
    {
        // Arrange
        var service = CreateService();
        await using var command = new SqlCommand("SELECT 1 AS Value WHERE 1 = 0");

        // Act
        var results = await service.ExecuteReaderAsync(
            tenantId: 1,
            command,
            reader => reader.GetInt32(0),
            CancellationToken.None);

        // Assert
        results.ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task GivenAParameterizedCreateAndInsert_WhenExecutedViaExecuteNonQueryAsync_ThenAffectsOneRowAndIsQueryable()
    {
        // Arrange
        var service = CreateService();

        // A real (permanent) table, not a temp table -- verified empirically against this repo's
        // pinned Microsoft.Data.SqlClient version: connection pooling calls sp_reset_connection on
        // every logical reuse, which drops BOTH local (#) and global (##) temp tables created by
        // that connection even when the underlying physical connection/SPID is reused (confirmed
        // with a standalone repro: create on connection A, close/return to pool, reopen connection
        // B from the same pool -- same SPID, "Invalid object name" on the global temp table
        // regardless). If Microsoft.Data.SqlClient is upgraded, this assumption should be
        // re-validated. Since ExecuteNonQueryAsync/ExecuteReaderAsync each open and close their own
        // pooled connection per call (see OpenConnectionAsync), a temp table of either kind cannot
        // survive from the CREATE call to the INSERT/SELECT calls here. A GUID-suffixed permanent
        // table sidesteps that entirely and is dropped explicitly below.
        var tableName = $"ExecTest_{Guid.NewGuid():N}";

        // CA2100 suppressed: tableName is a locally generated Guid-based identifier, never user
        // input -- SQL Server does not support parameterizing table names, so interpolation is the
        // only option for this dynamically-named-table pattern.
#pragma warning disable CA2100
        await using var create = new SqlCommand($"CREATE TABLE {tableName} (Id INT NOT NULL, Name NVARCHAR(50) NOT NULL)");
        await using var insert = new SqlCommand($"INSERT INTO {tableName} (Id, Name) VALUES (@id, @name)");
#pragma warning restore CA2100
        insert.Parameters.AddWithValue("@id", 42);
        insert.Parameters.AddWithValue("@name", "test-row");

        // Act
        await service.ExecuteNonQueryAsync(tenantId: 1, create, CancellationToken.None);
        var affected = await service.ExecuteNonQueryAsync(tenantId: 1, insert, CancellationToken.None);

#pragma warning disable CA2100
        await using var select = new SqlCommand($"SELECT Id, Name FROM {tableName}");
#pragma warning restore CA2100
        var rows = await service.ExecuteReaderAsync(
            tenantId: 1,
            select,
            reader => (Id: reader.GetInt32(0), Name: reader.GetString(1)),
            CancellationToken.None);

        // Assert
        affected.ShouldBe(1);
        rows.Count.ShouldBe(1);
        rows[0].Id.ShouldBe(42);
        rows[0].Name.ShouldBe("test-row");

        // Cleanup -- this permanent table would otherwise persist in the test database; drop
        // explicitly so a failed run doesn't leak state into the next.
#pragma warning disable CA2100
        await using var drop = new SqlCommand($"IF OBJECT_ID('dbo.{tableName}') IS NOT NULL DROP TABLE {tableName}");
#pragma warning restore CA2100
        await service.ExecuteNonQueryAsync(tenantId: 1, drop, CancellationToken.None);
    }

    [Fact]
    public async Task GivenATenantConfiguredForFileSystemStorage_WhenExecutingAQuery_ThenNonTransientExceptionsPropagateImmediately()
    {
        // Arrange -- confirms the OpenConnectionAsync validation guard still fires correctly once
        // wrapped in the retry pipeline (a non-transient InvalidOperationException is not a
        // SqlException, so it is never eligible for retry and propagates on the first attempt).
        var fileSystemTenant = new TenantConfiguration
        {
            TenantId = 2,
            DisplayName = "FileSystem Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" },
        };
        var store = new FakeStoreWithOneTenant(fileSystemTenant);
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);
        await using var command = new SqlCommand("SELECT 1");

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.ExecuteReaderAsync(2, command, reader => reader.GetInt32(0), CancellationToken.None));
    }

    private sealed class FakeStoreWithOneTenant : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant;
        public FakeStoreWithOneTenant(TenantConfiguration tenant) => _tenant = tenant;
        public TenantMode Mode => TenantMode.Isolated;
        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == _tenant.TenantId ? _tenant : null);
        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }
}
