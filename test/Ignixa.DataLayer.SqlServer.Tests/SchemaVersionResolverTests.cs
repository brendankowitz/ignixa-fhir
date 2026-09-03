using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SchemaVersionResolverTests
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
    public async Task GivenANonexistentTenant_WhenGetCurrentVersionAsyncCalled_ThenThrowsWithTenantMessage()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore(); // no tenant 999
        var resolver = new SchemaVersionResolver(store, NullLogger<SchemaVersionResolver>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => resolver.GetCurrentVersionAsync(999, CancellationToken.None));

        // ShouldStartWith, not ShouldBe: the sentence now continues into the configuration-binding hint
        // that makes the system partition's absence diagnosable. The identifying half is still pinned.
        ex.Message.ShouldStartWith("Tenant 999 does not exist or is inactive.");
    }
}
