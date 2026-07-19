using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SchemaDeployerConnectionTests
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
    public async Task GivenANonexistentTenant_WhenDeployIfEmptyAsyncCalled_ThenThrowsWithTenantMessage()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore(); // no tenant 999
        var deployer = new SchemaDeployer(
            store,
            new FakeHostEnvironment { EnvironmentName = "Production" },
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            NullLogger<SchemaDeployer>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => deployer.DeployIfEmptyAsync(999, CancellationToken.None));

        ex.Message.ShouldBe("Tenant 999 does not exist or is inactive.");
    }

    [Fact]
    public async Task GivenATenantConfiguredForFileSystemStorage_WhenDeployIfEmptyAsyncCalled_ThenThrowsWithStorageTypeMessage()
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
        var deployer = new SchemaDeployer(
            store,
            new FakeHostEnvironment { EnvironmentName = "Production" },
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            NullLogger<SchemaDeployer>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => deployer.DeployIfEmptyAsync(1, CancellationToken.None));

        ex.Message.ShouldContain("FileSystem");
        ex.Message.ShouldContain("SqlServer");
    }
}
