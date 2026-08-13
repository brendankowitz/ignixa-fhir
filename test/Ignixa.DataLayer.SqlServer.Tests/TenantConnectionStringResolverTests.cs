using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

/// <summary>
/// Direct coverage for <see cref="TenantConnectionStringResolver"/>. Three of its five outcomes --
/// the entire system-partition inheritance feature and the non-system empty-connection-string
/// guard -- were previously reachable only indirectly (or not at all) through SchemaDeployer.
/// </summary>
public class TenantConnectionStringResolverTests
{
    private const string TenantOneConnectionString = "Server=localhost;Database=IgnixaTenant1;Integrated Security=true;";

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

    private static TenantConfiguration Tenant(
        int tenantId,
        string storageType = "SqlEntityFramework",
        string? connectionString = TenantOneConnectionString,
        bool isSystemPartition = false,
        int inheritFrom = 1)
        => new()
        {
            TenantId = tenantId,
            DisplayName = $"Tenant {tenantId}",
            FhirVersion = "4.0",
            IsSystemPartition = isSystemPartition,
            Storage = new TenantStorageConfiguration
            {
                Type = storageType,
                ConnectionString = connectionString,
                InheritConnectionStringFromTenant = inheritFrom,
            },
        };

    [Fact]
    public async Task GivenASystemPartitionWithNoConnectionString_WhenResolved_ThenInheritsFromTheConfiguredTenant()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[0] = Tenant(0, connectionString: null, isSystemPartition: true, inheritFrom: 1);
        store.Tenants[1] = Tenant(1);

        var result = await TenantConnectionStringResolver.ResolveAsync(store, 0, CancellationToken.None);

        result.ShouldBe(TenantOneConnectionString);
    }

    [Fact]
    public async Task GivenASystemPartitionInheritingFromAMissingTenant_WhenResolved_ThenThrowsNotFound()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[0] = Tenant(0, connectionString: null, isSystemPartition: true, inheritFrom: 7);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => TenantConnectionStringResolver.ResolveAsync(store, 0, CancellationToken.None));

        ex.Message.ShouldContain("not found");
        ex.Message.ShouldContain("Tenant 7");
    }

    // The other arm of the same ternary as the test above -- trivially transposable, and nothing
    // else would notice if they were swapped.
    [Fact]
    public async Task GivenASystemPartitionInheritingFromATenantWithNoConnectionString_WhenResolved_ThenThrowsHasNoConnectionString()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[0] = Tenant(0, connectionString: null, isSystemPartition: true, inheritFrom: 1);
        store.Tenants[1] = Tenant(1, connectionString: null);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => TenantConnectionStringResolver.ResolveAsync(store, 0, CancellationToken.None));

        ex.Message.ShouldContain("has no ConnectionString");
    }

    // Guards the !isSystemPartitionAccess polarity. If that '!' were ever inverted, a regular
    // tenant missing its connection string would silently inherit tenant 1's database -- a
    // cross-tenant data-isolation breach rather than a startup error.
    [Fact]
    public async Task GivenANonSystemTenantWithNoConnectionString_WhenResolved_ThenThrowsRatherThanInheriting()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[2] = Tenant(2, connectionString: null);
        store.Tenants[1] = Tenant(1);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => TenantConnectionStringResolver.ResolveAsync(store, 2, CancellationToken.None));

        ex.Message.ShouldContain("Tenant 2");
        ex.Message.ShouldContain("has no ConnectionString");
        ex.Message.ShouldNotContain("inherit");
    }

    [Fact]
    public async Task GivenASystemPartitionInheritingFromANonSqlServerTenant_WhenResolved_ThenThrowsNamingBothTenants()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[0] = Tenant(0, connectionString: null, isSystemPartition: true, inheritFrom: 1);
        store.Tenants[1] = Tenant(1, storageType: "FileSystem");

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => TenantConnectionStringResolver.ResolveAsync(store, 0, CancellationToken.None));

        ex.Message.ShouldContain("Tenant 0");
        ex.Message.ShouldContain("Tenant 1");
        ex.Message.ShouldContain("FileSystem");
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("SqlEntityFramework")]
    public async Task GivenEitherSqlStorageTypeSynonym_WhenResolved_ThenReturnsTheConnectionString(string storageType)
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, storageType: storageType);

        var result = await TenantConnectionStringResolver.ResolveAsync(store, 1, CancellationToken.None);

        result.ShouldBe(TenantOneConnectionString);
    }

    [Fact]
    public async Task GivenAConnectionStringWithNoDatabaseName_WhenResolved_ThenThrowsBeforeReachingDacFx()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, connectionString: "Server=localhost;Integrated Security=true;");

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => TenantConnectionStringResolver.ResolveAsync(store, 1, CancellationToken.None));

        ex.Message.ShouldContain("no database name");
    }

    // A malformed connection string is the most likely appsettings typo. SqlConnectionStringBuilder
    // throws ArgumentException for it, which would otherwise escape this method with no tenant
    // named -- breaking the "failures are InvalidOperationException naming the tenant" contract
    // every other guard here upholds.
    [Fact]
    public async Task GivenAMalformedConnectionString_WhenResolved_ThenThrowsInvalidOperationNamingTheTenant()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[3] = Tenant(3, connectionString: "this is not a=valid;;;connection=string=======x");

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => TenantConnectionStringResolver.ResolveAsync(store, 3, CancellationToken.None));

        ex.Message.ShouldContain("Tenant 3");
        ex.InnerException.ShouldBeOfType<ArgumentException>();
    }

    // A whitespace-only ConnectionString is a realistic config typo; it must hit the same clear
    // guard as null/empty rather than producing an unusable connection string downstream.
    [Fact]
    public async Task GivenAWhitespaceOnlyConnectionStringOnANonSystemTenant_WhenResolved_ThenThrows()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[2] = Tenant(2, connectionString: "   ");

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => TenantConnectionStringResolver.ResolveAsync(store, 2, CancellationToken.None));

        ex.Message.ShouldContain("has no ConnectionString");
    }
}
