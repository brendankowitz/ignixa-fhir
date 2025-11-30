// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.DataLayer.FileSystem.FileSystem;

/// <summary>
/// Factory for creating and caching FileSystem-based search services per tenant.
/// Implements O(1) search service lookup after first access via ConcurrentDictionary caching.
/// </summary>
public class FileBasedSearchServiceFactory : ISearchServiceFactory
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ITenantConfigurationStore _configStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileBasedSearchServiceFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<int, ISearchService> _searchServiceCache;

    public FileBasedSearchServiceFactory(
        IFhirRepositoryFactory repositoryFactory,
        ITenantConfigurationStore configStore,
        IConfiguration configuration,
        ILogger<FileBasedSearchServiceFactory> logger,
        ILoggerFactory loggerFactory)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _searchServiceCache = new ConcurrentDictionary<int, ISearchService>();
    }

    public async Task<ISearchService> GetSearchServiceAsync(int tenantId, CancellationToken ct = default)
    {
        // Check cache first (O(1) lookup after first access)
        if (_searchServiceCache.TryGetValue(tenantId, out var cachedSearchService))
        {
            _logger.LogTrace("Search service cache hit for tenant {TenantId}", tenantId);
            return cachedSearchService;
        }

        _logger.LogDebug("Search service cache miss for tenant {TenantId}, creating new search service", tenantId);

        // Load tenant configuration
        var tenantConfig = await _configStore.GetTenantConfigurationAsync(tenantId, ct);
        if (tenantConfig == null)
        {
            _logger.LogWarning("Tenant {TenantId} not found or inactive", tenantId);
            throw new InvalidOperationException($"Tenant {tenantId} not found or inactive");
        }

        // Validate search type
        if (!string.Equals(tenantConfig.Search.Type, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Search type '{SearchType}' not supported by FileBasedSearchServiceFactory for tenant {TenantId}",
                tenantConfig.Search.Type,
                tenantId);
            throw new NotSupportedException(
                $"Search type '{tenantConfig.Search.Type}' not supported by FileBasedSearchServiceFactory. " +
                $"Only 'InMemory' search is supported.");
        }

        // Get tenant-specific repository from factory
        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, ct);

        // Build base directory for tenant (for index file access)
        string globalBaseDirectory = _configuration["FhirRepository:BaseDirectory"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "fhir-data");

        string tenantBaseDirectory = !string.IsNullOrEmpty(tenantConfig.Storage.BaseDirectory)
            ? Path.Combine(globalBaseDirectory, tenantConfig.Storage.BaseDirectory)
            : Path.Combine(globalBaseDirectory, "tenants", tenantId.ToString());

        // Create tenant-specific logger
        var searchServiceLogger = _loggerFactory.CreateLogger<FileBasedSearchService>();

        // Create search service
        var searchService = new FileBasedSearchService(
            repository,
            searchServiceLogger,
            tenantBaseDirectory);

        // Cache and return (GetOrAdd ensures atomicity if multiple threads race)
        var finalSearchService = _searchServiceCache.GetOrAdd(tenantId, searchService);

        if (ReferenceEquals(finalSearchService, searchService))
        {
            _logger.LogInformation(
                "Created search service for tenant {TenantId} ({DisplayName}) at {BaseDirectory}",
                tenantId,
                tenantConfig.DisplayName,
                tenantBaseDirectory);
        }
        else
        {
            _logger.LogTrace(
                "Another thread already created search service for tenant {TenantId}, using existing instance",
                tenantId);
        }

        return finalSearchService;
    }
}
