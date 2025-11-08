// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement;

/// <summary>
/// Entity Framework Core implementation of IPackageResourceRepository for SQL Server.
/// Provides storage and retrieval of FHIR conformance resources from NPM packages (IGs).
/// Supports multi-version package loading, semantic version resolution, and canonical URL lookups.
/// </summary>
public class SqlPackageResourceRepository : IPackageResourceRepository
{
    private readonly FhirDbContext _dbContext;
    private readonly ILogger<SqlPackageResourceRepository> _logger;

    public SqlPackageResourceRepository(
        FhirDbContext dbContext,
        ILogger<SqlPackageResourceRepository> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task UpsertAsync(PackageResource packageResource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        // Check if resource already exists (by unique constraint: PackageId + PackageVersion + Canonical)
        var existing = await _dbContext.PackageResources
            .FirstOrDefaultAsync(
                pr => pr.PackageId == packageResource.PackageId
                    && pr.PackageVersion == packageResource.PackageVersion
                    && pr.Canonical == packageResource.Canonical,
                cancellationToken);

        if (existing != null)
        {
            // Update existing resource
            UpdateEntityFromModel(existing, packageResource);
            _logger.LogDebug(
                "Updating package resource {Canonical} from package {PackageId}@{PackageVersion}",
                packageResource.Canonical,
                packageResource.PackageId,
                packageResource.PackageVersion);
        }
        else
        {
            // Insert new resource
            var entity = MapModelToEntity(packageResource);
            _dbContext.PackageResources.Add(entity);
            _logger.LogDebug(
                "Inserting package resource {Canonical} from package {PackageId}@{PackageVersion}",
                packageResource.Canonical,
                packageResource.PackageId,
                packageResource.PackageVersion);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Error upserting package resource {Canonical} from package {PackageId}@{PackageVersion}",
                packageResource.Canonical,
                packageResource.PackageId,
                packageResource.PackageVersion);
            throw;
        }
    }

    public async Task BatchUpsertAsync(
        IReadOnlyList<PackageResource> packageResources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResources);

        if (packageResources.Count == 0)
        {
            return;
        }

        // Group by package for logging
        var firstResource = packageResources[0];
        var packageId = firstResource.PackageId;
        var packageVersion = firstResource.PackageVersion;

        _logger.LogInformation(
            "Batch upserting {Count} resources from package {PackageId}@{PackageVersion}",
            packageResources.Count,
            packageId,
            packageVersion);

        // Load existing resources for this package version
        var canonicals = packageResources.Select(pr => pr.Canonical).ToHashSet();
        var existingResources = await _dbContext.PackageResources
            .Where(pr => pr.PackageId == packageId
                && pr.PackageVersion == packageVersion
                && canonicals.Contains(pr.Canonical))
            .ToListAsync(cancellationToken);

        var existingDict = existingResources
            .ToDictionary(pr => pr.Canonical, StringComparer.Ordinal);

        foreach (var packageResource in packageResources)
        {
            if (existingDict.TryGetValue(packageResource.Canonical, out var existing))
            {
                // Update existing
                UpdateEntityFromModel(existing, packageResource);
            }
            else
            {
                // Insert new
                var entity = MapModelToEntity(packageResource);
                _dbContext.PackageResources.Add(entity);
            }
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Successfully upserted {Count} resources from package {PackageId}@{PackageVersion}",
                packageResources.Count,
                packageId,
                packageVersion);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Error batch upserting {Count} resources from package {PackageId}@{PackageVersion}",
                packageResources.Count,
                packageId,
                packageVersion);
            throw;
        }
    }

    public async Task<PackageResource?> GetByCanonicalAsync(
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        var query = _dbContext.PackageResources
            .Where(pr => pr.Canonical == canonical && pr.IsActive);

        if (!string.IsNullOrEmpty(version))
        {
            query = query.Where(pr => pr.Version == version);
        }

        var entity = await query.FirstOrDefaultAsync(cancellationToken);

        return entity != null ? MapEntityToModel(entity) : null;
    }

    public async Task<PackageResource?> GetFromPackageAsync(
        string packageId,
        string packageVersion,
        string canonical,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        var entity = await _dbContext.PackageResources
            .FirstOrDefaultAsync(
                pr => pr.PackageId == packageId
                    && pr.PackageVersion == packageVersion
                    && pr.Canonical == canonical
                    && pr.IsActive,
                cancellationToken);

        return entity != null ? MapEntityToModel(entity) : null;
    }

    public async Task<PackageResource?> GetLatestByCanonicalAsync(
        string canonical,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        // Query for all active versions of this canonical
        var query = _dbContext.PackageResources
            .Where(pr => pr.Canonical == canonical && pr.IsActive);

        if (!string.IsNullOrEmpty(resourceType))
        {
            query = query.Where(pr => pr.ResourceType == resourceType);
        }

        // Order by semantic version (MAJOR.MINOR.PATCH) descending
        // Note: This uses SQL Server PARSENAME function to parse semantic versions
        var entity = await query
            .OrderByDescending(pr => pr.PackageVersion)
            .FirstOrDefaultAsync(cancellationToken);

        return entity != null ? MapEntityToModel(entity) : null;
    }

    public async Task<IReadOnlyList<PackageResource>> ListPackageResourcesAsync(
        string packageId,
        string packageVersion,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        var query = _dbContext.PackageResources
            .Where(pr => pr.PackageId == packageId
                && pr.PackageVersion == packageVersion
                && pr.IsActive);

        if (!string.IsNullOrEmpty(resourceType))
        {
            query = query.Where(pr => pr.ResourceType == resourceType);
        }

        var entities = await query
            .OrderBy(pr => pr.ResourceType)
            .ThenBy(pr => pr.Canonical)
            .ToListAsync(cancellationToken);

        return entities.Select(MapEntityToModel).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<(string PackageId, string PackageVersion)>> ListLoadedPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        var packages = await _dbContext.PackageResources
            .Where(pr => pr.IsActive)
            .Select(pr => new { pr.PackageId, pr.PackageVersion })
            .Distinct()
            .OrderBy(p => p.PackageId)
            .ThenBy(p => p.PackageVersion)
            .ToListAsync(cancellationToken);

        return packages
            .Select(p => (p.PackageId, p.PackageVersion))
            .ToList()
            .AsReadOnly();
    }

    public async Task<int> DeactivatePackageAsync(
        string packageId,
        string packageVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        var count = await _dbContext.PackageResources
            .Where(pr => pr.PackageId == packageId
                && pr.PackageVersion == packageVersion
                && pr.IsActive)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(pr => pr.IsActive, false),
                cancellationToken);

        _logger.LogInformation(
            "Deactivated {Count} resources from package {PackageId}@{PackageVersion}",
            count,
            packageId,
            packageVersion);

        return count;
    }

    public async Task<int> ReactivatePackageAsync(
        string packageId,
        string packageVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        var count = await _dbContext.PackageResources
            .Where(pr => pr.PackageId == packageId
                && pr.PackageVersion == packageVersion
                && !pr.IsActive)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(pr => pr.IsActive, true),
                cancellationToken);

        _logger.LogInformation(
            "Reactivated {Count} resources from package {PackageId}@{PackageVersion}",
            count,
            packageId,
            packageVersion);

        return count;
    }

    public async Task<int> DeletePackageAsync(
        string packageId,
        string packageVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        var count = await _dbContext.PackageResources
            .Where(pr => pr.PackageId == packageId && pr.PackageVersion == packageVersion)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogWarning(
            "Permanently deleted {Count} resources from package {PackageId}@{PackageVersion}",
            count,
            packageId,
            packageVersion);

        return count;
    }

    /// <summary>
    /// Maps database entity to domain model.
    /// </summary>
    private static PackageResource MapEntityToModel(PackageResourceEntity entity)
    {
        return new PackageResource
        {
            PackageResourceId = entity.PackageResourceId,
            PackageId = entity.PackageId,
            PackageVersion = entity.PackageVersion,
            ResourceType = entity.ResourceType,
            Canonical = entity.Canonical,
            Version = entity.Version,
            ResourceId = entity.ResourceId,
            ResourceJson = entity.ResourceJson,
            FhirVersion = entity.FhirVersion,
            LoadedDate = entity.LoadedDate,
            IsActive = entity.IsActive
        };
    }

    /// <summary>
    /// Maps domain model to database entity.
    /// </summary>
    private static PackageResourceEntity MapModelToEntity(PackageResource model)
    {
        return new PackageResourceEntity
        {
            PackageResourceId = model.PackageResourceId,
            PackageId = model.PackageId,
            PackageVersion = model.PackageVersion,
            ResourceType = model.ResourceType,
            Canonical = model.Canonical,
            Version = model.Version,
            ResourceId = model.ResourceId,
            ResourceJson = model.ResourceJson,
            FhirVersion = model.FhirVersion,
            LoadedDate = model.LoadedDate,
            IsActive = model.IsActive
        };
    }

    /// <summary>
    /// Updates entity properties from model without touching the primary key.
    /// </summary>
    private static void UpdateEntityFromModel(PackageResourceEntity entity, PackageResource model)
    {
        entity.ResourceType = model.ResourceType;
        entity.Version = model.Version;
        entity.ResourceId = model.ResourceId;
        entity.ResourceJson = model.ResourceJson;
        entity.FhirVersion = model.FhirVersion;
        entity.LoadedDate = model.LoadedDate;
        entity.IsActive = model.IsActive;
    }
}
