// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Application.Operations.Features.Transform;
using Ignixa.FhirPath.Parser;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Application.Tests.Features.Transform;

public class FhirPathExpressionCacheTests
{
    #region Cache Behavior Tests

    [Fact]
    [Trait("Category", "Integration")]
    public void GivenNewExpression_WhenGetOrCompile_ThenCompilesAndCaches()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);
        var expression = "Patient.name.family";

        // Act
        var compiled1 = cache.GetOrCompile(expression);
        var compiled2 = cache.GetOrCompile(expression);

        // Assert
        compiled1.ShouldNotBeNull();
        compiled2.ShouldBeSameAs(compiled1, "second call should return cached instance");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GivenCachedExpression_WhenGetOrCompile_ThenReturnsFromCache()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);
        var expression = "Patient.active";

        // Pre-populate cache
        cache.GetOrCompile(expression);

        // Act
        var result = cache.GetOrCompile(expression);

        // Assert
        result.ShouldNotBeNull();
        var stats = cache.GetStatistics();
        stats.CacheHits.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GivenMultipleCalls_WhenGetStatistics_ThenShowsHitRate()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        // Act
        cache.GetOrCompile("Patient.name"); // Miss
        cache.GetOrCompile("Patient.name"); // Hit
        cache.GetOrCompile("Patient.name"); // Hit
        cache.GetOrCompile("Patient.active"); // Miss
        cache.GetOrCompile("Patient.active"); // Hit

        var stats = cache.GetStatistics();

        // Assert
        stats.TotalRequests.ShouldBe(5);
        stats.CacheHits.ShouldBe(3);
        stats.CacheMisses.ShouldBe(2);
        stats.HitRate.ShouldBe(0.6); // 3/5 = 0.6
        stats.CachedExpressionCount.ShouldBe(2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GivenInvalidExpression_WhenGetOrCompile_ThenThrowsFormatException()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        // Act & Assert
        var act = () => cache.GetOrCompile("Invalid((((");
        Should.Throw<FormatException>(act);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GivenNullExpression_WhenGetOrCompile_ThenThrowsArgumentException()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        // Act & Assert
        var act = () => cache.GetOrCompile(null!);
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GivenWhitespaceExpression_WhenGetOrCompile_ThenThrowsArgumentException()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        // Act & Assert
        var act = () => cache.GetOrCompile("   ");
        Should.Throw<ArgumentException>(act);
    }

    #endregion

    #region Cache Management Tests

    [Fact]
    [Trait("Category", "Integration")]
    public void GivenPopulatedCache_WhenClear_ThenResetsStatistics()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        cache.GetOrCompile("Patient.name");
        cache.GetOrCompile("Patient.name");
        cache.GetOrCompile("Patient.active");

        // Act
        cache.Clear();
        var stats = cache.GetStatistics();

        // Assert
        stats.TotalRequests.ShouldBe(0);
        stats.CacheHits.ShouldBe(0);
        stats.CacheMisses.ShouldBe(0);
        stats.CachedExpressionCount.ShouldBe(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GivenMultipleExpressions_WhenGetStatistics_ThenTracksCompilationTime()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        // Act
        cache.GetOrCompile("Patient.name.family");
        cache.GetOrCompile("Patient.birthDate");
        cache.GetOrCompile("Patient.identifier.where(system='http://example.org').value");

        var stats = cache.GetStatistics();

        // Assert
        stats.TotalCompilationTimeMs.ShouldBeGreaterThan(0, "compilation should take some time");
        stats.CacheMisses.ShouldBe(3, "all should be cache misses");
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GivenConcurrentAccess_WhenGetOrCompile_ThenThreadSafe()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);
        var expression = "Patient.name.family";

        // Act - simulate concurrent access
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => cache.GetOrCompile(expression))
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert - all should return same compiled instance
        var compiled = cache.GetOrCompile(expression);
        // All tasks complete successfully
        tasks.ShouldAllBe(t => t.IsCompletedSuccessfully);

        var stats = cache.GetStatistics();
        stats.CachedExpressionCount.ShouldBe(1);
    }

    #endregion
}
