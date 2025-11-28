// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.DataLayer.InMemoryIndex;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Api.Services;

/// <summary>
/// Background service that loads resource metadata on startup to populate the in-memory index.
/// Ensures that resources persisted to disk are available after server restart (F5 developer experience).
/// Multi-Tenancy: Loads metadata from all active tenants (including system partition).
///
/// IMPORTANT: Uses BackgroundService to avoid blocking web server startup.
/// The web server starts immediately while metadata loading happens in the background.
/// </summary>
public class IndexLoaderService : BackgroundService
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ITenantConfigurationStore _tenantStore;
    private readonly IResourceLocationIndex _index;
    private readonly ILogger<IndexLoaderService> _logger;

    public IndexLoaderService(
        IFhirRepositoryFactory repositoryFactory,
        ITenantConfigurationStore tenantStore,
        IResourceLocationIndex index,
        ILogger<IndexLoaderService> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _tenantStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Scans all metadata files from all tenants and populates the resource location index.
    /// Multi-Tenancy: Iterates over all configured tenants (including system partition).
    /// Runs in the background without blocking web server startup.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IndexLoaderService starting - scanning metadata files from all tenants...");

        var stopwatch = Stopwatch.StartNew();
        int totalResourceCount = 0;
        int totalErrorCount = 0;
        int tenantCount = 0;

        try
        {
            // Get all tenant configurations (includes system partition and all active tenants)
            var allTenants = await _tenantStore.GetAllTenantsAsync(stoppingToken);

            _logger.LogInformation("Loading metadata from {TenantCount} tenant configuration(s)", allTenants.Count);

            foreach (var tenantConfig in allTenants)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                // Skip system partition (Tenant 0) - it cannot be accessed directly
                if (tenantConfig.IsSystemPartition)
                {
                    _logger.LogDebug(
                        "Skipping Tenant {TenantId} ({DisplayName}) - system partition cannot be accessed directly",
                        tenantConfig.TenantId,
                        tenantConfig.DisplayName);
                    continue;
                }

                tenantCount++;
                int tenantResourceCount = 0;
                int tenantErrorCount = 0;

                try
                {
                    _logger.LogDebug(
                        "Loading metadata from Tenant {TenantId} ({DisplayName})...",
                        tenantConfig.TenantId,
                        tenantConfig.DisplayName);

                    // Get repository for this tenant
                    var repository = await _repositoryFactory.GetRepositoryAsync(
                        tenantConfig.TenantId,
                        stoppingToken);

                    // Cast to FileBasedFhirRepository to access GetAllMetadataFiles
                    if (repository is not FileBasedFhirRepository fileRepository)
                    {
                        _logger.LogWarning(
                            "Tenant {TenantId} repository is not FileBasedFhirRepository (type: {Type}). Skipping metadata loading.",
                            tenantConfig.TenantId,
                            repository.GetType().Name);
                        continue;
                    }

                    // Get all metadata files from this tenant's repository
                    var metadataFiles = fileRepository.GetAllMetadataFiles();

                    foreach (var metadataFile in metadataFiles)
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            // Read and parse metadata
                            string metadataJson = await File.ReadAllTextAsync(metadataFile, stoppingToken).ConfigureAwait(false);
                            var metadata = JsonSerializer.Deserialize<ResourceMetadataDto>(metadataJson);

                            if (metadata != null && !string.IsNullOrEmpty(metadata.ResourceType) && !string.IsNullOrEmpty(metadata.ResourceId))
                            {
                                // Add to index
                                var key = new ResourceKey(metadata.ResourceType, metadata.ResourceId, metadata.VersionId);
                                await _index.AddAsync(key, FileBasedFhirRepository.DataLayerName, stoppingToken).ConfigureAwait(false);

                                tenantResourceCount++;
                                totalResourceCount++;

                                if (totalResourceCount % 100 == 0)
                                {
                                    _logger.LogDebug("Loaded {Count} resources...", totalResourceCount);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load metadata from {File}", metadataFile);
                            tenantErrorCount++;
                            totalErrorCount++;
                        }
                    }

                    _logger.LogDebug(
                        "Tenant {TenantId}: Loaded {ResourceCount} resources ({ErrorCount} errors)",
                        tenantConfig.TenantId,
                        tenantResourceCount,
                        tenantErrorCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to load metadata from Tenant {TenantId} ({DisplayName})",
                        tenantConfig.TenantId,
                        tenantConfig.DisplayName);
                    totalErrorCount++;
                }
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "IndexLoaderService completed: Loaded {ResourceCount} resources from {TenantCount} partition(s) in {ElapsedMs:N0}ms ({ErrorCount} errors)",
                totalResourceCount,
                tenantCount,
                stopwatch.ElapsedMilliseconds,
                totalErrorCount);

            // Log performance warning if slow (target: <3ms per resource)
            if (totalResourceCount > 0)
            {
                double msPerResource = (double)stopwatch.ElapsedMilliseconds / totalResourceCount;
                if (msPerResource > 3.0)
                {
                    _logger.LogWarning(
                        "IndexLoaderService performance is slow: {MsPerResource:N2}ms per resource (target: <3ms)",
                        msPerResource);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IndexLoaderService failed during startup");
            // Don't throw - allow web server to continue running even if index loading fails
            // This ensures the FHIR server remains available for SQL-based tenants
        }
    }

    /// <summary>
    /// DTO for deserializing metadata files.
    /// </summary>
    private class ResourceMetadataDto
    {
        public string ResourceType { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string VersionId { get; set; } = "1";
        public DateTimeOffset LastModified { get; set; }
    }
}
