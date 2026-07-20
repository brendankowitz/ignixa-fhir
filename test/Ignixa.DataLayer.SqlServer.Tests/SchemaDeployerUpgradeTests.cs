using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SchemaDeployerUpgradeTests
{
    private sealed class FakeTenantConfigurationStore : ITenantConfigurationStore
    {
        public Dictionary<int, TenantConfiguration> Tenants { get; } = new();

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(Tenants.TryGetValue(tenantId, out var config) ? config : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)Tenants.Values.ToList());
    }

    // IHostEnvironment.EnvironmentName is settable but the concrete HostingEnvironment
    // implementation lives in Microsoft.Extensions.Hosting.Internal (the Microsoft.Extensions.Hosting
    // package, not .Abstractions) and is documented as "not intended to be used directly from your
    // code". A minimal local fake avoids pulling in that extra package just for tests.
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlServer.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task GivenANonexistentTenant_WhenUpgradeIfNeededAsyncCalled_ThenThrowsWithTenantMessage()
    {
        var store = new FakeTenantConfigurationStore(); // no tenant 999
        var deployer = new SchemaDeployer(
            store,
            new FakeHostEnvironment { EnvironmentName = "Production" },
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            new ThrowingSchemaVersionResolver(), // never reached -- ResolveConnectionStringAsync throws first
            NullLogger<SchemaDeployer>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => deployer.UpgradeIfNeededAsync(999, CancellationToken.None));

        ex.Message.ShouldBe("Tenant 999 does not exist or is inactive.");
    }

    private sealed class ThrowingSchemaVersionResolver : ISchemaVersionResolver
    {
        public Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Not expected to be called in this test.");
    }
}
