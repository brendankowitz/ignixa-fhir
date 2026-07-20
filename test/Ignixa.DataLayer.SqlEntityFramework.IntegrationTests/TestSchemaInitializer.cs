// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlServer;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Deploys the SSDT-built schema to a test database via <see cref="SchemaDeployer"/> -- the same
/// mechanism production code uses (see <c>SqlEntityFrameworkRepositoryFactory.cs</c>) -- for tests
/// that need a real, fully-initialized schema against a live SQL Server. Replaces the retired
/// <c>DatabaseInitializer</c>/97.sql bootstrap path this project's integration tests used to call
/// directly. Mirrors the fakes <c>Ignixa.DataLayer.SqlServer.IntegrationTests.SchemaDeployerDeploymentTests</c>
/// already established and verified.
/// </summary>
internal static class TestSchemaInitializer
{
    private sealed class SingleTenantStore(string connectionString) : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = new()
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
        };

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == 1 ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });
    }

    // IHostEnvironment.EnvironmentName is settable but the concrete HostingEnvironment implementation
    // lives in the Microsoft.Extensions.Hosting package (not .Abstractions), in the
    // Microsoft.Extensions.Hosting.Internal namespace, and is documented as "not intended to be used
    // directly from your code". A minimal local fake avoids pulling in that extra package.
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlEntityFramework.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Deploys the schema to the database at <paramref name="connectionString"/> if -- and only if --
    /// it's currently empty. Runs in "Development" mode so an as-yet-nonexistent database is created
    /// first, matching this project's existing manual-integration-test convention.
    /// </summary>
    public static Task InitializeAsync(string connectionString, CancellationToken cancellationToken)
    {
        var store = new SingleTenantStore(connectionString);
        var deployer = new SchemaDeployer(
            store,
            new FakeHostEnvironment(),
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            new SchemaVersionResolver(store, NullLogger<SchemaVersionResolver>.Instance),
            NullLogger<SchemaDeployer>.Instance);

        return deployer.DeployIfEmptyAsync(tenantId: 1, cancellationToken);
    }
}
