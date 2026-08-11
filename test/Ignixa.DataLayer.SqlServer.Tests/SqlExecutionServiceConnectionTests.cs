using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SqlExecutionServiceConnectionTests
{
    private sealed class FakeTenantConfigurationStore : ITenantConfigurationStore
    {
        public Dictionary<int, TenantConfiguration> Tenants { get; } = new();

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(Tenants.TryGetValue(tenantId, out var config) ? config : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)Tenants.Values.ToList());

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    [Fact]
    public async Task GivenATenantThatDoesNotExist_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(999, CancellationToken.None));
        ex.Message.ShouldContain("999");
    }

    [Fact]
    public async Task GivenATenantConfiguredForFileSystemStorage_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" },
        };
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(1, CancellationToken.None));
        ex.Message.ShouldContain("FileSystem");
        ex.Message.ShouldContain("SqlServer");
    }

    [Fact]
    public async Task GivenATenantConfiguredForSqlServerWithNoConnectionString_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = null },
        };
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(1, CancellationToken.None));
        ex.Message.ShouldContain("ConnectionString");
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(1205)]
    [InlineData(4060)]
    [InlineData(10928)]
    [InlineData(10929)]
    [InlineData(40197)]
    [InlineData(40501)]
    [InlineData(40613)]
    public void GivenADocumentedTransientSqlErrorNumber_WhenClassifiedByIsTransient_ThenReturnsTrue(int sqlErrorNumber)
    {
        // Act & Assert
        SqlExecutionService.IsTransient(sqlErrorNumber).ShouldBeTrue();
    }

    [Theory]
    [InlineData(547)] // constraint violation
    [InlineData(0)]
    [InlineData(40614)] // just outside the documented Azure SQL throttling range
    [InlineData(4059)] // just outside the documented "cannot open database" range
    public void GivenANonTransientSqlErrorNumber_WhenClassifiedByIsTransient_ThenReturnsFalse(int sqlErrorNumber)
    {
        // Act & Assert
        SqlExecutionService.IsTransient(sqlErrorNumber).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenANullCommand_WhenExecutingReaderAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var service = new SqlExecutionService(new FakeTenantConfigurationStore(), NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.ExecuteReaderAsync<int>(1, null!, reader => reader.GetInt32(0), CancellationToken.None));
    }

    [Fact]
    public async Task GivenANullReadRow_WhenExecutingReaderAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var service = new SqlExecutionService(new FakeTenantConfigurationStore(), NullLogger<SqlExecutionService>.Instance);
        await using var command = new SqlCommand("SELECT 1");

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.ExecuteReaderAsync<int>(1, command, null!, CancellationToken.None));
    }

    [Fact]
    public async Task GivenANullCommand_WhenExecutingNonQueryAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var service = new SqlExecutionService(new FakeTenantConfigurationStore(), NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.ExecuteNonQueryAsync(1, null!, CancellationToken.None));
    }
}
