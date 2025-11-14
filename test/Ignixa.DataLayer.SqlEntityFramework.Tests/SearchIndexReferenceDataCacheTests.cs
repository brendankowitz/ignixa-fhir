// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

/// <summary>
/// Tests for SearchIndexReferenceDataCache lazy-loading behavior.
/// </summary>
public class SearchIndexReferenceDataCacheTests : TestBase
{
    [Fact]
    public void GivenEmptyCache_WhenAccessingSystemMappings_ThenLazyLoadsFromDatabase()
    {
        // Arrange: Add a system to database but don't preload cache
        var systemUri = "http://loinc.org";
        var systemEntity = new SystemEntity { Value = systemUri };
        Context.Systems.Add(systemEntity);
        Context.SaveChanges();

        // Act: Access SystemMappings property and try to get value
        var result = Cache.SystemMappings.TryGetValue(systemUri, out var systemId);

        // Assert: Should lazy-load and return the ID
        result.Should().BeTrue("lazy-loading should populate cache on miss");
        systemId.Should().BeGreaterThan(0, "loaded ID should be valid");
    }

    [Fact]
    public void GivenEmptyCache_WhenAccessingQuantityCodeMappings_ThenLazyLoadsFromDatabase()
    {
        // Arrange: Add a quantity code to database but don't preload cache
        var code = "mg";
        var quantityCodeEntity = new QuantityCodeEntity { Value = code };
        Context.QuantityCodes.Add(quantityCodeEntity);
        Context.SaveChanges();

        // Act: Access QuantityCodeMappings property and try to get value
        var result = Cache.QuantityCodeMappings.TryGetValue(code, out var codeId);

        // Assert: Should lazy-load and return the ID
        result.Should().BeTrue("lazy-loading should populate cache on miss");
        codeId.Should().BeGreaterThan(0, "loaded ID should be valid");
    }

    [Fact]
    public void GivenEmptyCache_WhenAccessingResourceTypeMappings_ThenLazyLoadsFromDatabase()
    {
        // Arrange: TestBase seeds common resource types including "Patient"
        // Cache is empty initially

        // Act: Access ResourceTypeMappings property and try to get value
        var result = Cache.ResourceTypeMappings.TryGetValue("Patient", out var resourceTypeId);

        // Assert: Should lazy-load and return the ID
        result.Should().BeTrue("lazy-loading should populate cache on miss");
        resourceTypeId.Should().BeGreaterThan(0, "loaded ID should be valid");
        resourceTypeId.Should().Be(1, "Patient should have ResourceTypeId 1");
    }

    [Fact]
    public void GivenEmptyCache_WhenAccessingSearchParameterMappings_ThenLazyLoadsFromDatabase()
    {
        // Arrange: TestBase seeds common search parameters
        var searchParamUri = "http://hl7.org/fhir/SearchParameter/Patient-name";

        // Act: Access SearchParameterMappings property and try to get value
        var result = Cache.SearchParameterMappings.TryGetValue(searchParamUri, out var searchParamId);

        // Assert: Should lazy-load and return the ID
        result.Should().BeTrue("lazy-loading should populate cache on miss");
        searchParamId.Should().BeGreaterThan(0, "loaded ID should be valid");
        searchParamId.Should().Be(1, "Patient-name should have SearchParamId 1");
    }

    [Fact]
    public void GivenNotFoundEntry_WhenAccessingResourceTypeMappings_ThenReturnsFalse()
    {
        // Arrange: Entry doesn't exist in database

        // Act: Try to get non-existent resource type
        var result = Cache.ResourceTypeMappings.TryGetValue("NonExistentType", out var resourceTypeId);

        // Assert: Should return false and default value
        result.Should().BeFalse("non-existent entries should return false");
        resourceTypeId.Should().Be(0, "default value should be returned");
    }

    [Fact]
    public void GivenNotFoundEntry_WhenAccessingSearchParameterMappings_ThenReturnsFalse()
    {
        // Arrange: Entry doesn't exist in database

        // Act: Try to get non-existent search parameter
        var result = Cache.SearchParameterMappings.TryGetValue("http://example.org/SearchParameter/NotFound", out var searchParamId);

        // Assert: Should return false and default value
        result.Should().BeFalse("non-existent entries should return false");
        searchParamId.Should().Be(0, "default value should be returned");
    }

    [Fact]
    public void GivenMissingSystem_WhenAccessingSystemMappings_ThenCreatesNewEntry()
    {
        // Arrange: System doesn't exist in database
        var systemUri = "http://example.org/new-system";

        // Act: Access SystemMappings - should create new entry
        var result = Cache.SystemMappings.TryGetValue(systemUri, out var systemId);

        // Assert: Should create entry and return valid ID
        result.Should().BeTrue("GetOrCreate should always succeed for systems");
        systemId.Should().BeGreaterThan(0, "created ID should be valid");

        // Verify entry was persisted to database
        var dbEntry = Context.Systems.FirstOrDefault(s => s.Value == systemUri);
        dbEntry.Should().NotBeNull("entry should be persisted to database");
        dbEntry!.SystemId.Should().Be(systemId, "database ID should match returned ID");
    }

    [Fact]
    public void GivenMissingQuantityCode_WhenAccessingQuantityCodeMappings_ThenCreatesNewEntry()
    {
        // Arrange: Code doesn't exist in database
        var code = "new-unit";

        // Act: Access QuantityCodeMappings - should create new entry
        var result = Cache.QuantityCodeMappings.TryGetValue(code, out var codeId);

        // Assert: Should create entry and return valid ID
        result.Should().BeTrue("GetOrCreate should always succeed for quantity codes");
        codeId.Should().BeGreaterThan(0, "created ID should be valid");

        // Verify entry was persisted to database
        var dbEntry = Context.QuantityCodes.FirstOrDefault(qc => qc.Value == code);
        dbEntry.Should().NotBeNull("entry should be persisted to database");
        dbEntry!.QuantityCodeId.Should().Be(codeId, "database ID should match returned ID");
    }

    [Fact]
    public void GivenSubsequentAccesses_WhenAccessingSameKey_ThenReturnsCachedValue()
    {
        // Arrange: Add system to database
        var systemUri = "http://snomed.info/sct";
        var systemEntity = new SystemEntity { Value = systemUri };
        Context.Systems.Add(systemEntity);
        Context.SaveChanges();

        // Act: Access same key twice
        var firstResult = Cache.SystemMappings.TryGetValue(systemUri, out var firstId);
        var secondResult = Cache.SystemMappings.TryGetValue(systemUri, out var secondId);

        // Assert: Both should succeed and return same ID
        firstResult.Should().BeTrue();
        secondResult.Should().BeTrue();
        secondId.Should().Be(firstId, "subsequent accesses should return cached value");
    }

    [Fact]
    public void GivenFilteredSentinelValues_WhenAccessingResourceTypeMappings_ThenFiltersSentinelValues()
    {
        // Arrange: Force a sentinel value into cache by querying non-existent entry
        _ = Cache.ResourceTypeMappings.TryGetValue("NonExistent", out _);

        // Verify sentinel was cached (using direct cache access would require reflection)
        // Instead, verify behavior: subsequent access should still return false
        var result = Cache.ResourceTypeMappings.TryGetValue("NonExistent", out var id);

        // Assert: Should still return false (sentinel filtered)
        result.Should().BeFalse("sentinel values should be filtered");
        id.Should().Be(0, "default value returned for sentinel");
    }

    [Fact]
    public async Task GivenMultipleThreads_WhenAccessingSystemMappings_ThenHandlesConcurrentAccess()
    {
        // Arrange: System URI that doesn't exist yet
        var systemUri = "http://concurrent-test.org";

        // Act: Access from multiple threads concurrently
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            await Task.Yield(); // Force async execution
            return Cache.SystemMappings.TryGetValue(systemUri, out var id) ? id : 0;
        });

        var results = await Task.WhenAll(tasks);

        // Assert: All threads should get same ID (thread-safe)
        results.Should().OnlyContain(id => id > 0, "all threads should get valid ID");
        results.Distinct().Should().HaveCount(1, "all threads should get same ID");

        // Verify only one entry was created in database
        var dbEntries = Context.Systems.Where(s => s.Value == systemUri).ToList();
        dbEntries.Should().HaveCount(1, "only one entry should be created despite concurrent access");
    }
}
