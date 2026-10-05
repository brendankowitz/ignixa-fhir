// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;

namespace Ignixa.DataLayer.SqlServer.Tests;

/// <summary>
/// Pins the Production credential guard to the factory that is supposed to run it.
/// <para>
/// <see cref="ManagedIdentityConnectionStringValidator.Validate"/> has exactly one production call site, and
/// it is inside <see cref="SqlServerTenantServiceFactory"/>. Its own unit tests exercise the validator in
/// isolation and pass whether or not anything calls it -- which is how the guard previously ran for nobody
/// without a single test noticing. These tests fail if the factory stops invoking it, or starts invoking it
/// after the database has already been touched.
/// </para>
/// </summary>
public sealed class SqlServerTenantServiceFactoryValidationTests : IDisposable
{
    private readonly SqlServerSearchIndexCacheRegistry _cacheRegistry =
        new(Substitute.For<ISqlExecutionService>(), NullLoggerFactory.Instance);

    private const int TenantId = 1;
    private const string PasswordConnectionString =
        "Server=tcp:server.database.windows.net,1433;Database=Fhir;User ID=sa;Password=Secret123;";
    private const string SchemaDeploymentReachedMarker = "schema-deployment-was-reached";

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

    public void Dispose() => _cacheRegistry.Dispose();

    private (SqlServerTenantServiceFactory Factory, ISchemaDeployer SchemaDeployer) CreateFactory(
        string environmentName)
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[TenantId] = new TenantConfiguration
        {
            TenantId = TenantId,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration
            {
                Type = "SqlServer",
                ConnectionString = PasswordConnectionString,
            },
        };

        var schemaDeployer = Substitute.For<ISchemaDeployer>();
        schemaDeployer.DeployIfEmptyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new NotSupportedException(SchemaDeploymentReachedMarker)));

        var initializer = new SqlServerTenantInitializer(
            schemaDeployer, _cacheRegistry, NullLogger<SqlServerTenantInitializer>.Instance);

        var factory = new SqlServerTenantServiceFactory(
            store,
            NullLoggerFactory.Instance,
            new RecyclableMemoryStreamManager(),
            initializer,
            new ManagedIdentityConnectionStringValidator(
                environmentName, NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            Substitute.For<ISqlExecutionService>());

        return (factory, schemaDeployer);
    }

    [Fact]
    public async Task GivenProductionAndAPasswordConnectionString_WhenGettingARepository_ThenTheCredentialGuardRejectsItBeforeTouchingTheDatabase()
    {
        // Arrange
        var (factory, schemaDeployer) = CreateFactory("Production");

        // Act
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => factory.GetRepositoryAsync(TenantId, CancellationToken.None));

        // Assert
        ex.Message.ShouldContain("Managed Identity");
        await schemaDeployer.DidNotReceive().DeployIfEmptyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenProductionAndAPasswordConnectionString_WhenGettingASearchService_ThenTheCredentialGuardRejectsItBeforeTouchingTheDatabase()
    {
        // Arrange
        var (factory, schemaDeployer) = CreateFactory("Production");

        // Act
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => factory.GetSearchServiceAsync(TenantId, CancellationToken.None));

        // Assert
        ex.Message.ShouldContain("Managed Identity");
        await schemaDeployer.DidNotReceive().DeployIfEmptyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The negative control for the two tests above: without it they would still pass if the factory threw
    /// for some unrelated reason before ever reaching the validator.
    /// </summary>
    [Fact]
    public async Task GivenANonProductionEnvironment_WhenGettingARepository_ThenTheSameConnectionStringPassesTheGuardAndInitializationProceeds()
    {
        // Arrange
        var (factory, schemaDeployer) = CreateFactory("Development");

        // Act
        var ex = await Should.ThrowAsync<NotSupportedException>(
            () => factory.GetRepositoryAsync(TenantId, CancellationToken.None));

        // Assert
        ex.Message.ShouldBe(SchemaDeploymentReachedMarker);
        await schemaDeployer.Received().DeployIfEmptyAsync(TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAFailedInitialization_WhenTheTenantIsRequestedAgain_ThenTheFailureIsNotCached()
    {
        // Arrange
        var (factory, _) = CreateFactory("Development");

        // Act
        var first = await Should.ThrowAsync<NotSupportedException>(
            () => factory.GetRepositoryAsync(TenantId, CancellationToken.None));
        var second = await Should.ThrowAsync<NotSupportedException>(
            () => factory.GetRepositoryAsync(TenantId, CancellationToken.None));

        // Assert
        first.Message.ShouldBe(SchemaDeploymentReachedMarker);
        second.Message.ShouldBe(SchemaDeploymentReachedMarker);
        factory.InitializedTenantCount.ShouldBe(0);
    }
}
