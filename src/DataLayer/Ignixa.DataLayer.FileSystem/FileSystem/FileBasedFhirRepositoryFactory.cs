// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.DataLayer.FileSystem.FileSystem;

/// <summary>
/// Factory for creating and caching FileSystem-based FHIR repositories per tenant.
/// Implements O(1) repository lookup after first access via ConcurrentDictionary caching.
/// </summary>
public partial class FileBasedFhirRepositoryFactory : IFhirRepositoryFactory
{
    private readonly ITenantConfigurationStore _configStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileBasedFhirRepositoryFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly ConcurrentDictionary<int, IFhirRepository> _repositoryCache;

    public FileBasedFhirRepositoryFactory(
        ITenantConfigurationStore configStore,
        IConfiguration configuration,
        ILogger<FileBasedFhirRepositoryFactory> logger,
        ILoggerFactory loggerFactory,
        RecyclableMemoryStreamManager memoryStreamManager)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _memoryStreamManager = memoryStreamManager ?? throw new ArgumentNullException(nameof(memoryStreamManager));
        _repositoryCache = new ConcurrentDictionary<int, IFhirRepository>();
    }

    public async Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default)
    {
        // Check cache first (O(1) lookup after first access)
        if (_repositoryCache.TryGetValue(tenantId, out var cachedRepository))
        {
            LogCacheHit(_logger, tenantId);
            return cachedRepository;
        }

        LogCacheMiss(_logger, tenantId);

        // Load tenant configuration
        var tenantConfig = await _configStore.GetTenantConfigurationAsync(tenantId, ct);
        if (tenantConfig == null)
        {
            LogTenantNotFound(_logger, tenantId);
            throw new InvalidOperationException($"Tenant {tenantId} not found or inactive");
        }

        // Validate storage type
        if (!string.Equals(tenantConfig.Storage.Type, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            LogUnsupportedStorageType(_logger, tenantConfig.Storage.Type, tenantId);
            throw new NotSupportedException(
                $"Storage type '{tenantConfig.Storage.Type}' not supported by FileBasedFhirRepositoryFactory. " +
                $"Only 'FileSystem' storage is supported.");
        }

        // Build base directory for tenant
        string globalBaseDirectory = _configuration["FhirRepository:BaseDirectory"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "fhir-data");

        string tenantBaseDirectory = !string.IsNullOrEmpty(tenantConfig.Storage.BaseDirectory)
            ? Path.Combine(globalBaseDirectory, tenantConfig.Storage.BaseDirectory)
            : Path.Combine(globalBaseDirectory, "tenants", tenantId.ToString());

        // Create tenant-specific logger
        var repositoryLogger = _loggerFactory.CreateLogger<FileBasedFhirRepository>();

        // Create repository
        var repository = new FileBasedFhirRepository(
            tenantBaseDirectory,
            repositoryLogger,
            _memoryStreamManager);

        // Cache and return (GetOrAdd ensures atomicity if multiple threads race)
        var finalRepository = _repositoryCache.GetOrAdd(tenantId, repository);

        if (ReferenceEquals(finalRepository, repository))
        {
            LogRepositoryCreated(_logger, tenantId, tenantConfig.DisplayName, tenantBaseDirectory);
        }
        else
        {
            LogCacheRace(_logger, tenantId);
        }

        return finalRepository;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Trace, Message = "Repository cache hit for tenant {TenantId}")]
    private static partial void LogCacheHit(ILogger logger, int tenantId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Repository cache miss for tenant {TenantId}, creating new repository")]
    private static partial void LogCacheMiss(ILogger logger, int tenantId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Tenant {TenantId} not found or inactive")]
    private static partial void LogTenantNotFound(ILogger logger, int tenantId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Storage type '{StorageType}' not supported by FileBasedFhirRepositoryFactory for tenant {TenantId}")]
    private static partial void LogUnsupportedStorageType(ILogger logger, string? storageType, int tenantId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Created repository for tenant {TenantId} ({DisplayName}) at {BaseDirectory}")]
    private static partial void LogRepositoryCreated(ILogger logger, int tenantId, string? displayName, string baseDirectory);

    [LoggerMessage(EventId = 6, Level = LogLevel.Trace, Message = "Another thread already created repository for tenant {TenantId}, using existing instance")]
    private static partial void LogCacheRace(ILogger logger, int tenantId);
}
