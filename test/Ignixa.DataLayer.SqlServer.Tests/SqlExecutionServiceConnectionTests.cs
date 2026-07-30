using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
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
            => new(Tenants.Values.FirstOrDefault(t => t.Hostnames.Contains(host, StringComparer.OrdinalIgnoreCase)));
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
    public async Task GivenATenantConfiguredForSqlEntityFrameworkStorage_WhenResolvingConnectionString_ThenReturnsItWithoutThrowing()
    {
        // Arrange -- shipped configuration now emits "SqlServer", but "SqlEntityFramework" is the value
        // every previously-deployed tenant config carries, so it must still resolve the same rather than
        // be rejected as a foreign storage type.
        var store = new FakeTenantConfigurationStore();
        const string connectionString = "Server=test;Database=test;Trusted_Connection=True;";
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlEntityFramework", ConnectionString = connectionString },
        };

        // Act
        var resolved = await SqlServerTenantConnectionResolver.ResolveConnectionStringAsync(store, 1, CancellationToken.None);

        // Assert
        resolved.ShouldBe(connectionString);
    }

    [Fact]
    public async Task GivenTheSystemPartitionWithNoConnectionString_WhenResolvingConnectionString_ThenInheritsFromTheConfiguredTenant()
    {
        // Arrange -- Tenant 0 (system partition) has no ConnectionString of its own; it inherits
        // Tenant 1's, matching CLAUDE.md's multi-tenancy rules. SqlEntityFrameworkRepositoryFactory
        // used to carry its own copy of this rule; it now calls the same resolver this asserts on.
        var store = new FakeTenantConfigurationStore();
        const string tenant1ConnectionString = "Server=test;Database=tenant1;Trusted_Connection=True;";
        store.Tenants[0] = new TenantConfiguration
        {
            TenantId = 0,
            DisplayName = "System Partition (Reserved)",
            FhirVersion = "4.0",
            IsSystemPartition = true,
            Storage = new TenantStorageConfiguration { Type = "SqlEntityFramework", InheritConnectionStringFromTenant = 1, ConnectionString = null },
        };
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Tenant 1",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlEntityFramework", ConnectionString = tenant1ConnectionString },
        };

        // Act
        var resolved = await SqlServerTenantConnectionResolver.ResolveConnectionStringAsync(store, 0, CancellationToken.None);

        // Assert
        resolved.ShouldBe(tenant1ConnectionString);
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
}
