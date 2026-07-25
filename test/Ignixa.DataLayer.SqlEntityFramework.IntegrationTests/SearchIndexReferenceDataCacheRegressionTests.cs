// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Pins the defects found reviewing PR #353 in <see cref="SearchIndexReferenceDataCache"/>: batch-lookup
/// response re-keying, change-tracker hygiene on a failed save, cancellation observed before the caches are
/// consulted, the <c>-1</c> sentinel surviving a search-parameter sync, and negative-cache invalidation from
/// the second writer of <c>dbo.System</c>. Runs against the EF Core in-memory provider.
/// </summary>
public sealed class SearchIndexReferenceDataCacheRegressionTests : IDisposable
{
    private const string PatientNameParameterUri = "http://hl7.org/fhir/SearchParameter/Patient-name";

    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;

    public SearchIndexReferenceDataCacheRegressionTests()
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FhirDbContext(options);
        _cache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);

        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = 1, Name = "Patient" });
        _context.SearchParams.Add(new SearchParamEntity { SearchParamId = 1, Uri = PatientNameParameterUri, Status = "Enabled" });
        _context.SaveChanges();
    }

    [Fact]
    public async Task GivenARowMatchedExactly_WhenGetSystemIdsAsync_ThenCreditsTheRequestedSpelling()
    {
        // Arrange
        var seeded = new SystemEntity { Value = "http://loinc.org" };
        _context.Systems.Add(seeded);
        await _context.SaveChangesAsync();

        // Act
        var results = await _cache.GetSystemIdsAsync(["http://loinc.org"]);

        // Assert
        results["http://loinc.org"].ShouldBe(seeded.SystemId);
    }

    [Fact]
    public async Task GivenASpellingDifferingOnlyByCase_WhenGetSystemIdsAsync_ThenItAgreesWithTheScalarLookup()
    {
        // Arrange: which spellings a batch query returns is decided by the column's collation, and this
        // provider compares ordinally. That makes the exact outcome for a case variant a property of the
        // database, not of this code -- so the assertion is agreement between the two lookup paths rather
        // than a fixed answer. Crediting a returned row to a spelling the database did not match would pass
        // under a case-insensitive collation and be a wrong positive under a case-sensitive one, cached
        // ordinally for the life of the process; recording a miss instead is wrong the other way round.
        _context.Systems.Add(new SystemEntity { Value = "http://loinc.org" });
        await _context.SaveChangesAsync();

        using var scalarOnlyCache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);
        var scalar = await scalarOnlyCache.GetSystemIdAsync("http://LOINC.ORG");

        // Act
        var batch = await _cache.GetSystemIdsAsync(["http://loinc.org", "http://LOINC.ORG"]);

        // Assert
        batch["http://LOINC.ORG"].ShouldBe(
            scalar,
            "the batch and scalar paths must answer the same question the same way, under any collation");
    }

    [Fact]
    public async Task GivenABatchLookupUsedACaseVariant_WhenGetSystemIdAsyncCalledAfterwards_ThenItStillAgrees()
    {
        // Arrange: the batch path records misses in a cache the scalar path consults before taking the lock,
        // so a miss the batch invented would outlive it for the whole TTL -- and null lowers to
        // Predicate.False, a definitive "this terminology does not exist".
        _context.Systems.Add(new SystemEntity { Value = "http://loinc.org" });
        await _context.SaveChangesAsync();

        using var referenceCache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);
        var withoutPriorBatch = await referenceCache.GetSystemIdAsync("http://LOINC.ORG");

        await _cache.GetSystemIdsAsync(["http://loinc.org", "http://LOINC.ORG"]);

        // Act
        var afterBatch = await _cache.GetSystemIdAsync("http://LOINC.ORG");

        // Assert
        afterBatch.ShouldBe(
            withoutPriorBatch,
            "a preceding batch lookup must not change the answer the scalar lookup gives");
    }

    [Fact]
    public async Task GivenASaveThatFails_WhenGetOrCreateSystemIdAsync_ThenNoAbandonedInsertSurvives()
    {
        // Arrange: this DbContext lives for the process, so an entity left in Added state outlives the
        // caller that staged it. Cancellation between Add and SaveChangesAsync is the ordinary way in.
        var interceptor = new FailingSaveChangesInterceptor { ShouldFail = true };
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new FhirDbContext(options);
#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000

        // Act
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await cache.GetOrCreateSystemIdAsync("http://abandoned.example/system"));

        // Assert: nothing staged remains, so the next unrelated save cannot carry the insert with it.
        context.ChangeTracker.Entries<SystemEntity>()
            .Any(e => e.State == EntityState.Added)
            .ShouldBeFalse("a failed save must leave the shared change tracker clean");

        interceptor.ShouldFail = false;
        context.QuantityCodes.Add(new QuantityCodeEntity { Value = "unrelated" });
        await context.SaveChangesAsync();

        context.Systems.Any(s => s.Value == "http://abandoned.example/system").ShouldBeFalse(
            "an unrelated save must not commit the insert abandoned by an earlier failure");
    }

    [Fact]
    public async Task GivenAWarmPositiveCacheAndACancelledToken_WhenGetSystemIdAsync_ThenThrows()
    {
        // Arrange
        _context.Systems.Add(new SystemEntity { Value = "http://warm.example/system" });
        await _context.SaveChangesAsync();
        await _cache.GetSystemIdAsync("http://warm.example/system");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert: the lock wait is the only other observation point and a cache hit returns before it.
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetSystemIdAsync("http://warm.example/system", cts.Token));
    }

    [Fact]
    public async Task GivenARecordedMissAndACancelledToken_WhenGetSystemIdAsync_ThenThrowsRatherThanReportingMissing()
    {
        // Arrange
        await _cache.GetSystemIdAsync("http://absent.example/system");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert: this is the load-bearing case. A null return is compiled into Predicate.False, so a
        // cancelled search answered from the negative cache is reported as a definitive "does not exist".
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetSystemIdAsync("http://absent.example/system", cts.Token));
    }

    [Fact]
    public async Task GivenARecordedMissAndACancelledToken_WhenGetQuantityCodeIdAsync_ThenThrowsRatherThanReportingMissing()
    {
        // Arrange
        await _cache.GetQuantityCodeIdAsync("absent-unit");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetQuantityCodeIdAsync("absent-unit", cts.Token));
    }

    [Fact]
    public async Task GivenAWarmPositiveCacheAndACancelledToken_WhenGetQuantityCodeIdAsync_ThenThrows()
    {
        // Arrange
        _context.QuantityCodes.Add(new QuantityCodeEntity { Value = "mg" });
        await _context.SaveChangesAsync();
        await _cache.GetQuantityCodeIdAsync("mg");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetQuantityCodeIdAsync("mg", cts.Token));
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenGetOrCreateSystemIdAsync_ThenThrows()
    {
        // Arrange
        await _cache.GetOrCreateSystemIdAsync("http://warm.example/create");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetOrCreateSystemIdAsync("http://warm.example/create", cts.Token));
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenGetOrCreateQuantityCodeIdAsync_ThenThrows()
    {
        // Arrange
        await _cache.GetOrCreateQuantityCodeIdAsync("warm-unit");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetOrCreateQuantityCodeIdAsync("warm-unit", cts.Token));
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenGetSearchParamIdAsync_ThenThrows()
    {
        // Arrange
        await _cache.GetSearchParamIdAsync(PatientNameParameterUri);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetSearchParamIdAsync(PatientNameParameterUri, cts.Token));
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenGetResourceTypeIdAsync_ThenThrows()
    {
        // Arrange
        await _cache.GetResourceTypeIdAsync("Patient");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetResourceTypeIdAsync("Patient", cts.Token));
    }

    [Fact]
    public async Task GivenAFullyAnsweredBatchAndACancelledToken_WhenGetSystemIdsAsync_ThenThrows()
    {
        // Arrange: every key answerable from cache means the method returns before taking the lock.
        _context.Systems.Add(new SystemEntity { Value = "http://batch.example/system" });
        await _context.SaveChangesAsync();
        await _cache.GetSystemIdAsync("http://batch.example/system");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => _cache.GetSystemIdsAsync(["http://batch.example/system"], cts.Token));
    }

    [Fact]
    public async Task GivenAProbeThatCachedTheNotFoundSentinel_WhenSyncSearchParametersToDatabase_ThenTheRealIdReplacesIt()
    {
        // Arrange: probing before the sync caches -1, which TryAdd would then refuse to overwrite for the
        // process lifetime -- every resource would index with this parameter's rows silently dropped.
        const string url = "http://example.org/SearchParameter/us-core-race";
        (await _cache.GetSearchParamIdAsync(url)).ShouldBeNull();

        _context.SearchParams.Add(new SearchParamEntity { SearchParamId = 42, Uri = url, Status = "Enabled" });
        await _context.SaveChangesAsync();

        // Act
        await _cache.SyncSearchParametersToDatabase([url], null!);

        // Assert
        _cache.TryGetSearchParamIdFromCache(url).ShouldBe(
            (short)42,
            "a synced parameter must replace the cached not-found sentinel");
        (await _cache.GetSearchParamIdAsync(url)).ShouldBe((short)42);
    }

    [Fact]
    public async Task GivenASearchRecordedASystemMissing_WhenSqlSystemRepositoryCreatesIt_ThenTheSearchStopsReportingItMissing()
    {
        // Arrange: CodeSystem import writes through SqlSystemRepository, not through the reference-data
        // cache, so without invalidation the one operation whose purpose is making unknown terminology known
        // leaves searches answering "missing" until the negative entry expires.
        const string systemUri = "http://imported.example/CodeSystem";
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var multiTenantCache = new MultiTenantSearchIndexCache(NullLoggerFactory.Instance);
        var tenantCache = multiTenantCache.GetOrCreateCacheForTenant(1, options);
        (await tenantCache.GetSystemIdAsync(systemUri)).ShouldBeNull();

        await using var importContext = new FhirDbContext(options);
        var repository = new SqlSystemRepository(
            importContext,
            NullLogger<SqlSystemRepository>.Instance,
            multiTenantCache);

        // Act
        var createdId = await repository.GetOrCreateAsync(systemUri, CancellationToken.None);

        // Assert
        (await tenantCache.GetSystemIdAsync(systemUri)).ShouldBe(
            createdId,
            "creating the row must invalidate the recorded miss, whichever writer created it");

        multiTenantCache.InvalidateAllCaches();
    }

    [Fact]
    public async Task GivenAProbeThatMissedAResourceType_WhenTheTypeIsLaterCreated_ThenTheLookupReturnsItsRealId()
    {
        // Arrange: probing before the type is created must NOT cache the -1 sentinel -- dbo.ResourceType is
        // populated as types are first encountered, so caching the miss would permanently poison every later
        // write of that type.
        (await _cache.GetResourceTypeIdAsync("Measure")).ShouldBeNull();

        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = 99, Name = "Measure" });
        await _context.SaveChangesAsync();

        // Act
        var result = await _cache.GetResourceTypeIdAsync("Measure");

        // Assert: would fail under pre-fix behavior, which cached the -1 sentinel on the first miss.
        result.ShouldBe((short)99);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    /// <summary>
    /// Fails <c>SaveChangesAsync</c> the way a cancelled token does, after the caller has staged its entity.
    /// </summary>
    private sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public bool ShouldFail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new OperationCanceledException("Simulated cancellation during SaveChangesAsync.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
