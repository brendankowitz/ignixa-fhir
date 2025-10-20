// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Factory for creating tenant-specific SqlEntityFrameworkRepository instances.
/// Implements caching to provide O(1) repository lookup after first access.
/// Each tenant gets its own DbContext with a dedicated connection string.
/// </summary>
public class SqlEntityFrameworkRepositoryFactory : IFhirRepositoryFactory, ISearchServiceFactory
{
    private readonly ITenantConfigurationStore _tenantStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<int, TenantServices> _servicesCache;

    /// <summary>
    /// Container for tenant-specific services that share a DbContext.
    /// </summary>
    private class TenantServices
    {
        public required FhirDbContext DbContext { get; init; }
        public required IFhirRepository Repository { get; init; }
        public required ISearchService SearchService { get; init; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlEntityFrameworkRepositoryFactory"/> class.
    /// </summary>
    /// <param name="tenantStore">The tenant configuration store.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public SqlEntityFrameworkRepositoryFactory(
        ITenantConfigurationStore tenantStore,
        ILoggerFactory loggerFactory)
    {
        _tenantStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _servicesCache = new ConcurrentDictionary<int, TenantServices>();
    }

    /// <inheritdoc/>
    public async Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default)
    {
        var services = await GetOrCreateServicesAsync(tenantId, ct);
        return services.Repository;
    }

    /// <inheritdoc/>
    public async Task<ISearchService> GetSearchServiceAsync(int tenantId, CancellationToken ct = default)
    {
        var services = await GetOrCreateServicesAsync(tenantId, ct);
        return services.SearchService;
    }

    private async Task<TenantServices> GetOrCreateServicesAsync(int tenantId, CancellationToken ct)
    {
        // Check cache first
        if (_servicesCache.TryGetValue(tenantId, out var cachedServices))
        {
            return cachedServices;
        }

        // Get tenant configuration
        var tenantConfig = await _tenantStore.GetTenantConfigurationAsync(tenantId, ct);

        if (tenantConfig == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} does not exist");
        }

        if (!tenantConfig.IsActive)
        {
            throw new InvalidOperationException($"Tenant {tenantId} is not active");
        }

        // Prevent access to system partition (Partition 0)
        if (tenantConfig.IsSystemPartition || tenantId == SystemConstants.SystemPartitionId)
        {
            throw new InvalidOperationException($"Tenant {tenantId} is a system partition and cannot be accessed directly");
        }

        // Validate storage configuration
        if (tenantConfig.Storage.Type != "SqlEntityFramework" && tenantConfig.Storage.Type != "SqlServer")
        {
            throw new InvalidOperationException($"Tenant {tenantId} storage type '{tenantConfig.Storage.Type}' is not supported by SqlEntityFrameworkRepositoryFactory. Expected 'SqlEntityFramework' or 'SqlServer'");
        }

        if (string.IsNullOrEmpty(tenantConfig.Storage.ConnectionString))
        {
            throw new InvalidOperationException($"Tenant {tenantId} is missing a connection string in Storage.ConnectionString");
        }

        // Create services and cache them
        var services = _servicesCache.GetOrAdd(tenantId, _ => CreateServices(tenantId, tenantConfig));

        return services;
    }

    private TenantServices CreateServices(int tenantId, Domain.Models.TenantConfiguration tenantConfig)
    {
        var logger = _loggerFactory.CreateLogger<SqlEntityFrameworkRepositoryFactory>();
        logger.LogInformation("Creating services for tenant {TenantId} ({DisplayName})", tenantId, tenantConfig.DisplayName);

        // Create DbContext with tenant-specific connection string
        var optionsBuilder = new DbContextOptionsBuilder<FhirDbContext>();
        optionsBuilder.UseSqlServer(
            tenantConfig.Storage.ConnectionString,
            sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                sqlOptions.CommandTimeout(30);
            });

        // Enable sensitive data logging in development (optional)
        // optionsBuilder.EnableSensitiveDataLogging();

        var dbContext = new FhirDbContext(optionsBuilder.Options);

        // Create shared dependencies
        var compressor = new GzipResourceCompressor();
        var searchIndexCache = new SearchIndexReferenceDataCache(
            dbContext,
            _loggerFactory.CreateLogger<SearchIndexReferenceDataCache>());
        var searchIndexWriter = new SearchIndexWriter(
            dbContext,
            searchIndexCache,
            _loggerFactory.CreateLogger<SearchIndexWriter>());

        // Create repository
        var repository = new SqlEntityFrameworkRepository(
            dbContext,
            compressor,
            searchIndexWriter,
            _loggerFactory.CreateLogger<SqlEntityFrameworkRepository>());

        // Create search service components
        var parameterQueryGenerator = new Search.SearchParameterQueryGenerator(
            dbContext,
            searchIndexCache,
            _loggerFactory.CreateLogger<Search.SearchParameterQueryGenerator>());

        var chainedExpressionProcessor = new Search.ChainedExpressionProcessor(
            dbContext,
            searchIndexCache,
            parameterQueryGenerator,
            _loggerFactory.CreateLogger<Search.ChainedExpressionProcessor>());

        var queryBuilder = new Search.SearchExpressionQueryBuilder(
            dbContext,
            parameterQueryGenerator,
            chainedExpressionProcessor,
            _loggerFactory.CreateLogger<Search.SearchExpressionQueryBuilder>());

        var includeProcessor = new Search.IncludeProcessor(
            dbContext,
            searchIndexCache,
            repository,
            _loggerFactory.CreateLogger<Search.IncludeProcessor>());

        var revIncludeProcessor = new Search.RevIncludeProcessor(
            dbContext,
            searchIndexCache,
            repository,
            _loggerFactory.CreateLogger<Search.RevIncludeProcessor>());

        var iterateProcessor = new Search.IterateProcessor(
            includeProcessor,
            revIncludeProcessor,
            _loggerFactory.CreateLogger<Search.IterateProcessor>());

        var searchService = new Search.SqlEntityFrameworkSearchService(
            dbContext,
            repository,
            queryBuilder,
            includeProcessor,
            revIncludeProcessor,
            iterateProcessor,
            _loggerFactory.CreateLogger<Search.SqlEntityFrameworkSearchService>());

        logger.LogInformation("Successfully created services for tenant {TenantId}", tenantId);

        return new TenantServices
        {
            DbContext = dbContext,
            Repository = repository,
            SearchService = searchService
        };
    }

    /// <summary>
    /// Clears the services cache. Useful for testing or when tenant configurations change.
    /// </summary>
    public void ClearCache()
    {
        _servicesCache.Clear();
    }

    /// <summary>
    /// Gets the current number of cached tenant service sets.
    /// </summary>
    public int CachedServicesCount => _servicesCache.Count;
}
