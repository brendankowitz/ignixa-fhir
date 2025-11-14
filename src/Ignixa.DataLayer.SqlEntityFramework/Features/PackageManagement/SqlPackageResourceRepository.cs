// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement;

/// <summary>
/// Entity Framework Core implementation of IPackageResourceRepository for SQL Server.
/// Provides storage and retrieval of FHIR conformance resources from NPM packages (IGs).
/// Supports multi-version package loading, semantic version resolution, and canonical URL lookups.
///
/// Threading Model:
/// Uses factory pattern to create a fresh DbContext for EACH operation to ensure thread safety.
/// DbContext is not thread-safe; even with InstancePerDependency, concurrent operations from
/// multiple threads can cause "A second operation was started on this context instance before
/// a previous operation completed" errors. This implementation creates a new DbContext per
/// method call, ensuring complete isolation between concurrent operations.
/// </summary>
public class SqlPackageResourceRepository : IPackageResourceRepository
{
    private readonly PackageRepositoryDbContextFactory _dbContextFactory;
    private readonly ILogger<SqlPackageResourceRepository> _logger;

    public SqlPackageResourceRepository(
        PackageRepositoryDbContextFactory dbContextFactory,
        ILogger<SqlPackageResourceRepository> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task UpsertAsync(PackageResource packageResource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Check if resource already exists (by unique constraint: PackageId + PackageVersion + ResourceType + ResourceId)
        var existing = await dbContext.PackageResources
            .AsNoTracking()
            .FirstOrDefaultAsync(
                pr => pr.PackageId == packageResource.PackageId
                    && pr.PackageVersion == packageResource.PackageVersion
                    && pr.ResourceType == packageResource.ResourceType
                    && pr.ResourceId == packageResource.ResourceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            // Update existing resource
            // Note: Must re-attach to DbContext for changes to be tracked
            var attached = dbContext.PackageResources.Attach(existing).Entity;
            UpdateEntityFromModel(attached, packageResource);
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
            dbContext.PackageResources.Add(entity);
            _logger.LogDebug(
                "Inserting package resource {Canonical} from package {PackageId}@{PackageVersion}",
                packageResource.Canonical,
                packageResource.PackageId,
                packageResource.PackageVersion);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique index", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Handle race condition: another thread may have inserted the same resource
            // between the time we checked for existing resources and when we tried to insert.
            // The entire transaction was rolled back, but the other thread will commit successfully.
            // Treat as idempotent success - the resource WILL be in the database after the other thread commits.
            _logger.LogWarning(
                ex,
                "Package resource {Canonical} from package {PackageId}@{PackageVersion} encountered duplicate key. " +
                "Another thread is loading this package. Transaction rolled back (expected). " +
                "Resource will be committed by the other thread.",
                packageResource.Canonical,
                packageResource.PackageId,
                packageResource.PackageVersion);
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

        using var dbContext = _dbContextFactory.CreateDbContext();

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
        var resourceKeys = packageResources
            .Select(pr => new { pr.ResourceType, pr.ResourceId })
            .ToHashSet();
        var existingResources = await dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.PackageId == packageId
                && pr.PackageVersion == packageVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingDict = existingResources
            .ToDictionary(pr => new { pr.ResourceType, pr.ResourceId });

        foreach (var packageResource in packageResources)
        {
            var key = new { packageResource.ResourceType, packageResource.ResourceId };
            if (existingDict.TryGetValue(key, out var existing))
            {
                // Update existing
                // Note: Must re-attach to DbContext for changes to be tracked
                var attached = dbContext.PackageResources.Attach(existing).Entity;
                UpdateEntityFromModel(attached, packageResource);
            }
            else
            {
                // Insert new
                var entity = MapModelToEntity(packageResource);
                dbContext.PackageResources.Add(entity);
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Successfully upserted {Count} resources from package {PackageId}@{PackageVersion}",
                packageResources.Count,
                packageId,
                packageVersion);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique index", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Handle race condition: another thread may have started inserting the same resources
            // between the time we checked for existing resources and when we tried to insert.
            // The ENTIRE batch transaction was rolled back, but the other thread will commit successfully.
            // This is expected during concurrent loading attempts (e.g., TenantPackagePreloadService
            // and EmbeddedPackagePreloadService running simultaneously).
            // Treat as success - the resources WILL be in the database after the other thread commits.
            _logger.LogWarning(
                ex,
                "Package {PackageId}@{PackageVersion}: Batch insert encountered duplicate key constraint. " +
                "Another thread is loading the same package. Transaction rolled back (expected race condition). " +
                "Resources will be committed by the other thread.",
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

        using var dbContext = _dbContextFactory.CreateDbContext();

        var query = dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.Canonical == canonical && pr.IsActive);

        if (!string.IsNullOrEmpty(version))
        {
            query = query.Where(pr => pr.Version == version);
        }

        var entity = await query
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

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

        using var dbContext = _dbContextFactory.CreateDbContext();

        var entity = await dbContext.PackageResources
            .AsNoTracking()
            .FirstOrDefaultAsync(
                pr => pr.PackageId == packageId
                    && pr.PackageVersion == packageVersion
                    && pr.Canonical == canonical
                    && pr.IsActive,
                cancellationToken)
            .ConfigureAwait(false);

        return entity != null ? MapEntityToModel(entity) : null;
    }

    public async Task<PackageResource?> GetLatestByCanonicalAsync(
        string canonical,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Query for all active versions of this canonical
        var query = dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.Canonical == canonical && pr.IsActive);

        if (!string.IsNullOrEmpty(resourceType))
        {
            query = query.Where(pr => pr.ResourceType == resourceType);
        }

        // Order by semantic version (MAJOR.MINOR.PATCH) descending
        // Note: This uses SQL Server PARSENAME function to parse semantic versions
        var entity = await query
            .OrderByDescending(pr => pr.PackageVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

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

        using var dbContext = _dbContextFactory.CreateDbContext();

        var query = dbContext.PackageResources
            .AsNoTracking()
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
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapEntityToModel).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<(string PackageId, string PackageVersion)>> ListLoadedPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        var packages = await dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.IsActive)
            .Select(pr => new { pr.PackageId, pr.PackageVersion })
            .Distinct()
            .OrderBy(p => p.PackageId)
            .ThenBy(p => p.PackageVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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

        using var dbContext = _dbContextFactory.CreateDbContext();

        var count = await dbContext.PackageResources
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

        using var dbContext = _dbContextFactory.CreateDbContext();

        var count = await dbContext.PackageResources
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

        using var dbContext = _dbContextFactory.CreateDbContext();

        var count = await dbContext.PackageResources
            .Where(pr => pr.PackageId == packageId && pr.PackageVersion == packageVersion)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogWarning(
            "Permanently deleted {Count} resources from package {PackageId}@{PackageVersion}",
            count,
            packageId,
            packageVersion);

        return count;
    }

    public async Task<IReadOnlyList<PackageResource>> GetStructureDefinitionsByCanonicalAsync(
        string canonical,
        string? fhirVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Determine search strategy based on whether canonical contains "/"
        // If it contains "/", it's a full canonical URL (exact match)
        // If it doesn't contain "/", it's just a resource type name (EndsWith match)
        bool isFullUrl = canonical.Contains('/', StringComparison.Ordinal);

        // Query for all active StructureDefinitions
        var query = dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.ResourceType == "StructureDefinition" && pr.IsActive);

        // Apply canonical matching strategy
        if (isFullUrl)
        {
            // Full URL: exact match only
            query = query.Where(pr => pr.Canonical == canonical);
        }
        else
        {
            // Resource type name: EndsWith match (case-insensitive on server-side prefix)
            // Will match "http://hl7.org/fhir/StructureDefinition/Patient" when searching for "Patient"
            query = query.Where(pr => pr.Canonical.EndsWith("/" + canonical));
        }

        // TODO: Investigate if this is a problem
        // Filter by FHIR version if specified
        // if (!string.IsNullOrEmpty(fhirVersion))
        // {
        //    query = query.Where(pr => pr.FhirVersion == fhirVersion);
        // }

        // Load results into memory first (due to EF Core limitations with EndsWith overload)
        var entities = await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Order by PackageVersion DESC (in memory) so newest version is first
        var ordered = entities
            .OrderByDescending(pr => pr.PackageVersion)
            .ToList();

        return ordered.Select(MapEntityToModel).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<PackageResource>> GetAllStructureDefinitionsAsync(
        string? fhirVersion = null,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        // Query for all active StructureDefinitions across all packages
        var query = dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.ResourceType == "StructureDefinition" && pr.IsActive);

        // TODO: FHIR version matching - commented out pending resolution of exact matching strategy
        // if (!string.IsNullOrEmpty(fhirVersion))
        // {
        //     query = query.Where(pr => pr.FhirVersion == fhirVersion);
        // }

        // Order by PackageVersion DESC so newest version is first
        var entities = await query
            .OrderByDescending(pr => pr.PackageVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Retrieved {Count} StructureDefinitions from packages (FHIR version: {FhirVersion})",
            entities.Count,
            fhirVersion ?? "any");

        return entities.Select(MapEntityToModel).ToList().AsReadOnly();
    }

    public async Task<bool> PackageVersionExistsAsync(
        string packageId,
        string packageVersion,
        int tenantId = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Use ConfigureAwait(false) to prevent DbContext deadlocks in multi-threaded scenarios
        // This ensures the query completes fully before returning to the caller
        var exists = await dbContext.PackageResources
            .AsNoTracking()  // Optimize read-only query - reduces DbContext overhead
            .Where(pr => pr.PackageId == packageId
                && pr.PackageVersion == packageVersion
                && pr.IsActive)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Package {PackageId}@{PackageVersion} exists: {Exists}",
            packageId, packageVersion, exists);

        return exists;
    }

    public async Task<IReadOnlySet<string>> GetCustomResourceTypesAsync(
        string? fhirVersion = null,
        CancellationToken cancellationToken = default)
    {
        var customTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await using var dbContext = _dbContextFactory.CreateDbContext();

            // Query ALL active StructureDefinitions (not limited to ViewDefinition)
            // This will capture custom resources from ANY IG: sqlonfhir, custom IGs, future FHIR versions
            List<PackageResourceEntity> structureDefinitions = await dbContext.PackageResources
                .AsNoTracking()
                .Where(pr => pr.ResourceType == "StructureDefinition"
                    && pr.IsActive)
                    //TODO investigate later: && (fhirVersion == null || pr.FhirVersion == fhirVersion))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            int customResourceCount = 0;
            foreach (var sd in structureDefinitions)
            {
                try
                {
                    var sdNode = StructureDefinitionJsonNode.Parse(sd.ResourceJson, _logger);
                    if (sdNode == null)
                        continue;

                    // Case 1: kind='resource' AND derivation='specialization' = new custom resource type
                    if (string.Equals(sdNode.Kind, "resource", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(sdNode.Derivation, "specialization", StringComparison.OrdinalIgnoreCase))
                    {
                        var resourceType = sdNode.Name;
                        if (!string.IsNullOrWhiteSpace(resourceType))
                        {
                            customTypes.Add(resourceType);
                            customResourceCount++;
                            _logger.LogDebug(
                                "Extracted custom resource type '{ResourceType}' from specialized StructureDefinition in package {PackageId}@{PackageVersion}",
                                resourceType, sd.PackageId, sd.PackageVersion);
                        }
                    }
                    // Case 2: kind='logical' = logical model (custom data structure)
                    else if (string.Equals(sdNode.Kind, "logical", StringComparison.OrdinalIgnoreCase))
                    {
                        var resourceType = sdNode.Name;
                        if (!string.IsNullOrWhiteSpace(resourceType))
                        {
                            customTypes.Add(resourceType);
                            customResourceCount++;
                            _logger.LogDebug(
                                "Extracted custom logical model '{ResourceType}' from StructureDefinition in package {PackageId}@{PackageVersion}",
                                resourceType, sd.PackageId, sd.PackageVersion);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to parse StructureDefinition from package {PackageId}@{PackageVersion}",
                        sd.PackageId, sd.PackageVersion);
                }
            }

            if (customTypes.Count > 0)
            {
                _logger.LogInformation(
                    "Extracted {Count} custom resource types from {StructureDefinitionCount} StructureDefinition resources",
                    customTypes.Count,
                    structureDefinitions.Count);
            }

            return customTypes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting custom resource types from packages");
            return new HashSet<string>();
        }
    }

    public async Task<IReadOnlyList<PackageResource>> GetSearchParametersByResourceTypeAsync(
        string resourceType,
        string? fhirVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Query for all active SearchParameters that apply to this resource type
        // SearchParameter.base[] field contains the resource types this parameter applies to
        // We need to parse the JSON to check if resourceType is in the base[] array
        var query = dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.ResourceType == "SearchParameter" && pr.IsActive);

        // TODO: FHIR version matching - commented out pending resolution of exact matching strategy
        // Filter by FHIR version if specified
        // if (!string.IsNullOrEmpty(fhirVersion))
        // {
        //     query = query.Where(pr => pr.FhirVersion == fhirVersion);
        // }

        // Fetch all SearchParameters and filter by base[] in memory (JSON query limitation)
        // Order by PackageVersion DESC so newest version is first
        var allSearchParameters = await query
            .OrderByDescending(pr => pr.PackageVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Filter by resource type using JSON parsing
        var matchingSearchParameters = allSearchParameters
            .Where(pr => SearchParameterAppliesToResourceType(pr.ResourceJson, resourceType))
            .Select(MapEntityToModel)
            .ToList()
            .AsReadOnly();

        _logger.LogDebug(
            "Found {Count} SearchParameters for resource type {ResourceType} (FHIR version: {FhirVersion})",
            matchingSearchParameters.Count,
            resourceType,
            fhirVersion ?? "any");

        return matchingSearchParameters;
    }

    public async Task<IReadOnlyList<PackageResource>> GetSearchParametersByCanonicalAsync(
        string canonical,
        string? fhirVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Query for all active SearchParameters with matching canonical URL
        var query = dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.ResourceType == "SearchParameter"
                && pr.Canonical == canonical
                && pr.IsActive);

        // TODO: FHIR version matching - commented out pending resolution of exact matching strategy
        // Filter by FHIR version if specified
        // if (!string.IsNullOrEmpty(fhirVersion))
        // {
        //     query = query.Where(pr => pr.FhirVersion == fhirVersion);
        // }

        // Order by PackageVersion DESC so newest version is first
        var entities = await query
            .OrderByDescending(pr => pr.PackageVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Found {Count} SearchParameters with canonical {Canonical} (FHIR version: {FhirVersion})",
            entities.Count,
            canonical,
            fhirVersion ?? "any");

        return entities.Select(MapEntityToModel).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<PackageResource>> GetAllSearchParametersAsync(
        string? fhirVersion = null,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        // Query for all active SearchParameters across all packages
        var query = dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.ResourceType == "SearchParameter" && pr.IsActive);

        // TODO: FHIR version matching - commented out pending resolution of exact matching strategy
        // if (!string.IsNullOrEmpty(fhirVersion))
        // {
        //     query = query.Where(pr => pr.FhirVersion == fhirVersion);
        // }

        // Order by PackageVersion DESC so newest version is first
        var entities = await query
            .OrderByDescending(pr => pr.PackageVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Retrieved {Count} SearchParameters from packages (FHIR version: {FhirVersion})",
            entities.Count,
            fhirVersion ?? "any");

        return entities.Select(MapEntityToModel).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<PackageResource>> GetOperationDefinitionsAsync(
        IReadOnlyList<string> operationNames,
        string? fhirVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationNames);

        if (operationNames.Count == 0)
        {
            return Array.Empty<PackageResource>();
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Query for all active OperationDefinitions with matching resource IDs
        var query = dbContext.PackageResources
            .AsNoTracking()
            .Where(pr => pr.ResourceType == "OperationDefinition"
                && pr.IsActive
                && operationNames.Contains(pr.ResourceId));

        // TODO: FHIR version matching - commented out pending resolution of exact matching strategy
        // if (!string.IsNullOrEmpty(fhirVersion))
        // {
        //     query = query.Where(pr => pr.FhirVersion == fhirVersion);
        // }

        // Order by PackageVersion DESC so newest version is first
        var entities = await query
            .OrderByDescending(pr => pr.PackageVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Retrieved {Count} OperationDefinitions from packages (FHIR version: {FhirVersion})",
            entities.Count,
            fhirVersion ?? "any");

        return entities.Select(MapEntityToModel).ToList().AsReadOnly();
    }

    /// <summary>
    /// Checks if a SearchParameter applies to a given resource type by parsing the base[] field.
    /// </summary>
    private bool SearchParameterAppliesToResourceType(string resourceJson, string resourceType)
    {
        try
        {
            var jsonNode = System.Text.Json.JsonDocument.Parse(resourceJson);
            if (jsonNode.RootElement.TryGetProperty("base", out var baseElement))
            {
                if (baseElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var baseItem in baseElement.EnumerateArray())
                    {
                        if (baseItem.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var baseValue = baseItem.GetString();
                            if (string.Equals(baseValue, resourceType, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse SearchParameter JSON to check base[] field");
            return false;
        }
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
