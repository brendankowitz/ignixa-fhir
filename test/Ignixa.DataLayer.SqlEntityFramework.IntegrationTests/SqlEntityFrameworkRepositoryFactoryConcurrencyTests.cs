// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlServer;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Always throws from <see cref="DeployIfEmptyAsync"/>, counting invocations, so tests can observe
/// how many times <c>SqlEntityFrameworkRepositoryFactory.CreateServiceFactory</c> actually ran
/// without needing that method to run to completion (which requires a live SQL Server for
/// <c>MultiTenantSearchIndexCache</c>'s cache-initialization query).
/// </summary>
internal sealed class FailingSchemaDeployer : ISchemaDeployer
{
    private int _deployIfEmptyCallCount;

    public int DeployIfEmptyCallCount => _deployIfEmptyCallCount;

    public Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deployIfEmptyCallCount);
        throw new InvalidOperationException("simulated schema deploy failure");
    }

    public Task UpgradeIfNeededAsync(int tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SingleTenantConfigurationStore : ITenantConfigurationStore
{
    private readonly TenantConfiguration _configuration;

    public SingleTenantConfigurationStore(TenantConfiguration configuration) => _configuration = configuration;

    public TenantMode Mode => TenantMode.Isolated;

    public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
        => new(tenantId == _configuration.TenantId ? _configuration : null);

    public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
        => new((IReadOnlyList<TenantConfiguration>)[_configuration]);

    public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
        => new((TenantConfiguration?)null);
}

// Covers the Lazy<TenantServiceFactory> + MaterializeOrEvict caching scheme in
// SqlEntityFrameworkRepositoryFactory.GetOrCreateFactoryAsync, added to prevent concurrent
// first-access callers for the same not-yet-provisioned tenant from racing
// CreateServiceFactory's non-idempotent schema-provisioning section. Both tests use a schema
// deployer that always fails, so CreateServiceFactory never reaches the real-SQL-Server-touching
// cache initialization further down -- the invariant under test (how many times, and under what
// conditions, CreateServiceFactory's body actually runs) is observable purely via the deployer's
// call count.
public class SqlEntityFrameworkRepositoryFactoryConcurrencyTests
{
    private static (SqlEntityFrameworkRepositoryFactory Factory, FailingSchemaDeployer SchemaDeployer) CreateFactory()
    {
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            IsActive = true,
            Storage = new TenantStorageConfiguration
            {
                Type = "SqlServer",
                ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;",
            },
        };

        var schemaDeployer = new FailingSchemaDeployer();
        var factory = new SqlEntityFrameworkRepositoryFactory(
            new SingleTenantConfigurationStore(tenantConfig),
            NullLoggerFactory.Instance,
            new RecyclableMemoryStreamManager(),
            new MultiTenantSearchIndexCache(NullLoggerFactory.Instance),
            schemaDeployer);

        return (factory, schemaDeployer);
    }

    // Proves the actual concurrency fix: without Lazy<T>'s ExecutionAndPublication mode,
    // ConcurrentDictionary.GetOrAdd's value-factory can run more than once under concurrent
    // callers for the same not-yet-cached key -- which would mean DeployIfEmptyAsync's
    // non-idempotent CREATE DATABASE could be issued more than once for the same tenant.
    [Fact]
    public async Task GivenConcurrentFirstAccessForTheSameTenant_WhenFactoryConstructionFails_ThenCreateServiceFactoryRunsExactlyOnce()
    {
        var (factory, schemaDeployer) = CreateFactory();

        var callers = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => factory.GetRepositoryAsync(1)))
            .ToArray();

        await Should.ThrowAsync<InvalidOperationException>(() => Task.WhenAll(callers));

        // All 10 concurrent callers must have observed the SAME in-flight Lazy<T> and therefore
        // the SAME single invocation of CreateServiceFactory -- not one each.
        schemaDeployer.DeployIfEmptyCallCount.ShouldBe(1);
    }

    // Proves MaterializeOrEvict actually evicts: a Lazy<T> whose construction failed caches and
    // rethrows the identical exception on every subsequent .Value access, so without eviction a
    // transient failure (e.g. a schema deploy hiccup) would permanently poison the tenant --
    // CreateServiceFactory would never be attempted again, even after whatever caused the first
    // failure is resolved.
    [Fact]
    public async Task GivenAFactoryConstructionFailure_WhenAccessedAgain_ThenCreateServiceFactoryIsAttemptedAgainRatherThanReplayingTheCachedFailure()
    {
        var (factory, schemaDeployer) = CreateFactory();

        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetRepositoryAsync(1));
        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetRepositoryAsync(1));

        // Two attempts, two real invocations -- if the failed Lazy<T> had not been evicted after
        // the first attempt, the second call would replay the cached exception without
        // CreateServiceFactory (and so DeployIfEmptyAsync) ever running again.
        schemaDeployer.DeployIfEmptyCallCount.ShouldBe(2);
    }
}
