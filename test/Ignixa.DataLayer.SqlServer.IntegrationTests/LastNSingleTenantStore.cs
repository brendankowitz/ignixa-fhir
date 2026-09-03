using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

internal sealed class LastNSingleTenantStore(string connectionString) : ITenantConfigurationStore
{
    private readonly TenantConfiguration _tenant = new()
    {
        TenantId = 1,
        DisplayName = "Test Tenant",
        FhirVersion = "4.0",
        Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
    };

    public TenantMode Mode => TenantMode.Isolated;

    public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken cancellationToken = default)
        => new(tenantId == 1 ? _tenant : null);

    public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
        => new((IReadOnlyList<TenantConfiguration>)[_tenant]);

    public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
        => new((TenantConfiguration?)null);
}
