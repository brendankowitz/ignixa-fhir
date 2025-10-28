// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain;

namespace Ignixa.Application.Features.Metadata;

/// <summary>
/// Service for generating and caching FHIR CapabilityStatements using segmented architecture.
/// Orchestrates capability segments, manages version-aware caching, and handles multi-tenant scenarios.
/// Phase 1.2 implementation with in-memory caching.
/// </summary>
public class CapabilityStatementService
{
    private readonly IEnumerable<ICapabilitySegment> _segments;
    private readonly ICapabilityCache _cache;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<CapabilityStatementService> _logger;

    public CapabilityStatementService(
        IEnumerable<ICapabilitySegment> segments,
        ICapabilityCache cache,
        ITenantConfigurationStore tenantConfigStore,
        IFhirVersionContext versionContext,
        ILogger<CapabilityStatementService> logger)
    {
        _segments = segments ?? throw new ArgumentNullException(nameof(segments));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the CapabilityStatement for the specified context.
    /// Uses smart caching with version hash validation.
    /// </summary>
    public async ValueTask<CapabilityStatementJsonNode> GetCapabilityStatementAsync(
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = context.ToCacheKey();

        _logger.LogDebug(
            "Getting capability statement for context: FhirVersion={FhirVersion}, TenantId={TenantId}, CacheKey={CacheKey}",
            context.FhirVersion,
            context.TenantId?.ToString() ?? "null",
            cacheKey);

        // 1. Check cache
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);

        if (cached != null)
        {
            // 2. Validate cached entry using version hash
            var currentHash = await ComputeVersionHashAsync(context, cancellationToken);

            if (cached.VersionHash == currentHash)
            {
                _logger.LogDebug(
                    "Cache hit for {CacheKey} - version hash matches ({Hash})",
                    cacheKey,
                    currentHash.AsSpan(0, Math.Min(8, currentHash.Length)).ToString());

                return cached.Statement;
            }

            _logger.LogDebug(
                "Cache hit for {CacheKey} but version hash mismatch (cached={CachedHash}, current={CurrentHash}) - rebuilding",
                cacheKey,
                cached.VersionHash.AsSpan(0, Math.Min(8, cached.VersionHash.Length)).ToString(),
                currentHash.AsSpan(0, Math.Min(8, currentHash.Length)).ToString());
        }
        else
        {
            _logger.LogDebug("Cache miss for {CacheKey} - building new capability statement", cacheKey);
        }

        // 3. Build new capability statement
        var statement = await BuildCapabilityStatementAsync(context, cancellationToken);

        // 4. Compute version hash for caching
        var versionHash = await ComputeVersionHashAsync(context, cancellationToken);

        // 5. Cache the new statement
        var cacheEntry = new CapabilityCacheEntry(statement, versionHash, DateTimeOffset.UtcNow);

        await _cache.SetAsync(
            cacheKey,
            cacheEntry,
            expiration: TimeSpan.FromHours(1), // 1-hour TTL as safety net
            cancellationToken);

        _logger.LogInformation(
            "Built and cached capability statement for {CacheKey} with version hash {Hash}",
            cacheKey,
            versionHash.AsSpan(0, Math.Min(8, versionHash.Length)).ToString());

        return statement;
    }

    /// <summary>
    /// Builds a new CapabilityStatement by applying all segments in priority order.
    /// </summary>
    private async ValueTask<CapabilityStatementJsonNode> BuildCapabilityStatementAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var statement = new CapabilityStatementJsonNode();

        // Get tenant configuration if tenant-specific
        Domain.Models.TenantConfiguration? tenantConfig = null;
        if (context.TenantId.HasValue)
        {
            tenantConfig = await _tenantConfigStore.GetTenantConfigurationAsync(
                context.TenantId.Value,
                cancellationToken);

            if (tenantConfig == null)
            {
                throw new InvalidOperationException($"Tenant {context.TenantId} not found or inactive");
            }
        }

        // Set basic properties that change per request
        statement.Url = "http://Ignixa.example.com/fhir/CapabilityStatement";
        statement.Version = "0.1.0";
        statement.Date = DateTimeOffset.UtcNow.ToString("O");

        // Use FullVersion from schema provider to include ballot/patch versions (e.g., "6.0.0-ballot2" for R6)
        var schemaProvider = _versionContext.GetSchemaProvider(context.FhirVersion);
        statement.FhirVersionString = schemaProvider.FullVersion;

        // Set name based on tenant
        statement.Name = tenantConfig != null
            ? $"IgnixaFhirServer_{tenantConfig.DisplayName.Replace(" ", string.Empty, StringComparison.Ordinal)}"
            : "IgnixaFhirServer";

        // Apply segments in priority order
        var orderedSegments = _segments.OrderBy(s => s.Priority).ToList();

        _logger.LogDebug(
            "Applying {Count} capability segments in priority order: {Segments}",
            orderedSegments.Count,
            string.Join(", ", orderedSegments.Select(s => $"{s.SegmentKey}({s.Priority})")));

        foreach (var segment in orderedSegments)
        {
            _logger.LogTrace("Applying segment {SegmentKey} (priority {Priority})", segment.SegmentKey, segment.Priority);

            await segment.ApplyAsync(statement, context, cancellationToken);
        }

        _logger.LogDebug(
            "Built capability statement with {ResourceCount} resources",
            statement.Rest?[0]?.Resource?.Count ?? 0);

        return statement;
    }

    /// <summary>
    /// Computes composite version hash from all segment hashes.
    /// If any segment's hash changes, the composite hash changes, invalidating the cache.
    /// </summary>
    private async ValueTask<string> ComputeVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var hashes = new List<string>();

        foreach (var segment in _segments.OrderBy(s => s.Priority))
        {
            var hash = await segment.GetVersionHashAsync(context, cancellationToken);
            hashes.Add(hash);
        }

        // Combine all segment hashes with separator
        var compositeHash = string.Join("|", hashes);

        _logger.LogTrace(
            "Computed composite version hash for {FhirVersion}: {Hash}",
            context.FhirVersion,
            compositeHash.Length > 50 ? string.Concat(compositeHash.AsSpan(0, 50), "...") : compositeHash);

        return compositeHash;
    }

    /// <summary>
    /// Invalidates the cache for a specific context.
    /// Used when capabilities need to be manually refreshed.
    /// </summary>
    public async ValueTask InvalidateCacheAsync(
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = context.ToCacheKey();
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        _logger.LogInformation("Invalidated capability cache for {CacheKey}", cacheKey);
    }

    /// <summary>
    /// Clears all cached capability statements.
    /// Used for admin operations or when segments are reconfigured.
    /// </summary>
    public async ValueTask ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        await _cache.ClearAsync(cancellationToken);
        _logger.LogInformation("Cleared all capability caches");
    }
}
