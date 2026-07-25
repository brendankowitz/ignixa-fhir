// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Covers <see cref="SearchIndexReferenceDataCache"/> lazy-loading, read-only lookup, negative-cache and
/// cancellation behaviour against the EF Core in-memory provider, so none of it needs a live SQL Server.
/// </summary>
public sealed class SearchIndexReferenceDataCacheTests : IDisposable
{
    private const string PatientNameParameterUri = "http://hl7.org/fhir/SearchParameter/Patient-name";

    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;

    public SearchIndexReferenceDataCacheTests()
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FhirDbContext(options);
        _cache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);

        SeedReferenceData();
    }

    [Fact]
    public void GivenEmptyCache_WhenAccessingSystemMappings_ThenLazyLoadsFromDatabase()
    {
        // Arrange
        const string systemUri = "http://loinc.org";
        _context.Systems.Add(new SystemEntity { Value = systemUri });
        _context.SaveChanges();

        // Act
        var result = _cache.SystemMappings.TryGetValue(systemUri, out var systemId);

        // Assert
        result.ShouldBeTrue("lazy-loading should populate cache on miss");
        systemId.ShouldBeGreaterThan(0, "loaded ID should be valid");
    }

    [Fact]
    public void GivenEmptyCache_WhenAccessingQuantityCodeMappings_ThenLazyLoadsFromDatabase()
    {
        // Arrange
        const string code = "mg";
        _context.QuantityCodes.Add(new QuantityCodeEntity { Value = code });
        _context.SaveChanges();

        // Act
        var result = _cache.QuantityCodeMappings.TryGetValue(code, out var codeId);

        // Assert
        result.ShouldBeTrue("lazy-loading should populate cache on miss");
        codeId.ShouldBeGreaterThan(0, "loaded ID should be valid");
    }

    [Fact]
    public void GivenEmptyCache_WhenAccessingResourceTypeMappings_ThenLazyLoadsFromDatabase()
    {
        // Act
        var result = _cache.ResourceTypeMappings.TryGetValue("Patient", out var resourceTypeId);

        // Assert
        result.ShouldBeTrue("lazy-loading should populate cache on miss");
        resourceTypeId.ShouldBeGreaterThan((short)0, "loaded ID should be valid");
        resourceTypeId.ShouldBe((short)1, "Patient should have ResourceTypeId 1");
    }

    [Fact]
    public void GivenEmptyCache_WhenAccessingSearchParameterMappings_ThenLazyLoadsFromDatabase()
    {
        // Act
        var result = _cache.SearchParameterMappings.TryGetValue(PatientNameParameterUri, out var searchParamId);

        // Assert
        result.ShouldBeTrue("lazy-loading should populate cache on miss");
        searchParamId.ShouldBeGreaterThan((short)0, "loaded ID should be valid");
        searchParamId.ShouldBe((short)1, "Patient-name should have SearchParamId 1");
    }

    [Fact]
    public void GivenNotFoundEntry_WhenAccessingResourceTypeMappings_ThenReturnsFalse()
    {
        // Act
        var result = _cache.ResourceTypeMappings.TryGetValue("NonExistentType", out var resourceTypeId);

        // Assert
        result.ShouldBeFalse("non-existent entries should return false");
        resourceTypeId.ShouldBe((short)0, "default value should be returned");
    }

    [Fact]
    public void GivenNotFoundEntry_WhenAccessingSearchParameterMappings_ThenReturnsFalse()
    {
        // Act
        var result = _cache.SearchParameterMappings.TryGetValue(
            "http://example.org/SearchParameter/NotFound", out var searchParamId);

        // Assert
        result.ShouldBeFalse("non-existent entries should return false");
        searchParamId.ShouldBe((short)0, "default value should be returned");
    }

    [Fact]
    public void GivenMissingSystem_WhenAccessingSystemMappings_ThenCreatesNewEntry()
    {
        // Arrange
        const string systemUri = "http://example.org/new-system";

        // Act
        var result = _cache.SystemMappings.TryGetValue(systemUri, out var systemId);

        // Assert
        result.ShouldBeTrue("GetOrCreate should always succeed for systems");
        systemId.ShouldBeGreaterThan(0, "created ID should be valid");

        var dbEntry = _context.Systems.FirstOrDefault(s => s.Value == systemUri);
        dbEntry.ShouldNotBeNull("entry should be persisted to database");
        dbEntry.SystemId.ShouldBe(systemId, "database ID should match returned ID");
    }

    [Fact]
    public void GivenMissingQuantityCode_WhenAccessingQuantityCodeMappings_ThenCreatesNewEntry()
    {
        // Arrange
        const string code = "new-unit";

        // Act
        var result = _cache.QuantityCodeMappings.TryGetValue(code, out var codeId);

        // Assert
        result.ShouldBeTrue("GetOrCreate should always succeed for quantity codes");
        codeId.ShouldBeGreaterThan(0, "created ID should be valid");

        var dbEntry = _context.QuantityCodes.FirstOrDefault(qc => qc.Value == code);
        dbEntry.ShouldNotBeNull("entry should be persisted to database");
        dbEntry.QuantityCodeId.ShouldBe(codeId, "database ID should match returned ID");
    }

    [Fact]
    public void GivenSubsequentAccesses_WhenAccessingSameKey_ThenReturnsCachedValue()
    {
        // Arrange
        const string systemUri = "http://snomed.info/sct";
        _context.Systems.Add(new SystemEntity { Value = systemUri });
        _context.SaveChanges();

        // Act
        var firstResult = _cache.SystemMappings.TryGetValue(systemUri, out var firstId);
        var secondResult = _cache.SystemMappings.TryGetValue(systemUri, out var secondId);

        // Assert
        firstResult.ShouldBeTrue();
        secondResult.ShouldBeTrue();
        secondId.ShouldBe(firstId, "subsequent accesses should return cached value");
    }

    [Fact]
    public void GivenFilteredSentinelValues_WhenAccessingResourceTypeMappings_ThenFiltersSentinelValues()
    {
        // Arrange: a resource type absent from dbo.ResourceType is deliberately NOT cached as a -1 sentinel
        // in the resource-type cache (see SearchIndexReferenceDataCache.GetResourceTypeIdAsync) -- each miss
        // re-queries. The wrapper's load function still maps "not found" to -1 and its isValidValue filter
        // (value > 0) still rejects it, so a repeated miss keeps answering false rather than surfacing -1.
        _ = _cache.ResourceTypeMappings.TryGetValue("NonExistent", out _);

        // Act
        var result = _cache.ResourceTypeMappings.TryGetValue("NonExistent", out var id);

        // Assert
        result.ShouldBeFalse("sentinel values should be filtered");
        id.ShouldBe((short)0, "default value returned for sentinel");
    }

    [Fact]
    public async Task GivenMultipleThreads_WhenAccessingSystemMappings_ThenHandlesConcurrentAccess()
    {
        // Arrange
        const string systemUri = "http://concurrent-test.org";

        // Act
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            await Task.Yield();
            return _cache.SystemMappings.TryGetValue(systemUri, out var id) ? id : 0;
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.ShouldAllBe(id => id > 0, "all threads should get valid ID");
        results.Distinct().Count().ShouldBe(1, "all threads should get same ID");

        var dbEntries = _context.Systems.Where(s => s.Value == systemUri).ToList();
        dbEntries.Count.ShouldBe(1, "only one entry should be created despite concurrent access");
    }

    [Fact]
    public async Task GivenASeededSystem_WhenGetSystemIdAsync_ThenReturnsItsId()
    {
        // Arrange
        var systemEntity = new SystemEntity { Value = "http://loinc.org" };
        _context.Systems.Add(systemEntity);
        await _context.SaveChangesAsync();
        var expectedId = systemEntity.SystemId;

        // Act
        var result = await _cache.GetSystemIdAsync("http://loinc.org");

        // Assert
        result.ShouldBe(expectedId);
    }

    [Fact]
    public async Task GivenAnUnknownSystem_WhenGetSystemIdAsync_ThenReturnsNull()
    {
        // Act
        var result = await _cache.GetSystemIdAsync("http://unknown.example");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAnUnknownSystem_WhenGetSystemIdAsync_ThenDoesNotAddEntity()
    {
        // Act
        await _cache.GetSystemIdAsync("http://unknown.example");

        // Assert
        _context.Systems.Any(s => s.Value == "http://unknown.example").ShouldBeFalse(
            "read-only lookup must never insert a row");

        _context.ChangeTracker.Entries<SystemEntity>()
            .Any(e => e.State == EntityState.Added && e.Entity.Value == "http://unknown.example")
            .ShouldBeFalse("read-only lookup must not stage a new entity in Added state");
    }

    [Fact]
    public async Task GivenASeededQuantityCode_WhenGetQuantityCodeIdAsync_ThenReturnsItsId()
    {
        // Arrange
        var codeEntity = new QuantityCodeEntity { Value = "mg" };
        _context.QuantityCodes.Add(codeEntity);
        await _context.SaveChangesAsync();
        var expectedId = codeEntity.QuantityCodeId;

        // Act
        var result = await _cache.GetQuantityCodeIdAsync("mg");

        // Assert
        result.ShouldBe(expectedId);
    }

    [Fact]
    public async Task GivenAnUnknownQuantityCode_WhenGetQuantityCodeIdAsync_ThenReturnsNull()
    {
        // Act
        var result = await _cache.GetQuantityCodeIdAsync("unknown");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAnUnknownQuantityCode_WhenGetQuantityCodeIdAsync_ThenDoesNotAddEntity()
    {
        // Act
        await _cache.GetQuantityCodeIdAsync("unknown");

        // Assert
        _context.QuantityCodes.Any(qc => qc.Value == "unknown").ShouldBeFalse(
            "read-only lookup must never insert a row");

        _context.ChangeTracker.Entries<QuantityCodeEntity>()
            .Any(e => e.State == EntityState.Added && e.Entity.Value == "unknown")
            .ShouldBeFalse("read-only lookup must not stage a new entity in Added state");
    }

    [Fact]
    public async Task GivenAKnownMissingSystem_WhenGetSystemIdAsyncCalledAgain_ThenAnswersFromNegativeCacheWithoutQuerying()
    {
        // Arrange: prove the second call never reaches the database by inserting the row behind the
        // cache's back after the miss was recorded -- a query would find it, the negative cache cannot.
        const string systemUri = "http://negative-cache.example/system";
        (await _cache.GetSystemIdAsync(systemUri)).ShouldBeNull();

        _context.Systems.Add(new SystemEntity { Value = systemUri });
        await _context.SaveChangesAsync();

        // Act
        var result = await _cache.GetSystemIdAsync(systemUri);

        // Assert
        result.ShouldBeNull("a recorded miss must be served from memory, not re-queried");
    }

    [Fact]
    public async Task GivenAKnownMissingQuantityCode_WhenGetQuantityCodeIdAsyncCalledAgain_ThenAnswersFromNegativeCacheWithoutQuerying()
    {
        // Arrange
        const string code = "negative-cache-unit";
        (await _cache.GetQuantityCodeIdAsync(code)).ShouldBeNull();

        _context.QuantityCodes.Add(new QuantityCodeEntity { Value = code });
        await _context.SaveChangesAsync();

        // Act
        var result = await _cache.GetQuantityCodeIdAsync(code);

        // Assert
        result.ShouldBeNull("a recorded miss must be served from memory, not re-queried");
    }

    [Fact]
    public async Task GivenAKnownMissingSystem_WhenGetOrCreateSystemIdAsyncCreatesIt_ThenGetSystemIdAsyncReturnsTheNewId()
    {
        // Arrange
        const string systemUri = "http://invalidation.example/system";
        (await _cache.GetSystemIdAsync(systemUri)).ShouldBeNull();

        // Act
        var createdId = await _cache.GetOrCreateSystemIdAsync(systemUri);
        var lookedUpId = await _cache.GetSystemIdAsync(systemUri);

        // Assert
        createdId.ShouldNotBeNull();
        lookedUpId.ShouldBe(createdId, "creating the row must invalidate the recorded miss");
    }

    [Fact]
    public async Task GivenAKnownMissingQuantityCode_WhenGetOrCreateQuantityCodeIdAsyncCreatesIt_ThenGetQuantityCodeIdAsyncReturnsTheNewId()
    {
        // Arrange
        const string code = "invalidation-unit";
        (await _cache.GetQuantityCodeIdAsync(code)).ShouldBeNull();

        // Act
        var createdId = await _cache.GetOrCreateQuantityCodeIdAsync(code);
        var lookedUpId = await _cache.GetQuantityCodeIdAsync(code);

        // Assert
        createdId.ShouldNotBeNull();
        lookedUpId.ShouldBe(createdId, "creating the row must invalidate the recorded miss");
    }

    [Fact]
    public async Task GivenAnUnknownSystem_WhenGetSystemIdAsync_ThenPositiveCacheIsNotPolluted()
    {
        // Act
        await _cache.GetSystemIdAsync("http://sentinel.example/system");

        // Assert: the write path reads every entry in the positive cache as a real surrogate key, so a
        // sentinel landing there would be handed to the indexer as a SystemId.
        _cache.GetStatistics().SystemCount.ShouldBe(0, "a miss must not add an entry to the positive cache");
        _cache.SystemMappings.Keys.ShouldNotContain("http://sentinel.example/system");
    }

    [Fact]
    public async Task GivenAnUnknownQuantityCode_WhenGetQuantityCodeIdAsync_ThenPositiveCacheIsNotPolluted()
    {
        // Act
        await _cache.GetQuantityCodeIdAsync("sentinel-unit");

        // Assert
        _cache.GetStatistics().QuantityCodeCount.ShouldBe(0, "a miss must not add an entry to the positive cache");
        _cache.QuantityCodeMappings.Keys.ShouldNotContain("sentinel-unit");
    }

    [Fact]
    public async Task GivenACancelledToken_WhenGetSystemIdAsync_ThenThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetSystemIdAsync("http://cancelled.example/system", cts.Token));
    }

    [Fact]
    public async Task GivenACancelledToken_WhenGetQuantityCodeIdAsync_ThenThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetQuantityCodeIdAsync("cancelled-unit", cts.Token));
    }

    [Fact]
    public async Task GivenACancelledToken_WhenGetOrCreateSystemIdAsync_ThenThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetOrCreateSystemIdAsync("http://cancelled.example/create", cts.Token));

        _context.Systems.Any(s => s.Value == "http://cancelled.example/create").ShouldBeFalse(
            "a cancelled call must not have created a row");
    }

    [Fact]
    public async Task GivenACancelledToken_WhenGetOrCreateQuantityCodeIdAsync_ThenThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.GetOrCreateQuantityCodeIdAsync("cancelled-create-unit", cts.Token));

        _context.QuantityCodes.Any(qc => qc.Value == "cancelled-create-unit").ShouldBeFalse(
            "a cancelled call must not have created a row");
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    private void SeedReferenceData()
    {
        _context.ResourceTypes.AddRange(
            new ResourceTypeEntity { ResourceTypeId = 1, Name = "Patient" },
            new ResourceTypeEntity { ResourceTypeId = 2, Name = "Organization" },
            new ResourceTypeEntity { ResourceTypeId = 3, Name = "Observation" },
            new ResourceTypeEntity { ResourceTypeId = 4, Name = "Practitioner" },
            new ResourceTypeEntity { ResourceTypeId = 5, Name = "Encounter" });

        _context.SearchParams.AddRange(
            new SearchParamEntity { SearchParamId = 1, Uri = PatientNameParameterUri, Status = "Enabled" },
            new SearchParamEntity { SearchParamId = 2, Uri = "http://hl7.org/fhir/SearchParameter/Patient-organization", Status = "Enabled" },
            new SearchParamEntity { SearchParamId = 3, Uri = "http://hl7.org/fhir/SearchParameter/Observation-patient", Status = "Enabled" },
            new SearchParamEntity { SearchParamId = 4, Uri = "http://hl7.org/fhir/SearchParameter/Observation-code", Status = "Enabled" },
            new SearchParamEntity { SearchParamId = 5, Uri = "http://hl7.org/fhir/SearchParameter/Organization-name", Status = "Enabled" },
            new SearchParamEntity { SearchParamId = 6, Uri = "http://hl7.org/fhir/SearchParameter/Encounter-subject", Status = "Enabled" });

        _context.SaveChanges();
    }
}
