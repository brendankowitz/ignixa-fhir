using System.Diagnostics;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Imports extracted package resources to the database.
/// </summary>
public class PackageResourceImporter : IPackageResourceImporter
{
    private readonly ILogger<PackageResourceImporter> _logger;

    /// <summary>
    /// Initializes a new instance of the PackageResourceImporter class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public PackageResourceImporter(
        ILogger<PackageResourceImporter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Imports extracted resources to the PackageResource table.
    /// </summary>
    /// <param name="extraction">Extracted resources and manifest</param>
    /// <param name="repository">Package resource repository</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with statistics</returns>
    public async Task<PackageImportResult> ImportAsync(
        PackageExtractionResult extraction,
        IPackageResourceRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(repository);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting import of {Count} resources from package {PackageId}@{Version}",
            extraction.Resources.Count, extraction.Manifest.Name, extraction.Manifest.Version);

        try
        {
            // Map extracted resources to domain models
            var packageResources = extraction.Resources
                .Select(r => new PackageResource
                {
                    PackageId = extraction.Manifest.Name,
                    PackageVersion = extraction.Manifest.Version,
                    ResourceType = r.ResourceType,
                    Canonical = r.Canonical,
                    Version = r.Version,
                    ResourceId = r.ResourceId,
                    ResourceJson = r.ResourceJson,
                    FhirVersion = r.FhirVersion,
                    LoadedDate = DateTimeOffset.UtcNow,
                    IsActive = true
                })
                .ToList();

            _logger.LogDebug(
                "Mapped {Count} extracted resources to domain models",
                packageResources.Count);

            // Deduplicate by (ResourceType, ResourceId) - packages may contain the same resource in multiple files
            var deduplicated = packageResources
                .GroupBy(r => new { r.ResourceType, r.ResourceId })
                .Select(g =>
                {
                    if (g.Count() > 1)
                    {
                        _logger.LogWarning(
                            "Found {Count} duplicate resources for {ResourceType}/{ResourceId} in package {PackageId}@{Version}. Using first occurrence.",
                            g.Count(), g.Key.ResourceType, g.Key.ResourceId,
                            extraction.Manifest.Name, extraction.Manifest.Version);
                    }
                    return g.First();
                })
                .ToList();

            _logger.LogDebug(
                "After deduplication: {Count} unique resources (removed {Duplicates} duplicates)",
                deduplicated.Count, packageResources.Count - deduplicated.Count);

            packageResources = deduplicated;

            // Count by resource type for reporting
            var resourcesByType = new Dictionary<string, int>();
            foreach (var group in extraction.Resources.GroupBy(r => r.ResourceType))
            {
                resourcesByType[group.Key] = group.Count();
            }

            // Batch insert to database
            await BatchUpsertResourcesAsync(packageResources, repository, cancellationToken);

            stopwatch.Stop();

            var result = new PackageImportResult
            {
                PackageId = extraction.Manifest.Name,
                PackageVersion = extraction.Manifest.Version,
                TotalResources = extraction.Resources.Count,
                ImportedResources = extraction.Resources.Count,
                UpdatedResources = 0, // Note: Repository uses upsert, so we can't easily distinguish
                Duration = stopwatch.Elapsed,
                ResourcesByType = resourcesByType
            };

            _logger.LogInformation(
                "Package import completed successfully. " +
                "PackageId: {PackageId}@{Version}, Resources: {Count}, Duration: {Duration}ms",
                result.PackageId, result.PackageVersion, result.TotalResources,
                result.Duration.TotalMilliseconds);

            // Log breakdown by resource type
            foreach (var kvp in result.ResourcesByType.OrderByDescending(k => k.Value))
            {
                _logger.LogDebug(
                    "Imported {Count} {ResourceType} resources",
                    kvp.Value, kvp.Key);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Failed to import package {PackageId}@{Version} after {Duration}ms",
                extraction.Manifest.Name, extraction.Manifest.Version, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Batch upserts resources to the repository.
    /// Uses chunking to handle large packages while keeping transactions manageable.
    /// </summary>
    private async Task BatchUpsertResourcesAsync(
        IReadOnlyList<PackageResource> packageResources,
        IPackageResourceRepository repository,
        CancellationToken cancellationToken)
    {
        const int ChunkSize = 500; // Process 500 resources per transaction

        if (packageResources.Count <= ChunkSize)
        {
            // Small package - single transaction
            _logger.LogDebug("Batch upserting {Count} resources in single transaction", packageResources.Count);
            await repository.BatchUpsertAsync(packageResources, cancellationToken);
            return;
        }

        // Large package - chunk into multiple transactions
        _logger.LogInformation(
            "Batch upserting {Total} resources in {Chunks} chunks of {ChunkSize}",
            packageResources.Count, Math.Ceiling((double)packageResources.Count / ChunkSize), ChunkSize);

        for (int i = 0; i < packageResources.Count; i += ChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = packageResources.Skip(i).Take(ChunkSize).ToList();
            var chunkNumber = (i / ChunkSize) + 1;
            var totalChunks = (packageResources.Count + ChunkSize - 1) / ChunkSize;

            _logger.LogDebug(
                "Upserting chunk {ChunkNumber}/{TotalChunks} ({ResourceCount} resources)",
                chunkNumber, totalChunks, chunk.Count);

            await repository.BatchUpsertAsync(chunk, cancellationToken);

            _logger.LogDebug(
                "Chunk {ChunkNumber}/{TotalChunks} completed ({ProgressPercent}%)",
                chunkNumber, totalChunks, (chunkNumber * 100) / totalChunks);
        }

        _logger.LogInformation("All {Count} resources upserted successfully", packageResources.Count);
    }
}
