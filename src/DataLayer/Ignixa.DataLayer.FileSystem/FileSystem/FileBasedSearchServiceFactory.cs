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
public partial class FileBasedSearchServiceFactory : ISearchServiceFactory
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
            LogCacheHit(_logger, tenantId);
            return cachedSearchService;
        }

        LogCacheMiss(_logger, tenantId);

        // Load tenant configuration
        var tenantConfig = await _configStore.GetTenantConfigurationAsync(tenantId, ct);
        if (tenantConfig == null)
        {
            LogTenantNotFound(_logger, tenantId);
            throw new InvalidOperationException($"Tenant {tenantId} not found or inactive");
        }

        // Validate search type
        if (!string.Equals(tenantConfig.Search.Type, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            LogUnsupportedSearchType(_logger, tenantConfig.Search.Type, tenantId);
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
            LogSearchServiceCreated(_logger, tenantId, tenantConfig.DisplayName, tenantBaseDirectory);
        }
        else
        {
            LogCacheRace(_logger, tenantId);
        }

        return finalSearchService;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Trace, Message = "Search service cache hit for tenant {TenantId}")]
    private static partial void LogCacheHit(ILogger logger, int tenantId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Search service cache miss for tenant {TenantId}, creating new search service")]
    private static partial void LogCacheMiss(ILogger logger, int tenantId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Tenant {TenantId} not found or inactive")]
    private static partial void LogTenantNotFound(ILogger logger, int tenantId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Search type '{SearchType}' not supported by FileBasedSearchServiceFactory for tenant {TenantId}")]
    private static partial void LogUnsupportedSearchType(ILogger logger, string? searchType, int tenantId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Created search service for tenant {TenantId} ({DisplayName}) at {BaseDirectory}")]
    private static partial void LogSearchServiceCreated(ILogger logger, int tenantId, string? displayName, string baseDirectory);

    [LoggerMessage(EventId = 6, Level = LogLevel.Trace, Message = "Another thread already created search service for tenant {TenantId}, using existing instance")]
    private static partial void LogCacheRace(ILogger logger, int tenantId);
}
