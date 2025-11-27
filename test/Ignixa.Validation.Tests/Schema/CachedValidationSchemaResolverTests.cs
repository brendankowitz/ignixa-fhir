// <copyright file="CachedValidationSchemaResolverTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Unit tests for CachedValidationSchemaResolver.
/// Tests caching behavior and thread-safety.
/// </summary>
public class CachedValidationSchemaResolverTests
{
    private readonly ISchema _schema;
    private readonly StructureDefinitionSchemaResolver _innerResolver;

    public CachedValidationSchemaResolverTests()
    {
        _schema = new R4CoreSchemaProvider();
        _innerResolver = new StructureDefinitionSchemaResolver(_schema);
    }

    #region Basic Caching Tests

    [Fact]
    public void GivenSameCanonicalUrl_WhenCalledTwice_ThenReturnsCachedInstance()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";

        // Act
        var schema1 = cachedResolver.GetSchema(canonicalUrl);
        var schema2 = cachedResolver.GetSchema(canonicalUrl);

        // Assert
        schema1.Should().NotBeNull();
        schema2.Should().NotBeNull();
        schema1.Should().BeSameAs(schema2); // Same instance from cache
    }

    [Fact]
    public void GivenValidCanonicalUrl_WhenFirstCall_ThenBuildsSchema()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";

        // Act
        var schema = cachedResolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().NotBeNull();
        schema!.ResourceType.Should().Be("Patient");
        schema.CanonicalUrl.Should().Be(canonicalUrl);
        schema.Checks.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenValidCanonicalUrl_WhenSecondCall_ThenReturnsCachedSchema()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

        // Act
        var schema1 = cachedResolver.GetSchema(canonicalUrl);
        var schema2 = cachedResolver.GetSchema(canonicalUrl);

        // Assert
        schema1.Should().BeSameAs(schema2);
        cachedResolver.CacheCount.Should().Be(1);
    }

    #endregion

    #region Cache Count Tests

    [Fact]
    public void GivenNoCalls_WhenCheckingCacheCount_ThenReturnsZero()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);

        // Act & Assert
        cachedResolver.CacheCount.Should().Be(0);
    }

    [Fact]
    public void GivenOneCall_WhenCheckingCacheCount_ThenReturnsOne()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);

        // Act
        cachedResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");

        // Assert
        cachedResolver.CacheCount.Should().Be(1);
    }

    [Fact]
    public void GivenMultipleDifferentCalls_WhenCheckingCacheCount_ThenReturnsCorrectCount()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);

        // Act
        cachedResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
        cachedResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Observation");
        cachedResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Condition");

        // Assert
        cachedResolver.CacheCount.Should().Be(3);
    }

    [Fact]
    public void GivenMultipleSameCalls_WhenCheckingCacheCount_ThenReturnsOne()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";

        // Act
        cachedResolver.GetSchema(canonicalUrl);
        cachedResolver.GetSchema(canonicalUrl);
        cachedResolver.GetSchema(canonicalUrl);

        // Assert
        cachedResolver.CacheCount.Should().Be(1);
    }

    #endregion

    #region Clear Cache Tests

    [Fact]
    public void GivenCachedSchemas_WhenClearingCache_ThenCacheCountIsZero()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        cachedResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
        cachedResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Observation");
        cachedResolver.CacheCount.Should().Be(2);

        // Act
        cachedResolver.ClearCache();

        // Assert
        cachedResolver.CacheCount.Should().Be(0);
    }

    [Fact]
    public void GivenCachedSchemas_WhenClearingCacheAndCallingAgain_ThenRebuildsSchema()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";
        var schema1 = cachedResolver.GetSchema(canonicalUrl);

        // Act
        cachedResolver.ClearCache();
        var schema2 = cachedResolver.GetSchema(canonicalUrl);

        // Assert
        schema1.Should().NotBeNull();
        schema2.Should().NotBeNull();
        schema1.Should().NotBeSameAs(schema2); // Different instances after cache clear
        schema1!.ResourceType.Should().Be(schema2!.ResourceType); // Same content
    }

    [Fact]
    public void GivenEmptyCache_WhenClearingCache_ThenNoException()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);

        // Act
        var act = () => cachedResolver.ClearCache();

        // Assert
        act.Should().NotThrow();
        cachedResolver.CacheCount.Should().Be(0);
    }

    #endregion

    #region Null Caching Tests

    [Fact]
    public void GivenInvalidCanonicalUrl_WhenCalled_ThenCachesNullResult()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/InvalidResource";

        // Act
        var schema1 = cachedResolver.GetSchema(canonicalUrl);
        var schema2 = cachedResolver.GetSchema(canonicalUrl);

        // Assert
        schema1.Should().BeNull();
        schema2.Should().BeNull();
        cachedResolver.CacheCount.Should().Be(1); // Null result is cached
    }

    [Fact]
    public void GivenEmptyCanonicalUrl_WhenCalled_ThenReturnsNullWithoutCaching()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);

        // Act
        var schema = cachedResolver.GetSchema(string.Empty);

        // Assert
        schema.Should().BeNull();
        cachedResolver.CacheCount.Should().Be(0); // Empty string not cached
    }

    [Fact]
    public void GivenNullCanonicalUrl_WhenCalled_ThenReturnsNullWithoutCaching()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);

        // Act
        var schema = cachedResolver.GetSchema(null!);

        // Assert
        schema.Should().BeNull();
        cachedResolver.CacheCount.Should().Be(0); // Null not cached
    }

    #endregion

    #region Case Insensitivity Tests

    [Fact]
    public void GivenCanonicalUrlWithDifferentCasing_WhenCalled_ThenReturnsSameCachedInstance()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var url1 = "http://hl7.org/fhir/StructureDefinition/Patient";
        var url2 = "HTTP://HL7.ORG/FHIR/STRUCTUREDEFINITION/PATIENT";

        // Act
        var schema1 = cachedResolver.GetSchema(url1);
        var schema2 = cachedResolver.GetSchema(url2);

        // Assert
        schema1.Should().BeSameAs(schema2); // Case insensitive caching
        cachedResolver.CacheCount.Should().Be(1);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void GivenNullInnerResolver_WhenCreating_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new CachedValidationSchemaResolver(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("inner");
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task GivenConcurrentCalls_WhenCalledFromMultipleThreads_ThenCachesCorrectly()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrls = new[]
        {
            "http://hl7.org/fhir/StructureDefinition/Patient",
            "http://hl7.org/fhir/StructureDefinition/Observation",
            "http://hl7.org/fhir/StructureDefinition/Condition",
            "http://hl7.org/fhir/StructureDefinition/Medication",
            "http://hl7.org/fhir/StructureDefinition/Procedure"
        };

        // Act - Simulate concurrent calls
        var tasks = canonicalUrls
            .SelectMany(url => Enumerable.Range(0, 10).Select(_ => Task.Run(() => cachedResolver.GetSchema(url))))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Each URL should be cached exactly once
        cachedResolver.CacheCount.Should().Be(5);
    }

    [Fact]
    public async Task GivenConcurrentCallsForSameUrl_WhenCalledFromMultipleThreads_ThenReturnsSameInstance()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";
        var schemas = new System.Collections.Concurrent.ConcurrentBag<object>();

        // Act - Multiple threads requesting the same schema
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() =>
            {
                var schema = cachedResolver.GetSchema(canonicalUrl);
                if (schema != null)
                {
                    schemas.Add(schema);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All threads should get the same instance
        schemas.Should().HaveCount(100);
        schemas.Distinct().Should().HaveCount(1); // All are the same instance
        cachedResolver.CacheCount.Should().Be(1);
    }

    #endregion

    #region Multiple Resource Types Tests

    [Fact]
    public void GivenMultipleResourceTypes_WhenCached_ThenEachHasUniqueEntry()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var resourceTypes = new[] { "Patient", "Observation", "Condition", "Medication" };

        // Act
        var schemas = resourceTypes
            .Select(rt => cachedResolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{rt}"))
            .ToList();

        // Assert
        schemas.Should().AllSatisfy(s => s.Should().NotBeNull());
        schemas.Select(s => s!.ResourceType).Should().OnlyHaveUniqueItems();
        cachedResolver.CacheCount.Should().Be(4);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void GivenCachedSchema_WhenAccessedMultipleTimes_ThenFasterThanUncached()
    {
        // Arrange
        var cachedResolver = new CachedValidationSchemaResolver(_innerResolver);
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";

        // Warm up cache
        var warmup = cachedResolver.GetSchema(canonicalUrl);
        warmup.Should().NotBeNull();

        // Act - Multiple cached accesses should be very fast
        var startCached = DateTime.UtcNow;
        for (int i = 0; i < 1000; i++)
        {
            var schema = cachedResolver.GetSchema(canonicalUrl);
            schema.Should().NotBeNull();
        }
        var cachedDuration = DateTime.UtcNow - startCached;

        // Compare with uncached (build new schema each time)
        var startUncached = DateTime.UtcNow;
        for (int i = 0; i < 1000; i++)
        {
            var schema = _innerResolver.GetSchema(canonicalUrl);
            schema.Should().NotBeNull();
        }
        var uncachedDuration = DateTime.UtcNow - startUncached;

        // Assert - Cached should be significantly faster
        cachedDuration.Should().BeLessThan(uncachedDuration / 10); // At least 10x faster
    }

    #endregion
}
