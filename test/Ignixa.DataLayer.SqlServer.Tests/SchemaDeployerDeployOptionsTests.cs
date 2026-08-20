using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

/// <summary>
/// Pins the deploy options both automatic paths run under. These are load-bearing safety settings,
/// and the environment predicate in particular is easy to get subtly wrong: an earlier revision
/// keyed on <c>IsDevelopment()</c>, which silently excluded the E2E host (environment "Test") and
/// would have failed every E2E run against the box SQL Server container.
/// </summary>
public class SchemaDeployerDeployOptionsTests
{
    private sealed class EmptyTenantStore : ITenantConfigurationStore
    {
        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new((TenantConfiguration?)null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)[]);

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlServer.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static SchemaDeployer CreateDeployer(string environmentName, bool allowIncompatiblePlatform = false)
        => new(
            new EmptyTenantStore(),
            new FakeHostEnvironment { EnvironmentName = environmentName },
            Options.Create(new SqlServerOptions
            {
                AutomaticSchemaDeploymentEnabled = true,
                AllowIncompatiblePlatform = allowIncompatiblePlatform,
            }),
            new SchemaVersionResolver(new EmptyTenantStore(), NullLogger<SchemaVersionResolver>.Instance),
            NullLogger<SchemaDeployer>.Instance);

    // The dacpac targets Azure SQL Database, so every non-production host -- all of which run a box
    // SQL Server -- must be allowed to deploy across the platform mismatch. "Test" is the E2E host's
    // environment specifically; it is the case the previous IsDevelopment() predicate missed.
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Staging")]
    public void GivenANonProductionEnvironment_WhenBuildingDeployOptions_ThenIncompatiblePlatformIsAllowed(string environmentName)
    {
        CreateDeployer(environmentName).CreateDeployOptions().AllowIncompatiblePlatform.ShouldBeTrue();
    }

    // Production deploys Azure-to-Azure and needs no exemption. If a production target ever were
    // incompatible, it is not a platform this schema is built for and the deploy must fail loudly.
    [Fact]
    public void GivenProduction_WhenBuildingDeployOptions_ThenIncompatiblePlatformIsNotAllowed()
    {
        CreateDeployer("Production").CreateDeployOptions().AllowIncompatiblePlatform.ShouldBeFalse();
    }

    // The internal test-only escape hatch, for suites that deliberately exercise the strict
    // production path while still running against a box SQL Server.
    [Fact]
    public void GivenProductionAndTheInternalOverride_WhenBuildingDeployOptions_ThenIncompatiblePlatformIsAllowed()
    {
        CreateDeployer("Production", allowIncompatiblePlatform: true)
            .CreateDeployOptions().AllowIncompatiblePlatform.ShouldBeTrue();
    }

    // The last backstop behind DeployReportClassifier. It must never be left to DacFx's default,
    // in any environment.
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Production")]
    public void GivenAnyEnvironment_WhenBuildingDeployOptions_ThenDataLossIsBlocked(string environmentName)
    {
        CreateDeployer(environmentName).CreateDeployOptions().BlockOnPossibleDataLoss.ShouldBeTrue();
    }
}
