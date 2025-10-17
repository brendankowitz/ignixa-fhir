// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.DataLayer.InMemoryIndex;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Api.Services;

/// <summary>
/// Background service that loads resource metadata on startup to populate the in-memory index.
/// Ensures that resources persisted to disk are available after server restart (F5 developer experience).
/// Multi-Tenancy: Loads metadata from all active tenants (including system partition).
/// </summary>
public class IndexLoaderService : IHostedService
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
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IndexLoaderService starting - scanning metadata files from all tenants...");

        var stopwatch = Stopwatch.StartNew();
        int totalResourceCount = 0;
        int totalErrorCount = 0;
        int tenantCount = 0;

        try
        {
            // Get all tenant configurations (includes system partition and all active tenants)
            var allTenants = await _tenantStore.GetAllTenantsAsync(cancellationToken);

            // Also load system partition (Partition 0) even though it's filtered from GetAllTenantsAsync
            var systemPartition = await _tenantStore.GetTenantConfigurationAsync(0, cancellationToken);
            var allConfigs = systemPartition != null
                ? new[] { systemPartition }.Concat(allTenants).ToList()
                : allTenants.ToList();

            _logger.LogInformation("Loading metadata from {TenantCount} partition(s)", allConfigs.Count);

            foreach (var tenantConfig in allConfigs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
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
                        cancellationToken);

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
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            // Read and parse metadata
                            string metadataJson = await File.ReadAllTextAsync(metadataFile, cancellationToken).ConfigureAwait(false);
                            var metadata = JsonSerializer.Deserialize<ResourceMetadataDto>(metadataJson);

                            if (metadata != null && !string.IsNullOrEmpty(metadata.ResourceType) && !string.IsNullOrEmpty(metadata.ResourceId))
                            {
                                // Add to index
                                var key = new ResourceKey(metadata.ResourceType, metadata.ResourceId, metadata.VersionId);
                                await _index.AddAsync(key, FileBasedFhirRepository.DataLayerName, cancellationToken).ConfigureAwait(false);

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
            throw;
        }
    }

    /// <summary>
    /// No-op for shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IndexLoaderService stopping");
        return Task.CompletedTask;
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
