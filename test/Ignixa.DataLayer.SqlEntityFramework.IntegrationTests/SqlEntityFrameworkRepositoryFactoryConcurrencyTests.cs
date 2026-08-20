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
/// Records how many times <see cref="DeployIfEmptyAsync"/> ran and, crucially, the high-water mark
/// of how many ran <em>at the same time</em>. It always throws, so tests can observe
/// <c>SqlEntityFrameworkRepositoryFactory.CreateServiceFactory</c> without that method running to
/// completion (which needs a live SQL Server for <c>MultiTenantSearchIndexCache</c>'s
/// cache-initialization query).
/// </summary>
internal sealed class FailingSchemaDeployer : ISchemaDeployer
{
    private readonly TimeSpan _holdOpen;
    private int _deployIfEmptyCallCount;
    private int _inFlight;
    private int _maxInFlight;

    public FailingSchemaDeployer(TimeSpan holdOpen = default) => _holdOpen = holdOpen;

    public int DeployIfEmptyCallCount => Volatile.Read(ref _deployIfEmptyCallCount);

    /// <summary>The most invocations ever overlapping. 1 means no two ever ran concurrently.</summary>
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deployIfEmptyCallCount);
        var inFlight = Interlocked.Increment(ref _inFlight);

        int observedMax;
        while (inFlight > (observedMax = Volatile.Read(ref _maxInFlight)))
        {
            Interlocked.CompareExchange(ref _maxInFlight, inFlight, observedMax);
        }

        // Hold the factory body open so that a second caller racing into it is actually observable.
        // Without this the body returns too fast for an overlap to be caught, and the test would
        // pass whether or not the Lazy<T> serialization works. Lengthening this can only make a
        // genuine overlap MORE likely to be detected, never less -- so the assertion cannot pass
        // spuriously because of timing.
        if (_holdOpen > TimeSpan.Zero)
        {
            Thread.Sleep(_holdOpen);
        }

        Interlocked.Decrement(ref _inFlight);
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
// cache initialization further down -- the invariant under test is observable purely via the
// deployer's own bookkeeping.
public class SqlEntityFrameworkRepositoryFactoryConcurrencyTests
{
    private static (SqlEntityFrameworkRepositoryFactory Factory, FailingSchemaDeployer SchemaDeployer) CreateFactory(
        TimeSpan holdOpen = default,
        TimeSpan? failureCooldown = null)
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

        var schemaDeployer = new FailingSchemaDeployer(holdOpen);
        var factory = new SqlEntityFrameworkRepositoryFactory(
            new SingleTenantConfigurationStore(tenantConfig),
            NullLoggerFactory.Instance,
            new RecyclableMemoryStreamManager(),
            new MultiTenantSearchIndexCache(NullLoggerFactory.Instance),
            schemaDeployer,
            failureCooldown: failureCooldown ?? TimeSpan.Zero);

        return (factory, schemaDeployer);
    }

    // Proves the actual concurrency fix. Without Lazy<T>'s ExecutionAndPublication mode,
    // ConcurrentDictionary.GetOrAdd's value-factory can run more than once CONCURRENTLY for the
    // same not-yet-cached key -- which would mean DeployIfEmptyAsync's non-idempotent
    // CREATE DATABASE could be in flight twice at once for one tenant.
    //
    // Note what is deliberately NOT asserted here: "ran exactly once". MaterializeOrEvict removes
    // a Lazy whose construction failed (proved by the next test), so a caller that reaches
    // GetOrAdd after that eviction legitimately starts a fresh attempt, and the total call count
    // is a race between the callers and the eviction -- it is legitimately >= 1. Asserting == 1
    // made this test fail intermittently while testing nothing the implementation promises. The
    // guarantee Lazy<T> actually provides, and the one that protects the non-idempotent deploy, is
    // that no two constructions for a tenant ever OVERLAP.
    [Fact]
    public async Task GivenConcurrentFirstAccessForTheSameTenant_WhenFactoryConstructionFails_ThenCreateServiceFactoryNeverRunsConcurrently()
    {
        var (factory, schemaDeployer) = CreateFactory(holdOpen: TimeSpan.FromMilliseconds(50));

        var callers = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => factory.GetRepositoryAsync(1)))
            .ToArray();

        await Should.ThrowAsync<InvalidOperationException>(() => Task.WhenAll(callers));

        schemaDeployer.MaxInFlight.ShouldBe(1);
        schemaDeployer.DeployIfEmptyCallCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    // Proves MaterializeOrEvict actually evicts: a Lazy<T> whose construction failed caches and
    // rethrows the identical exception on every subsequent .Value access, so without eviction a
    // transient failure (e.g. a schema deploy hiccup) would permanently poison the tenant --
    // CreateServiceFactory would never be attempted again, even after whatever caused the first
    // failure is resolved. Uses a zero cooldown so eviction is what is under test, not the
    // fail-fast window.
    [Fact]
    public async Task GivenAFactoryConstructionFailure_WhenAccessedAgain_ThenCreateServiceFactoryIsAttemptedAgainRatherThanReplayingTheCachedFailure()
    {
        var (factory, schemaDeployer) = CreateFactory(failureCooldown: TimeSpan.Zero);

        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetRepositoryAsync(1));
        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetRepositoryAsync(1));

        // Two attempts, two real invocations -- if the failed Lazy<T> had not been evicted after
        // the first attempt, the second call would replay the cached exception without
        // CreateServiceFactory (and so DeployIfEmptyAsync) ever running again.
        schemaDeployer.DeployIfEmptyCallCount.ShouldBe(2);
    }

    // Proves the sticky-failure cooldown. The dominant construction failure is NOT transient --
    // UpgradeIfNeededAsync throws for as long as the pending schema diff stays Unsafe or
    // Unclassifiable, which is until an operator runs the CLI. Retrying that on every request means
    // regenerating a full DacFx deploy report each time, so within the cooldown window the recorded
    // failure is replayed and CreateServiceFactory is not re-entered.
    [Fact]
    public async Task GivenARecentConstructionFailure_WhenAccessedAgainWithinTheCooldown_ThenCreateServiceFactoryIsNotRetried()
    {
        var (factory, schemaDeployer) = CreateFactory(failureCooldown: TimeSpan.FromMinutes(1));

        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetRepositoryAsync(1));
        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetRepositoryAsync(1));
        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetRepositoryAsync(1));

        // The caller still sees the same failure -- it is replayed, not swallowed -- but the
        // expensive path behind it ran only once.
        schemaDeployer.DeployIfEmptyCallCount.ShouldBe(1);
    }
}
