// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.Transform;

/// <summary>
/// Thread-safe cache for compiled FHIRPath expressions.
/// Compiles expressions once and reuses them for better performance in mapping transformations.
/// </summary>
public class FhirPathExpressionCache
{
    private readonly ConcurrentDictionary<string, Expression> _cache = new();
    private readonly FhirPathParser _compiler;
    private readonly ILogger<FhirPathExpressionCache> _logger;

    // Cache statistics for monitoring
    private long _cacheHits;
    private long _cacheMisses;
    private long _compilationTimeMs;

    public FhirPathExpressionCache(
        FhirPathParser compiler,
        ILogger<FhirPathExpressionCache> logger)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a compiled expression from the cache or compiles it if not cached.
    /// Thread-safe for concurrent access.
    /// </summary>
    /// <param name="expression">The FHIRPath expression string to compile</param>
    /// <returns>Compiled FHIRPath expression AST</returns>
    /// <exception cref="ArgumentException">Thrown when expression is null or whitespace</exception>
    /// <exception cref="FormatException">Thrown when expression cannot be parsed</exception>
    public Expression GetOrCompile(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("Expression cannot be null or whitespace", nameof(expression));
        }

        // Try to get from cache first
        if (_cache.TryGetValue(expression, out var compiled))
        {
            Interlocked.Increment(ref _cacheHits);
            _logger.LogTrace("FHIRPath cache hit: {Expression}", expression);
            return compiled;
        }

        // Cache miss - compile the expression
        Interlocked.Increment(ref _cacheMisses);

        var stopwatch = Stopwatch.StartNew();
        Expression result;

        try
        {
            result = _compiler.Parse(expression);
        }
        catch (FormatException ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse FHIRPath expression: {Expression}",
                expression);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            Interlocked.Add(ref _compilationTimeMs, stopwatch.ElapsedMilliseconds);
        }

        // Add to cache (TryAdd handles concurrent additions gracefully)
        _cache.TryAdd(expression, result);

        _logger.LogDebug(
            "Compiled FHIRPath expression (took {Ms}ms): {Expression}",
            stopwatch.ElapsedMilliseconds,
            expression);

        return result;
    }

    /// <summary>
    /// Gets cache statistics for monitoring and diagnostics.
    /// </summary>
    public FhirPathExpressionCacheStatistics GetStatistics()
    {
        var hits = Interlocked.Read(ref _cacheHits);
        var misses = Interlocked.Read(ref _cacheMisses);
        var totalRequests = hits + misses;
        var hitRate = totalRequests > 0 ? (double)hits / totalRequests : 0.0;

        return new FhirPathExpressionCacheStatistics
        {
            CacheHits = hits,
            CacheMisses = misses,
            TotalRequests = totalRequests,
            HitRate = hitRate,
            CachedExpressionCount = _cache.Count,
            TotalCompilationTimeMs = Interlocked.Read(ref _compilationTimeMs)
        };
    }

    /// <summary>
    /// Clears all cached expressions and resets statistics.
    /// Useful for testing or when expression definitions change.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _compilationTimeMs, 0);

        _logger.LogInformation("FHIRPath expression cache cleared");
    }
}

/// <summary>
/// Statistics about FHIRPath expression cache performance.
/// </summary>
public class FhirPathExpressionCacheStatistics
{
    /// <summary>
    /// Number of cache hits (expressions retrieved from cache).
    /// </summary>
    public long CacheHits { get; init; }

    /// <summary>
    /// Number of cache misses (expressions compiled on-demand).
    /// </summary>
    public long CacheMisses { get; init; }

    /// <summary>
    /// Total number of cache requests (hits + misses).
    /// </summary>
    public long TotalRequests { get; init; }

    /// <summary>
    /// Cache hit rate (0.0 to 1.0).
    /// </summary>
    public double HitRate { get; init; }

    /// <summary>
    /// Number of expressions currently cached.
    /// </summary>
    public int CachedExpressionCount { get; init; }

    /// <summary>
    /// Total time spent compiling expressions (milliseconds).
    /// </summary>
    public long TotalCompilationTimeMs { get; init; }
}
