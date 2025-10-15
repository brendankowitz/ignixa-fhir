// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sparky.DataLayer.LegacySqlEF.Indexing;
using Sparky.Domain.Abstractions;

namespace Sparky.DataLayer.LegacySqlEF.Search;

/// <summary>
/// Factory for creating tenant-specific LegacySqlEfSearchService instances.
/// Implements caching to provide O(1) search service lookup after first access.
/// Each tenant gets its own search service with a dedicated DbContext.
/// </summary>
public class LegacySqlEfSearchServiceFactory : ISearchServiceFactory
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<int, ISearchService> _searchServiceCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacySqlEfSearchServiceFactory"/> class.
    /// </summary>
    /// <param name="repositoryFactory">The repository factory (for DbContext access).</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public LegacySqlEfSearchServiceFactory(
        IFhirRepositoryFactory repositoryFactory,
        ILoggerFactory loggerFactory)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _searchServiceCache = new ConcurrentDictionary<int, ISearchService>();
    }

    /// <inheritdoc/>
    public async Task<ISearchService> GetSearchServiceAsync(int tenantId, CancellationToken ct = default)
    {
        // Check cache first
        if (_searchServiceCache.TryGetValue(tenantId, out var cachedService))
        {
            return cachedService;
        }

        // Get repository (which validates tenant exists and is active)
        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, ct);

        // Extract DbContext from repository (this is a bit of a hack - in production we'd have a better way)
        // For now, we'll create a new DbContext with the same configuration
        var service = _searchServiceCache.GetOrAdd(tenantId, _ => CreateSearchService(tenantId, repository));

        return service;
    }

    private ISearchService CreateSearchService(int tenantId, IFhirRepository repository)
    {
        var logger = _loggerFactory.CreateLogger<LegacySqlEfSearchServiceFactory>();
        logger.LogInformation("Creating search service for tenant {TenantId}", tenantId);

        // Note: This is a simplified implementation
        // In production, we'd need access to the DbContext from the repository
        // For now, we'll throw NotImplementedException to indicate this needs proper wiring
        throw new NotImplementedException(
            "LegacySqlEfSearchServiceFactory.CreateSearchService needs to be wired with DbContext. " +
            "Consider refactoring to share DbContext between repository and search service.");
    }

    /// <summary>
    /// Clears the search service cache. Useful for testing or when tenant configurations change.
    /// </summary>
    public void ClearCache()
    {
        _searchServiceCache.Clear();
    }

    /// <summary>
    /// Gets the current number of cached search services.
    /// </summary>
    public int CachedSearchServiceCount => _searchServiceCache.Count;
}
