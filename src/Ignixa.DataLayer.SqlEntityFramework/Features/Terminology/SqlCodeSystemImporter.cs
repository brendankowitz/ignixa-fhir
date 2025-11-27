// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;
using Ignixa.Validation.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;

/// <summary>
/// Imports CodeSystem resources into TermCodeSystem and TermConcept tables.
/// Parses CodeSystem JSON, flattens hierarchy, and normalizes system URLs.
/// </summary>
public class SqlCodeSystemImporter : ITerminologyImporter
{
    private readonly FhirDbContext _context;
    private readonly ISystemRepository _systemRepository;
    private readonly ILogger<SqlCodeSystemImporter> _logger;

    // Cache system IDs across imports to avoid repeated database lookups
    private readonly Dictionary<string, int> _systemIdCache = new(StringComparer.Ordinal);

    public SqlCodeSystemImporter(
        FhirDbContext context,
        ISystemRepository systemRepository,
        ILogger<SqlCodeSystemImporter> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _systemRepository = systemRepository ?? throw new ArgumentNullException(nameof(systemRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets or creates a system ID, using class-level cache for performance.
    /// </summary>
    private async Task<int> GetOrCreateSystemIdCachedAsync(string systemUri, CancellationToken cancellationToken)
    {
        if (!_systemIdCache.TryGetValue(systemUri, out var systemId))
        {
            systemId = await _systemRepository.GetOrCreateAsync(systemUri, cancellationToken);
            _systemIdCache[systemUri] = systemId;
        }
        return systemId;
    }

    public async Task<TerminologyImportResult> ImportCodeSystemAsync(
        int tenantId,
        PackageResource packageResource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        if (packageResource.ResourceType != "CodeSystem")
        {
            throw new ArgumentException($"Expected ResourceType 'CodeSystem', got '{packageResource.ResourceType}'", nameof(packageResource));
        }

        _logger.LogInformation(
            "Starting CodeSystem import for '{Canonical}' (PackageResourceId: {PackageResourceId})",
            packageResource.Canonical,
            packageResource.PackageResourceId);

        var packageResourceEntity = await _context.PackageResources
            .FirstOrDefaultAsync(pr => pr.PackageResourceId == packageResource.PackageResourceId, cancellationToken);

        if (packageResourceEntity == null)
        {
            throw new InvalidOperationException($"PackageResource {packageResource.PackageResourceId} not found in tenant {tenantId}");
        }

        try
        {
            // 1. Parse CodeSystem JSON
            JsonObject codeSystem = ParseCodeSystemJson(packageResource.ResourceJson);

            // 2. Check content hash (skip if unchanged)
            string newContentHash = packageResource.ComputeContentHash();
            if (string.Equals(packageResourceEntity.ContentHash, newContentHash, StringComparison.Ordinal) &&
                string.Equals(packageResourceEntity.TerminologyImportStatus, nameof(TerminologyImportStatus.Completed), StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "CodeSystem '{Canonical}' content unchanged (hash: {Hash}), skipping import",
                    packageResource.Canonical,
                    newContentHash);

                packageResourceEntity.ContentHash = newContentHash;
                packageResourceEntity.ImportStartDate ??= DateTimeOffset.UtcNow;
                packageResourceEntity.ImportCompletedDate ??= DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                return TerminologyImportResult.CreateSkipped();
            }

            // 3. Extract metadata
            var metadata = ExtractMetadata(codeSystem);

            // Validate required fields
            if (string.IsNullOrEmpty(metadata.Url))
            {
                throw new InvalidOperationException("CodeSystem.url is required");
            }

            if (string.IsNullOrEmpty(metadata.Content))
            {
                throw new InvalidOperationException("CodeSystem.content is required");
            }

            // Skip if content is not-present (no concepts to import)
            if (metadata.Content == "not-present")
            {
                _logger.LogInformation(
                    "CodeSystem '{Canonical}' has content=not-present, skipping import",
                    packageResource.Canonical);

                packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.Skipped);
                packageResourceEntity.ContentHash = newContentHash;
                packageResourceEntity.ImportStartDate = DateTimeOffset.UtcNow;
                packageResourceEntity.ImportCompletedDate = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                return TerminologyImportResult.CreateSkipped();
            }

            // CRITICAL FIX #1: Handle CodeSystem supplements
            // Supplements (content=supplement) add properties to concepts in another CodeSystem
            // Example: http://hl7.org/fhir/us/core/CodeSystem/us-core-narrative-status
            // supplements http://hl7.org/fhir/narrative-status
            if (metadata.Content == "supplement")
            {
                // Week 4 TODO: Implement supplement merging logic
                // For now, skip supplements to avoid creating duplicate concepts
                _logger.LogWarning(
                    "CodeSystem '{Canonical}' is a supplement (content=supplement). " +
                    "Supplement import not yet implemented (Week 4). Skipping.",
                    packageResource.Canonical);

                packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.Skipped);
                packageResourceEntity.ContentHash = newContentHash;
                packageResourceEntity.ImportStartDate = DateTimeOffset.UtcNow;
                packageResourceEntity.ImportCompletedDate = DateTimeOffset.UtcNow;
                packageResourceEntity.ImportErrorMessage = "Supplement import not yet implemented";
                await _context.SaveChangesAsync(cancellationToken);

                return TerminologyImportResult.CreateSkipped();
            }

            // 4. Get or create SystemId
            int systemId = await _systemRepository.GetOrCreateAsync(metadata.Url, cancellationToken);

            // 5. Execute within resilient transaction (supports SqlServerRetryingExecutionStrategy)
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    // Update import status to InProgress
                    packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.InProgress);
                    packageResourceEntity.ImportStartDate = DateTimeOffset.UtcNow;
                    packageResourceEntity.ContentHash = newContentHash;
                    await _context.SaveChangesAsync(cancellationToken);

                    // 6. Delete existing TermCodeSystem (cascade deletes TermConcepts)
                    var existingCodeSystem = await _context.TermCodeSystems
                        .FirstOrDefaultAsync(tcs => tcs.PackageResourceId == packageResource.PackageResourceId, cancellationToken);

                    if (existingCodeSystem != null)
                    {
                        _logger.LogInformation(
                            "Deleting existing TermCodeSystem {TermCodeSystemId} for re-import",
                            existingCodeSystem.TermCodeSystemId);

                        _context.TermCodeSystems.Remove(existingCodeSystem);
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    // 7. Create TermCodeSystem entity
                    var termCodeSystem = new TermCodeSystemEntity
                    {
                        PackageResourceId = packageResource.PackageResourceId,
                        SystemId = systemId,
                        Version = metadata.Version,
                        ConceptCount = metadata.Count ?? 0,
                        Content = metadata.Content,
                        IsHierarchical = metadata.IsHierarchical,
                        CaseSensitive = metadata.CaseSensitive,
                        Compositional = metadata.Compositional,
                        ImportedDate = DateTimeOffset.UtcNow
                    };

                    _context.TermCodeSystems.Add(termCodeSystem);
                    await _context.SaveChangesAsync(cancellationToken);

                    // 8. Flatten concept hierarchy
                    var (concepts, parentMap) = FlattenConcepts(codeSystem["concept"]?.AsArray(), termCodeSystem.TermCodeSystemId, null, 0);

                    _logger.LogInformation(
                        "Importing {ConceptCount} concepts for CodeSystem '{Canonical}'",
                        concepts.Count,
                        packageResource.Canonical);

                    // 9. Save concepts - Week 5: SqlBulkCopy optimization for large CodeSystems
                    const int BulkInsertThreshold = 1000;

                    if (concepts.Count > BulkInsertThreshold)
                    {
                        // Large CodeSystem: Use SqlBulkCopy
                        _logger.LogInformation(
                            "CodeSystem '{Canonical}' has {Count} concepts (>{Threshold}), using SqlBulkCopy",
                            packageResource.Canonical,
                            concepts.Count,
                            BulkInsertThreshold);

                        // Pass 1: Bulk insert concepts (ParentConceptId will be NULL initially)
                        await BulkInsertConceptsAsync(termCodeSystem.TermCodeSystemId, concepts, cancellationToken);

                        // Pass 2: Update parent references
                        await UpdateParentReferencesAsync(termCodeSystem.TermCodeSystemId, parentMap, cancellationToken);
                    }
                    else
                    {
                        // Small CodeSystem: Use EF AddRange (simpler, no performance issue)
                        _logger.LogInformation(
                            "CodeSystem '{Canonical}' has {Count} concepts (<={Threshold}), using EF AddRange",
                            packageResource.Canonical,
                            concepts.Count,
                            BulkInsertThreshold);

                        _context.TermConcepts.AddRange(concepts);
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    // 10. Update PackageResource import status
                    packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.Completed);
                    packageResourceEntity.ImportCompletedDate = DateTimeOffset.UtcNow;
                    packageResourceEntity.ImportedConceptCount = concepts.Count;
                    packageResourceEntity.ImportErrorMessage = null;

                    // Update ConceptCount if not specified in metadata
                    if (metadata.Count == null || metadata.Count == 0)
                    {
                        termCodeSystem.ConceptCount = concepts.Count;
                    }

                    await _context.SaveChangesAsync(cancellationToken);

                    // 11. Commit transaction
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "Successfully imported CodeSystem '{Canonical}' with {ConceptCount} concepts",
                        packageResource.Canonical,
                        concepts.Count);

                    return TerminologyImportResult.CreateSuccess(concepts.Count);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to import CodeSystem '{Canonical}' (PackageResourceId: {PackageResourceId}): {ErrorMessage}",
                packageResource.Canonical,
                packageResource.PackageResourceId,
                ex.Message);

            // Update PackageResource with error
            packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.Failed);
            packageResourceEntity.ImportCompletedDate = DateTimeOffset.UtcNow;
            packageResourceEntity.ImportErrorMessage = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save error status to database");
            }

            return TerminologyImportResult.CreateFailure(ex.Message);
        }
    }

    public async Task<TerminologyImportResult> ImportValueSetAsync(
        int tenantId,
        PackageResource packageResource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        if (packageResource.ResourceType != "ValueSet")
        {
            throw new ArgumentException($"Expected ResourceType 'ValueSet', got '{packageResource.ResourceType}'", nameof(packageResource));
        }

        _logger.LogInformation(
            "Starting ValueSet import for '{Canonical}' (PackageResourceId: {PackageResourceId})",
            packageResource.Canonical,
            packageResource.PackageResourceId);

        var packageResourceEntity = await _context.PackageResources
            .FirstOrDefaultAsync(pr => pr.PackageResourceId == packageResource.PackageResourceId, cancellationToken);

        if (packageResourceEntity == null)
        {
            throw new InvalidOperationException($"PackageResource {packageResource.PackageResourceId} not found in tenant {tenantId}");
        }

        try
        {
            // 1. Parse ValueSet JSON
            JsonObject valueSet = ParseValueSetJson(packageResource.ResourceJson);

            // 2. Check content hash (skip if unchanged)
            string newContentHash = packageResource.ComputeContentHash();
            if (string.Equals(packageResourceEntity.ContentHash, newContentHash, StringComparison.Ordinal) &&
                string.Equals(packageResourceEntity.TerminologyImportStatus, nameof(TerminologyImportStatus.Completed), StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "ValueSet '{Canonical}' content unchanged (hash: {Hash}), skipping import",
                    packageResource.Canonical,
                    newContentHash);

                packageResourceEntity.ContentHash = newContentHash;
                packageResourceEntity.ImportStartDate ??= DateTimeOffset.UtcNow;
                packageResourceEntity.ImportCompletedDate ??= DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                return TerminologyImportResult.CreateSkipped();
            }

            // 3. Extract metadata
            var metadata = ExtractValueSetMetadata(valueSet);

            // Validate required fields
            if (string.IsNullOrEmpty(metadata.Url))
            {
                throw new InvalidOperationException("ValueSet.url is required");
            }

            if (string.IsNullOrEmpty(metadata.Name))
            {
                throw new InvalidOperationException("ValueSet.name is required");
            }

            // 4. Execute within resilient transaction (supports SqlServerRetryingExecutionStrategy)
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    // Update import status to InProgress
                    packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.InProgress);
                    packageResourceEntity.ImportStartDate = DateTimeOffset.UtcNow;
                    packageResourceEntity.ContentHash = newContentHash;
                    await _context.SaveChangesAsync(cancellationToken);

                    // 5. Delete existing TermValueSet (cascade deletes TermValueSetExpansion)
                    var existingValueSet = await _context.TermValueSets
                        .FirstOrDefaultAsync(tvs => tvs.PackageResourceId == packageResource.PackageResourceId, cancellationToken);

                    if (existingValueSet != null)
                    {
                        _logger.LogInformation(
                            "Deleting existing TermValueSet {TermValueSetId} for re-import",
                            existingValueSet.TermValueSetId);

                        _context.TermValueSets.Remove(existingValueSet);
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    // 6. Create TermValueSet entity
                    var termValueSet = new TermValueSetEntity
                    {
                        PackageResourceId = packageResource.PackageResourceId,
                        Canonical = metadata.Url,
                        Version = metadata.Version,
                        Name = metadata.Name,
                        Immutable = metadata.Immutable,
                        IsExpanded = false,  // Will be set to true if expansion entries imported
                        ImportedDate = DateTimeOffset.UtcNow
                    };

                    _context.TermValueSets.Add(termValueSet);
                    await _context.SaveChangesAsync(cancellationToken);

                    // 7. Import expansion entries if present, otherwise compute from compose
                    int importedCount = 0;
                    bool isPartialExpansion = false;
                    string? partialExpansionReason = null;

                    var expansion = valueSet["expansion"];
                    if (expansion != null)
                    {
                        importedCount = await ImportExpansionEntries(
                            expansion.AsObject(),
                            termValueSet.TermValueSetId,
                            cancellationToken);
                        // Pre-computed expansions are assumed to be complete
                    }
                    else
                    {
                        var compose = valueSet["compose"]?.AsObject();
                        if (compose != null)
                        {
                            var composeResult = await ImportComposeExpansionAsync(
                                compose,
                                termValueSet.TermValueSetId,
                                cancellationToken);
                            importedCount = composeResult.ImportedCount;
                            isPartialExpansion = composeResult.IsPartial;

                            // Build reason string if partial
                            if (isPartialExpansion)
                            {
                                var reasons = new List<string>();
                                if (composeResult.ExternalSystems.Count > 0)
                                {
                                    reasons.Add($"External systems not imported: {string.Join(", ", composeResult.ExternalSystems)}");
                                }
                                if (composeResult.MissingValueSets.Count > 0)
                                {
                                    reasons.Add($"Referenced ValueSets not expanded: {string.Join(", ", composeResult.MissingValueSets)}");
                                }
                                partialExpansionReason = string.Join("; ", reasons);
                                if (partialExpansionReason.Length > 1024)
                                {
                                    partialExpansionReason = string.Concat(partialExpansionReason.AsSpan(0, 1021), "...");
                                }
                            }
                        }
                    }

                    if (importedCount > 0 || isPartialExpansion)
                    {
                        termValueSet.IsExpanded = true;
                        termValueSet.LastExpansionDate = DateTimeOffset.UtcNow;
                        termValueSet.ExpansionCodeCount = importedCount;
                        termValueSet.IsPartialExpansion = isPartialExpansion;
                        termValueSet.PartialExpansionReason = partialExpansionReason;
                        await _context.SaveChangesAsync(cancellationToken);

                        if (isPartialExpansion)
                        {
                            _logger.LogWarning(
                                "ValueSet '{Canonical}' has partial expansion ({Count} codes). Reason: {Reason}",
                                packageResource.Canonical,
                                importedCount,
                                partialExpansionReason);
                        }
                    }

                    // 8. Update PackageResource import status
                    packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.Completed);
                    packageResourceEntity.ImportCompletedDate = DateTimeOffset.UtcNow;
                    packageResourceEntity.ImportedConceptCount = importedCount;
                    packageResourceEntity.ImportErrorMessage = null;
                    await _context.SaveChangesAsync(cancellationToken);

                    // 9. Commit transaction
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "Successfully imported ValueSet '{Canonical}' with {ConceptCount} expansion entries",
                        packageResource.Canonical,
                        importedCount);

                    return TerminologyImportResult.CreateSuccess(importedCount);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to import ValueSet '{Canonical}' (PackageResourceId: {PackageResourceId}): {ErrorMessage}",
                packageResource.Canonical,
                packageResource.PackageResourceId,
                ex.Message);

            // Update PackageResource with error
            packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.Failed);
            packageResourceEntity.ImportCompletedDate = DateTimeOffset.UtcNow;
            packageResourceEntity.ImportErrorMessage = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save error status to database");
            }

            return TerminologyImportResult.CreateFailure(ex.Message);
        }
    }

    public async Task<TerminologyImportResult> ImportConceptMapAsync(
        int tenantId,
        PackageResource packageResource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageResource);

        if (packageResource.ResourceType != "ConceptMap")
        {
            throw new ArgumentException($"Expected ResourceType 'ConceptMap', got '{packageResource.ResourceType}'", nameof(packageResource));
        }

        _logger.LogInformation(
            "Starting ConceptMap import for '{Canonical}' (PackageResourceId: {PackageResourceId})",
            packageResource.Canonical,
            packageResource.PackageResourceId);

        var packageResourceEntity = await _context.PackageResources
            .FirstOrDefaultAsync(pr => pr.PackageResourceId == packageResource.PackageResourceId, cancellationToken);

        if (packageResourceEntity == null)
        {
            throw new InvalidOperationException($"PackageResource {packageResource.PackageResourceId} not found in tenant {tenantId}");
        }

        try
        {
            // 1. Parse ConceptMap JSON
            JsonObject conceptMap = ParseConceptMapJson(packageResource.ResourceJson);

            // 2. Check content hash (skip if unchanged)
            string newContentHash = packageResource.ComputeContentHash();
            if (string.Equals(packageResourceEntity.ContentHash, newContentHash, StringComparison.Ordinal) &&
                string.Equals(packageResourceEntity.TerminologyImportStatus, nameof(TerminologyImportStatus.Completed), StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "ConceptMap '{Canonical}' content unchanged (hash: {Hash}), skipping import",
                    packageResource.Canonical,
                    newContentHash);

                packageResourceEntity.ContentHash = newContentHash;
                packageResourceEntity.ImportStartDate ??= DateTimeOffset.UtcNow;
                packageResourceEntity.ImportCompletedDate ??= DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                return TerminologyImportResult.CreateSkipped();
            }

            // 3. Extract metadata
            var metadata = ExtractConceptMapMetadata(conceptMap);

            // Validate required fields
            if (string.IsNullOrEmpty(metadata.Url))
            {
                throw new InvalidOperationException("ConceptMap.url is required");
            }

            if (string.IsNullOrEmpty(metadata.Name))
            {
                throw new InvalidOperationException("ConceptMap.name is required");
            }

            // 4. Execute within resilient transaction (supports SqlServerRetryingExecutionStrategy)
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    // Update import status to InProgress
                    packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.InProgress);
                    packageResourceEntity.ImportStartDate = DateTimeOffset.UtcNow;
                    packageResourceEntity.ContentHash = newContentHash;
                    await _context.SaveChangesAsync(cancellationToken);

                    // 5. Delete existing TermConceptMap (cascade deletes TermConceptMapElement)
                    var existingConceptMap = await _context.TermConceptMaps
                        .FirstOrDefaultAsync(tcm => tcm.PackageResourceId == packageResource.PackageResourceId, cancellationToken);

                    if (existingConceptMap != null)
                    {
                        _logger.LogInformation(
                            "Deleting existing TermConceptMap {TermConceptMapId} for re-import",
                            existingConceptMap.TermConceptMapId);

                        _context.TermConceptMaps.Remove(existingConceptMap);
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    // 6. Create TermConceptMap entity
                    var termConceptMap = new TermConceptMapEntity
                    {
                        PackageResourceId = packageResource.PackageResourceId,
                        Canonical = metadata.Url,
                        Version = metadata.Version,
                        Name = metadata.Name,
                        SourceCanonical = metadata.SourceCanonical,
                        TargetCanonical = metadata.TargetCanonical,
                        ImportedDate = DateTimeOffset.UtcNow
                    };

                    _context.TermConceptMaps.Add(termConceptMap);
                    await _context.SaveChangesAsync(cancellationToken);

                    // 7. Import mapping elements from groups
                    int importedCount = await ImportConceptMapElements(
                        conceptMap,
                        termConceptMap.TermConceptMapId,
                        cancellationToken);

                    // 8. Update PackageResource import status
                    packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.Completed);
                    packageResourceEntity.ImportCompletedDate = DateTimeOffset.UtcNow;
                    packageResourceEntity.ImportedConceptCount = importedCount;
                    packageResourceEntity.ImportErrorMessage = null;
                    await _context.SaveChangesAsync(cancellationToken);

                    // 9. Commit transaction
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "Successfully imported ConceptMap '{Canonical}' with {Count} mapping elements",
                        packageResource.Canonical,
                        importedCount);

                    return TerminologyImportResult.CreateSuccess(importedCount);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to import ConceptMap '{Canonical}' (PackageResourceId: {PackageResourceId}): {ErrorMessage}",
                packageResource.Canonical,
                packageResource.PackageResourceId,
                ex.Message);

            // Update PackageResource with error
            packageResourceEntity.TerminologyImportStatus = nameof(TerminologyImportStatus.Failed);
            packageResourceEntity.ImportCompletedDate = DateTimeOffset.UtcNow;
            packageResourceEntity.ImportErrorMessage = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save error status to database");
            }

            return TerminologyImportResult.CreateFailure(ex.Message);
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Helper Methods
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses CodeSystem JSON string into a JsonObject.
    /// </summary>
    private JsonObject ParseCodeSystemJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        if (node == null)
        {
            throw new InvalidOperationException("Failed to parse CodeSystem JSON (null result)");
        }

        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException($"Expected JSON object, got {node.GetType().Name}");
        }

        string? resourceType = obj["resourceType"]?.GetValue<string>();
        if (resourceType != "CodeSystem")
        {
            throw new InvalidOperationException($"Expected resourceType 'CodeSystem', got '{resourceType}'");
        }

        return obj;
    }

    /// <summary>
    /// Extracts metadata from CodeSystem JSON.
    /// </summary>
    private CodeSystemMetadata ExtractMetadata(JsonObject codeSystem)
    {
        return new CodeSystemMetadata
        {
            Url = codeSystem["url"]?.GetValue<string>() ?? throw new InvalidOperationException("CodeSystem.url is required"),
            Version = codeSystem["version"]?.GetValue<string>(),
            Content = codeSystem["content"]?.GetValue<string>() ?? throw new InvalidOperationException("CodeSystem.content is required"),
            Count = codeSystem["count"]?.GetValue<int>(),
            CaseSensitive = codeSystem["caseSensitive"]?.GetValue<bool>() ?? true,
            HierarchyMeaning = codeSystem["hierarchyMeaning"]?.GetValue<string>(),
            Compositional = codeSystem["compositional"]?.GetValue<bool>() ?? false
        };
    }

    /// <summary>
    /// Flattens concept hierarchy into a flat list of TermConceptEntity.
    /// Uses a queue-based approach to handle parent-child relationships properly.
    /// Parent concepts are added first, then children reference parent codes via temporary tracking.
    /// Returns both the flattened list and a map of concept code → parent code for parent reference resolution.
    /// </summary>
    private (List<TermConceptEntity> Concepts, Dictionary<string, string?> ParentMap) FlattenConcepts(
        JsonArray? concepts,
        long termCodeSystemId,
        long? parentConceptId,
        int level)
    {
        var result = new List<TermConceptEntity>();
        var parentMap = new Dictionary<string, string?>();

        if (concepts == null || concepts.Count == 0)
        {
            return (result, parentMap);
        }

        // Queue of (concept JSON, parent code, level) to process
        var queue = new Queue<(JsonObject Concept, string? ParentCode, int Level)>();

        // Initialize queue with root concepts
        foreach (var conceptNode in concepts)
        {
            if (conceptNode is JsonObject concept)
            {
                queue.Enqueue((concept, null, level));
            }
        }

        // Track parent codes to resolve parent IDs later
        // Map: concept code → (TermConceptEntity, parent code)
        var conceptMap = new Dictionary<string, (TermConceptEntity Entity, string? ParentCode)>();

        // Process all concepts breadth-first
        while (queue.Count > 0)
        {
            var (concept, parentCode, currentLevel) = queue.Dequeue();

            string? code = concept["code"]?.GetValue<string>();
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("Skipping concept with missing code: {Concept}", concept.ToJsonString());
                continue;
            }

            string? display = concept["display"]?.GetValue<string>();
            string? definition = concept["definition"]?.GetValue<string>();

            // Truncate definition if too long (SQL max: 4000 chars)
            if (definition?.Length > 4000)
            {
                _logger.LogWarning(
                    "Truncating definition for concept '{Code}' from {Length} to 4000 characters",
                    code,
                    definition.Length);

                definition = definition.Substring(0, 4000);
            }

            // Serialize properties and designations to JSON
            string? propertiesJson = SerializePropertiesJson(concept["property"], concept["designation"]);

            var termConcept = new TermConceptEntity
            {
                TermCodeSystemId = termCodeSystemId,
                Code = code,
                Display = display,
                Definition = definition,
                ParentConceptId = null, // Will be set after save if has parent
                Level = currentLevel,
                IsActive = true,
                PropertiesJson = propertiesJson
            };

            result.Add(termConcept);
            conceptMap[code] = (termConcept, parentCode);
            parentMap[code] = parentCode;

            // Enqueue child concepts
            var childConcepts = concept["concept"]?.AsArray();
            if (childConcepts != null && childConcepts.Count > 0)
            {
                foreach (var childNode in childConcepts)
                {
                    if (childNode is JsonObject childConcept)
                    {
                        queue.Enqueue((childConcept, code, currentLevel + 1));
                    }
                }
            }
        }

        // Note: ParentConceptId relationships cannot be resolved until after concepts are saved
        // and have database-generated IDs. For Phase 1, we store concepts without parent IDs,
        // and will add a second pass in Week 5 to update parent references after bulk insert.
        // For now, parent-child relationships are tracked via Level field and code matching.

        return (result, parentMap);
    }

    /// <summary>
    /// Bulk inserts TermConcept entities using SqlBulkCopy for improved performance.
    /// Used for large CodeSystems (>1000 concepts).
    /// </summary>
    private async Task BulkInsertConceptsAsync(
        long termCodeSystemId,
        List<TermConceptEntity> concepts,
        CancellationToken cancellationToken)
    {
        if (concepts.Count == 0) return;

        _logger.LogInformation(
            "Using SqlBulkCopy for {Count} concepts (TermCodeSystemId: {TermCodeSystemId})",
            concepts.Count,
            termCodeSystemId);

        // Get connection string from DbContext
        var connectionString = _context.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Database connection string is null");
        }

        // Create DataTable
        using var conceptTable = new DataTable();
        conceptTable.Columns.Add("TermCodeSystemId", typeof(long));
        conceptTable.Columns.Add("Code", typeof(string));
        conceptTable.Columns.Add("Display", typeof(string));
        conceptTable.Columns.Add("Definition", typeof(string));
        conceptTable.Columns.Add("ParentConceptId", typeof(long));
        conceptTable.Columns.Add("Level", typeof(int));
        conceptTable.Columns.Add("IsActive", typeof(bool));
        conceptTable.Columns.Add("PropertiesJson", typeof(string));

        // Populate DataTable from concepts
        foreach (var concept in concepts)
        {
            var row = conceptTable.NewRow();
            row["TermCodeSystemId"] = termCodeSystemId;
            row["Code"] = concept.Code;
            row["Display"] = (object?)concept.Display ?? DBNull.Value;
            row["Definition"] = (object?)concept.Definition ?? DBNull.Value;
            row["ParentConceptId"] = concept.ParentConceptId.HasValue ? (object)concept.ParentConceptId.Value : DBNull.Value;
            row["Level"] = concept.Level;
            row["IsActive"] = concept.IsActive;
            row["PropertiesJson"] = (object?)concept.PropertiesJson ?? DBNull.Value;
            conceptTable.Rows.Add(row);
        }

        // Use SqlBulkCopy
        using var bulkCopy = new SqlBulkCopy(connectionString, SqlBulkCopyOptions.Default);
        bulkCopy.DestinationTableName = "dbo.TermConcept";
        bulkCopy.BatchSize = 10000;
        bulkCopy.BulkCopyTimeout = 300; // 5 minutes for very large imports

        // Map columns
        bulkCopy.ColumnMappings.Add("TermCodeSystemId", "TermCodeSystemId");
        bulkCopy.ColumnMappings.Add("Code", "Code");
        bulkCopy.ColumnMappings.Add("Display", "Display");
        bulkCopy.ColumnMappings.Add("Definition", "Definition");
        bulkCopy.ColumnMappings.Add("ParentConceptId", "ParentConceptId");
        bulkCopy.ColumnMappings.Add("Level", "Level");
        bulkCopy.ColumnMappings.Add("IsActive", "IsActive");
        bulkCopy.ColumnMappings.Add("PropertiesJson", "PropertiesJson");

        await bulkCopy.WriteToServerAsync(conceptTable, cancellationToken);

        _logger.LogInformation(
            "SqlBulkCopy completed for {Count} concepts",
            concepts.Count);
    }

    /// <summary>
    /// Updates ParentConceptId foreign keys after bulk insert using two-pass approach.
    /// Creates temp table with code→parentCode mappings, then updates via JOIN.
    /// </summary>
    private async Task UpdateParentReferencesAsync(
        long termCodeSystemId,
        Dictionary<string, string?> parentMap,
        CancellationToken cancellationToken)
    {
        if (parentMap.Count == 0) return;

        _logger.LogInformation(
            "Updating parent references for {Count} concepts (TermCodeSystemId: {TermCodeSystemId})",
            parentMap.Count,
            termCodeSystemId);

        // Create temp table for parent mappings
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE #ParentMapping (
                Code NVARCHAR(256) NOT NULL,
                ParentCode NVARCHAR(256) NULL
            )", cancellationToken);

        // Get connection string for SqlBulkCopy
        var connectionString = _context.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Database connection string is null");
        }

        // Create DataTable for parent mappings
        using var mappingTable = new DataTable();
        mappingTable.Columns.Add("Code", typeof(string));
        mappingTable.Columns.Add("ParentCode", typeof(string));

        foreach (var (code, parentCode) in parentMap)
        {
            if (!string.IsNullOrEmpty(parentCode)) // Only add mappings where parent exists
            {
                var row = mappingTable.NewRow();
                row["Code"] = code;
                row["ParentCode"] = parentCode;
                mappingTable.Rows.Add(row);
            }
        }

        // Bulk insert parent mappings into temp table
        if (mappingTable.Rows.Count > 0)
        {
            using var bulkCopy = new SqlBulkCopy(connectionString, SqlBulkCopyOptions.Default);
            bulkCopy.DestinationTableName = "#ParentMapping";
            bulkCopy.BatchSize = 10000;

            bulkCopy.ColumnMappings.Add("Code", "Code");
            bulkCopy.ColumnMappings.Add("ParentCode", "ParentCode");

            await bulkCopy.WriteToServerAsync(mappingTable, cancellationToken);

            // Update ParentConceptId using JOIN
            var updateSql = @"
                UPDATE tc
                SET ParentConceptId = parent.TermConceptId
                FROM dbo.TermConcept tc
                INNER JOIN #ParentMapping pm ON tc.Code = pm.Code AND tc.TermCodeSystemId = @systemId
                INNER JOIN dbo.TermConcept parent ON parent.Code = pm.ParentCode AND parent.TermCodeSystemId = @systemId";

            await _context.Database.ExecuteSqlRawAsync(
                updateSql,
                new SqlParameter("@systemId", termCodeSystemId),
                cancellationToken);

            _logger.LogInformation(
                "Updated parent references for {Count} concepts",
                mappingTable.Rows.Count);
        }

        // Drop temp table
        await _context.Database.ExecuteSqlRawAsync("DROP TABLE #ParentMapping", cancellationToken);
    }

    /// <summary>
    /// Serializes concept properties and designations to JSON string.
    /// Returns null if no properties or designations exist.
    /// </summary>
    private string? SerializePropertiesJson(JsonNode? properties, JsonNode? designations)
    {
        bool hasProperties = properties is JsonArray propArray && propArray.Count > 0;
        bool hasDesignations = designations is JsonArray desigArray && desigArray.Count > 0;

        if (!hasProperties && !hasDesignations)
        {
            return null;
        }

        var wrapper = new JsonObject();

        if (hasProperties)
        {
            wrapper["property"] = properties!.DeepClone();
        }

        if (hasDesignations)
        {
            wrapper["designation"] = designations!.DeepClone();
        }

        return wrapper.ToJsonString();
    }

    /// <summary>
    /// Parses ValueSet JSON string into a JsonObject.
    /// </summary>
    private JsonObject ParseValueSetJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        if (node == null)
        {
            throw new InvalidOperationException("Failed to parse ValueSet JSON (null result)");
        }

        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException($"Expected JSON object, got {node.GetType().Name}");
        }

        string? resourceType = obj["resourceType"]?.GetValue<string>();
        if (resourceType != "ValueSet")
        {
            throw new InvalidOperationException($"Expected resourceType 'ValueSet', got '{resourceType}'");
        }

        return obj;
    }

    /// <summary>
    /// Extracts metadata from ValueSet JSON.
    /// </summary>
    private ValueSetMetadata ExtractValueSetMetadata(JsonObject valueSet)
    {
        return new ValueSetMetadata
        {
            Url = valueSet["url"]?.GetValue<string>() ?? throw new InvalidOperationException("ValueSet.url is required"),
            Version = valueSet["version"]?.GetValue<string>(),
            Name = valueSet["name"]?.GetValue<string>() ?? throw new InvalidOperationException("ValueSet.name is required"),
            Immutable = valueSet["immutable"]?.GetValue<bool>() ?? false
        };
    }

    /// <summary>
    /// Imports expansion entries from ValueSet.expansion into TermValueSetExpansion table.
    /// </summary>
    private async Task<int> ImportExpansionEntries(
        JsonObject expansion,
        long termValueSetId,
        CancellationToken cancellationToken)
    {
        var contains = expansion["contains"]?.AsArray();
        if (contains == null || contains.Count == 0)
        {
            return 0;
        }

        var expansionEntries = new List<TermValueSetExpansionEntity>();
        int ordinal = 0;

        foreach (var containsItem in contains)
        {
            var containsObj = containsItem?.AsObject();
            if (containsObj == null)
            {
                continue;
            }

            string? system = containsObj["system"]?.GetValue<string>();
            string? code = containsObj["code"]?.GetValue<string>();
            string? display = containsObj["display"]?.GetValue<string>();
            string? systemVersion = containsObj["version"]?.GetValue<string>();

            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("Skipping expansion entry with missing code: {Entry}", containsObj.ToJsonString());
                continue;
            }

            // Get SystemId using class-level cache (0 if system is null/empty)
            int systemId = 0;
            if (!string.IsNullOrEmpty(system))
            {
                systemId = await GetOrCreateSystemIdCachedAsync(system, cancellationToken);
            }

            expansionEntries.Add(new TermValueSetExpansionEntity
            {
                TermValueSetId = termValueSetId,
                SystemId = systemId,
                Code = code,
                Display = display,
                SystemVersion = systemVersion,
                IsActive = true,
                Ordinal = ordinal++
            });
        }

        if (expansionEntries.Count > 0)
        {
            _context.TermValueSetExpansions.AddRange(expansionEntries);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return expansionEntries.Count;
    }

    /// <summary>
    /// Result of compose expansion import.
    /// </summary>
    private record ComposeExpansionResult(
        int ImportedCount,
        bool IsPartial,
        List<string> ExternalSystems,
        List<string> MissingValueSets);

    /// <summary>
    /// Builds expansion entries from ValueSet.compose (include/exclude) into TermValueSetExpansion.
    /// Supports:
    /// - include.concept (explicit codes)
    /// - include.valueSet (pulls already-expanded ValueSets)
    /// - include.system with filters (code/display) or full system when enabled
    /// - exclude counterparts remove matching codes
    /// Advanced filter operations (regex, is-a, property-based) are not implemented.
    /// </summary>
    private async Task<ComposeExpansionResult> ImportComposeExpansionAsync(
        JsonObject compose,
        long termValueSetId,
        CancellationToken cancellationToken)
    {
        var includeEntries = new List<TermValueSetExpansionEntity>();
        var includedKeys = new HashSet<string>(StringComparer.Ordinal);
        var excludedKeys = new HashSet<string>(StringComparer.Ordinal);
        var externalSystems = new List<string>();
        var missingValueSets = new List<string>();

        static string MakeKey(int systemId, string code) => $"{systemId}:{code}";

        Task AddEntryAsync(int systemId, string systemUri, string code, string? display, string? systemVersion)
        {
            if (string.IsNullOrEmpty(code))
            {
                return Task.CompletedTask;
            }

            var key = MakeKey(systemId, code);
            if (!includedKeys.Add(key))
            {
                return Task.CompletedTask;
            }

            includeEntries.Add(new TermValueSetExpansionEntity
            {
                TermValueSetId = termValueSetId,
                SystemId = systemId,
                Code = code,
                Display = display,
                SystemVersion = systemVersion,
                IsActive = true,
                Ordinal = includeEntries.Count
            });

            return Task.CompletedTask;
        }

        async Task AddFromValueSetAsync(string canonical)
        {
            var includedValueSet = await _context.TermValueSets
                .AsNoTracking()
                .Where(tvs => tvs.Canonical == canonical && tvs.IsExpanded)
                .OrderByDescending(tvs => tvs.ImportedDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (includedValueSet == null)
            {
                _logger.LogWarning("Compose include references ValueSet '{Canonical}' that is not expanded", canonical);
                if (!missingValueSets.Contains(canonical))
                {
                    missingValueSets.Add(canonical);
                }
                return;
            }

            var expansions = await _context.TermValueSetExpansions
                .AsNoTracking()
                .Include(tvse => tvse.System)
                .Where(tvse => tvse.TermValueSetId == includedValueSet.TermValueSetId)
                .ToListAsync(cancellationToken);

            foreach (var entry in expansions)
            {
                var systemUri = entry.System.Value;
                await AddEntryAsync(entry.SystemId, systemUri, entry.Code!, entry.Display, entry.SystemVersion);
            }
        }

        async Task AddSystemAllCodesAsync(string systemUri, string? systemVersion)
        {
            // Always expand CodeSystems that are in our database
            // If the CodeSystem isn't imported, mark expansion as partial
            var systemId = await GetOrCreateSystemIdCachedAsync(systemUri, cancellationToken);

            var allCodes = await _context.TermConcepts
                .AsNoTracking()
                .Include(tc => tc.CodeSystem)
                .Where(tc => tc.CodeSystem.SystemId == systemId)
                .Where(tc => systemVersion == null || tc.CodeSystem.Version == systemVersion)
                .ToListAsync(cancellationToken);

            // Track external systems that have no locally imported codes
            if (allCodes.Count == 0 && !externalSystems.Contains(systemUri))
            {
                _logger.LogDebug("CodeSystem '{System}' has no imported concepts - marking expansion as partial", systemUri);
                externalSystems.Add(systemUri);
            }
            else if (allCodes.Count > 0)
            {
                _logger.LogDebug("Including all {Count} codes from CodeSystem '{System}' in expansion", allCodes.Count, systemUri);
            }

            foreach (var concept in allCodes)
            {
                await AddEntryAsync(systemId, systemUri, concept.Code, concept.Display, concept.CodeSystem.Version);
            }
        }

        async Task AddSystemCodesWithFiltersAsync(string systemUri, string? systemVersion, JsonArray filters)
        {
            var systemId = await GetOrCreateSystemIdCachedAsync(systemUri, cancellationToken);

            var candidates = await _context.TermConcepts
                .AsNoTracking()
                .Include(tc => tc.CodeSystem)
                .Where(tc => tc.CodeSystem.SystemId == systemId)
                .Where(tc => systemVersion == null || tc.CodeSystem.Version == systemVersion)
                .ToListAsync(cancellationToken);

            // Track external systems that have no locally imported codes
            if (candidates.Count == 0 && !externalSystems.Contains(systemUri))
            {
                _logger.LogDebug("CodeSystem '{System}' has no imported concepts to filter - marking expansion as partial", systemUri);
                externalSystems.Add(systemUri);
                return;
            }

            // Build lookup for ancestry checks
            var conceptById = candidates.ToDictionary(c => c.TermConceptId);

            bool IsDescendantOfCode(Entities.Terminology.TermConceptEntity concept, string ancestorCode, Dictionary<long, Entities.Terminology.TermConceptEntity> lookup)
            {
                var current = concept;
                var visited = new HashSet<long>();
                while (current != null)
                {
                    if (string.Equals(current.Code, ancestorCode, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (current.ParentConceptId == null || !lookup.TryGetValue(current.ParentConceptId.Value, out var parent) || !visited.Add(parent.TermConceptId))
                    {
                        break;
                    }
                    current = parent;
                }
                return false;
            }

            bool MatchesFilter(Entities.Terminology.TermConceptEntity concept, JsonObject filterObj)
            {
                var property = filterObj["property"]?.GetValue<string>();
                var op = filterObj["op"]?.GetValue<string>()?.ToUpperInvariant();
                var value = filterObj["value"]?.GetValue<string>();

                if (string.IsNullOrEmpty(property) || string.IsNullOrEmpty(op) || string.IsNullOrEmpty(value))
                {
                    return true; // ignore invalid filter
                }

                bool RegexMatch(string? target) => target != null && Regex.IsMatch(target, value, RegexOptions.IgnoreCase);

                switch (property)
                {
                    case "code":
                        return op switch
                        {
                            "=" => concept.Code == value,
                            "IN" => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(concept.Code),
                            "REGEX" => RegexMatch(concept.Code),
                            "IS-A" or "DESCENDENT-OF" => IsDescendantOfCode(concept, value, conceptById),
                            _ => true
                        };
                    case "display":
                        return op switch
                        {
                            "=" => string.Equals(concept.Display, value, StringComparison.Ordinal),
                            "CONTAINS" => concept.Display != null && concept.Display.Contains(value, StringComparison.OrdinalIgnoreCase),
                            "REGEX" => RegexMatch(concept.Display),
                            _ => true
                        };
                    default:
                        // Property-based filters: look into PropertiesJson for matching code/value
                        var (properties, _) = ParsePropertiesJson(concept.PropertiesJson);
                        var matchValue = properties?.Any(p =>
                            string.Equals(p.Code, property, StringComparison.OrdinalIgnoreCase) &&
                            p.Value != null &&
                            (op switch
                            {
                                "=" => string.Equals(p.Value, value, StringComparison.Ordinal),
                                "REGEX" => RegexMatch(p.Value),
                                "IN" => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(p.Value),
                                _ => false
                            }));
                        return matchValue == true;
                }
            }

            var filtered = candidates.Where(c =>
            {
                foreach (var filter in filters)
                {
                    var filterObj = filter?.AsObject();
                    if (filterObj == null)
                    {
                        continue;
                    }

                    if (!MatchesFilter(c, filterObj))
                    {
                        return false;
                    }
                }
                return true;
            }).ToList();

            foreach (var concept in filtered)
            {
                await AddEntryAsync(systemId, systemUri, concept.Code, concept.Display, concept.CodeSystem.Version);
            }
        }

        async Task ProcessIncludeAsync(JsonObject include)
        {
            var includeSystem = include["system"]?.GetValue<string>();
            var includeVersion = include["version"]?.GetValue<string>();
            var filters = include["filter"]?.AsArray();

            // Explicit concepts
            var concepts = include["concept"]?.AsArray();
            if (concepts != null && concepts.Count > 0)
            {
                foreach (var conceptNode in concepts)
                {
                    var concept = conceptNode?.AsObject();
                    if (concept == null)
                    {
                        continue;
                    }

                    var code = concept["code"]?.GetValue<string>();
                    var display = concept["display"]?.GetValue<string>();
                    var conceptSystem = concept["system"]?.GetValue<string>() ?? includeSystem;
                    if (string.IsNullOrEmpty(conceptSystem) || string.IsNullOrEmpty(code))
                    {
                        continue;
                    }

                    var systemId = await GetOrCreateSystemIdCachedAsync(conceptSystem, cancellationToken);
                    await AddEntryAsync(systemId, conceptSystem, code!, display, includeVersion);
                }
            }

            // Included ValueSets
            var includeValueSets = include["valueSet"]?.AsArray();
            if (includeValueSets != null)
            {
                foreach (var vs in includeValueSets)
                {
                    var canonical = vs?.GetValue<string>();
                    if (!string.IsNullOrEmpty(canonical))
                    {
                        await AddFromValueSetAsync(canonical!);
                    }
                }
            }

            // System without filters => include all codes
            if (includeSystem != null && filters != null && filters.Count > 0)
            {
                await AddSystemCodesWithFiltersAsync(includeSystem, includeVersion, filters);
            }
            else if (includeSystem != null && (concepts == null || concepts.Count == 0) && (includeValueSets == null || includeValueSets.Count == 0))
            {
                await AddSystemAllCodesAsync(includeSystem, includeVersion);
            }
        }

        async Task ProcessExcludeAsync(JsonObject exclude)
        {
            var excludeSystem = exclude["system"]?.GetValue<string>();
            var filters = exclude["filter"]?.AsArray();

            var concepts = exclude["concept"]?.AsArray();
            if (concepts != null && concepts.Count > 0 && excludeSystem != null)
            {
                var systemId = await _systemRepository.GetSystemIdAsync(excludeSystem, cancellationToken);
                if (systemId.HasValue)
                {
                    foreach (var conceptNode in concepts)
                    {
                        var code = conceptNode?["code"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(code))
                        {
                            excludedKeys.Add(MakeKey(systemId.Value, code!));
                        }
                    }
                }
            }

            var excludeValueSets = exclude["valueSet"]?.AsArray();
            if (excludeValueSets != null)
            {
                foreach (var vs in excludeValueSets)
                {
                    var canonical = vs?.GetValue<string>();
                    if (string.IsNullOrEmpty(canonical))
                    {
                        continue;
                    }

                    var excludedValueSet = await _context.TermValueSets
                        .AsNoTracking()
                        .Where(tvs => tvs.Canonical == canonical && tvs.IsExpanded)
                        .OrderByDescending(tvs => tvs.ImportedDate)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (excludedValueSet == null)
                    {
                        continue;
                    }

                    var expansions = await _context.TermValueSetExpansions
                        .AsNoTracking()
                        .Where(tvse => tvse.TermValueSetId == excludedValueSet.TermValueSetId)
                        .ToListAsync(cancellationToken);

                    foreach (var entry in expansions)
                    {
                        excludedKeys.Add(MakeKey(entry.SystemId, entry.Code!));
                    }
                }
            }

            if (excludeSystem != null && filters != null && filters.Count > 0)
            {
                var systemId = await _systemRepository.GetSystemIdAsync(excludeSystem, cancellationToken);
                if (systemId.HasValue)
                {
                    var query = _context.TermConcepts
                        .AsNoTracking()
                        .Include(tc => tc.CodeSystem)
                        .Where(tc => tc.CodeSystem.SystemId == systemId.Value);

                    foreach (var filter in filters)
                    {
                        var filterObj = filter?.AsObject();
                        if (filterObj == null)
                        {
                            continue;
                        }

                        var property = filterObj["property"]?.GetValue<string>();
                        var op = filterObj["op"]?.GetValue<string>();
                        var value = filterObj["value"]?.GetValue<string>();

                        if (string.IsNullOrEmpty(property) || string.IsNullOrEmpty(op) || string.IsNullOrEmpty(value))
                        {
                            continue;
                        }

                        switch (property)
                        {
                            case "code":
                                if (op == "=")
                                {
                                    query = query.Where(tc => tc.Code == value);
                                }
                                else if (op == "in")
                                {
                                    var codes = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                    query = query.Where(tc => codes.Contains(tc.Code));
                                }
                                break;
                            case "display":
                                if (op == "=")
                                {
                                    query = query.Where(tc => tc.Display == value);
                                }
                                else if (op == "contains")
                                {
                                    query = query.Where(tc => tc.Display != null && EF.Functions.Like(tc.Display, $"%{value}%"));
                                }
                                break;
                            default:
                                break;
                        }
                    }

                    var filtered = await query.ToListAsync(cancellationToken);
                    foreach (var concept in filtered)
                    {
                        excludedKeys.Add(MakeKey(systemId.Value, concept.Code));
                    }
                }
            }
            else if (excludeSystem != null && (concepts == null || concepts.Count == 0) && (excludeValueSets == null || excludeValueSets.Count == 0))
            {
                var systemId = await _systemRepository.GetSystemIdAsync(excludeSystem, cancellationToken);
                if (systemId.HasValue)
                {
                    excludedKeys.Add(MakeKey(systemId.Value, "*"));
                }
            }
        }

        var includes = compose["include"]?.AsArray();
        if (includes != null)
        {
            foreach (var includeNode in includes)
            {
                var include = includeNode?.AsObject();
                if (include != null)
                {
                    await ProcessIncludeAsync(include);
                }
            }
        }

        var excludes = compose["exclude"]?.AsArray();
        if (excludes != null)
        {
            foreach (var excludeNode in excludes)
            {
                var excludeObj = excludeNode?.AsObject();
                if (excludeObj != null)
                {
                    await ProcessExcludeAsync(excludeObj);
                }
            }
        }

        // Apply exclusions and renumber ordinals
        var filtered = new List<TermValueSetExpansionEntity>();
        foreach (var entry in includeEntries)
        {
            var key = MakeKey(entry.SystemId, entry.Code!);
            if (excludedKeys.Contains(key) || excludedKeys.Contains(MakeKey(entry.SystemId, "*")))
            {
                continue;
            }

            entry.Ordinal = filtered.Count;
            filtered.Add(entry);
        }

        if (filtered.Count > 0)
        {
            _context.TermValueSetExpansions.AddRange(filtered);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var isPartial = externalSystems.Count > 0 || missingValueSets.Count > 0;
        return new ComposeExpansionResult(
            ImportedCount: filtered.Count,
            IsPartial: isPartial,
            ExternalSystems: externalSystems,
            MissingValueSets: missingValueSets);
    }

    /// <summary>
    /// Metadata extracted from CodeSystem JSON.
    /// </summary>
    private class CodeSystemMetadata
    {
        public required string Url { get; init; }
        public string? Version { get; init; }
        public required string Content { get; init; }
        public int? Count { get; init; }
        public bool CaseSensitive { get; init; }
        public string? HierarchyMeaning { get; init; }
        public bool Compositional { get; init; }

        public bool IsHierarchical => HierarchyMeaning is "is-a" or "part-of" or "classified-with";
    }

    /// <summary>
    /// Metadata extracted from ValueSet JSON.
    /// </summary>
    private class ValueSetMetadata
    {
        public required string Url { get; init; }
        public string? Version { get; init; }
        public required string Name { get; init; }
        public bool Immutable { get; init; }
    }

    /// <summary>
    /// Parses ConceptMap JSON string into a JsonObject.
    /// </summary>
    private JsonObject ParseConceptMapJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        if (node == null)
        {
            throw new InvalidOperationException("Failed to parse ConceptMap JSON (null result)");
        }

        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException($"Expected JSON object, got {node.GetType().Name}");
        }

        string? resourceType = obj["resourceType"]?.GetValue<string>();
        if (resourceType != "ConceptMap")
        {
            throw new InvalidOperationException($"Expected resourceType 'ConceptMap', got '{resourceType}'");
        }

        return obj;
    }

    /// <summary>
    /// Extracts metadata from ConceptMap JSON.
    /// </summary>
    private ConceptMapMetadata ExtractConceptMapMetadata(JsonObject conceptMap)
    {
        // Try both R4 and R5 variants for source/target
        string? sourceCanonical = conceptMap["sourceUri"]?.GetValue<string>()
            ?? conceptMap["sourceCanonical"]?.GetValue<string>();
        string? targetCanonical = conceptMap["targetUri"]?.GetValue<string>()
            ?? conceptMap["targetCanonical"]?.GetValue<string>();

        return new ConceptMapMetadata
        {
            Url = conceptMap["url"]?.GetValue<string>() ?? throw new InvalidOperationException("ConceptMap.url is required"),
            Version = conceptMap["version"]?.GetValue<string>(),
            Name = conceptMap["name"]?.GetValue<string>() ?? throw new InvalidOperationException("ConceptMap.name is required"),
            SourceCanonical = sourceCanonical,
            TargetCanonical = targetCanonical
        };
    }

    /// <summary>
    /// Imports mapping elements from ConceptMap.group into TermConceptMapElement table.
    /// </summary>
    private async Task<int> ImportConceptMapElements(
        JsonObject conceptMap,
        long termConceptMapId,
        CancellationToken cancellationToken)
    {
        var groups = conceptMap["group"]?.AsArray();
        if (groups == null || groups.Count == 0)
        {
            return 0;
        }

        int totalImported = 0;

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex]?.AsObject();
            if (group == null)
            {
                continue;
            }

            string? groupSource = group["source"]?.GetValue<string>();
            string? groupTarget = group["target"]?.GetValue<string>();

            // Get SystemIds for group source/target
            int groupSourceSystemId = 0;
            if (!string.IsNullOrEmpty(groupSource))
            {
                groupSourceSystemId = await _systemRepository.GetOrCreateAsync(groupSource, cancellationToken);
            }

            int groupTargetSystemId = 0;
            if (!string.IsNullOrEmpty(groupTarget))
            {
                groupTargetSystemId = await _systemRepository.GetOrCreateAsync(groupTarget, cancellationToken);
            }

            var elements = group["element"]?.AsArray();
            if (elements == null || elements.Count == 0)
            {
                continue;
            }

            var mappingElements = new List<TermConceptMapElementEntity>();

            foreach (var element in elements)
            {
                var elementObj = element?.AsObject();
                if (elementObj == null)
                {
                    continue;
                }

                string? sourceCode = elementObj["code"]?.GetValue<string>();
                string? sourceDisplay = elementObj["display"]?.GetValue<string>();

                if (string.IsNullOrEmpty(sourceCode))
                {
                    _logger.LogWarning("ConceptMap element missing source code, skipping");
                    continue;
                }

                // Process targets
                var targets = elementObj["target"]?.AsArray();
                if (targets != null && targets.Count > 0)
                {
                    foreach (var target in targets)
                    {
                        var targetObj = target?.AsObject();
                        if (targetObj == null)
                        {
                            continue;
                        }

                        string? targetCode = targetObj["code"]?.GetValue<string>();
                        string? targetDisplay = targetObj["display"]?.GetValue<string>();
                        string? equivalence = targetObj["equivalence"]?.GetValue<string>();
                        string? comment = targetObj["comment"]?.GetValue<string>();

                        // equivalence defaults to "equivalent" if not specified (FHIR spec)
                        equivalence ??= "equivalent";

                        mappingElements.Add(new TermConceptMapElementEntity
                        {
                            TermConceptMapId = termConceptMapId,
                            SourceSystemId = groupSourceSystemId,
                            SourceCode = sourceCode,
                            SourceDisplay = sourceDisplay,
                            TargetSystemId = targetCode != null ? groupTargetSystemId : null,
                            TargetCode = targetCode,
                            TargetDisplay = targetDisplay,
                            Equivalence = equivalence,
                            Comment = comment,
                            GroupIndex = groupIndex
                        });
                    }
                }
                else
                {
                    // Element with no target (unmapped code)
                    mappingElements.Add(new TermConceptMapElementEntity
                    {
                        TermConceptMapId = termConceptMapId,
                        SourceSystemId = groupSourceSystemId,
                        SourceCode = sourceCode,
                        SourceDisplay = sourceDisplay,
                        TargetSystemId = null,
                        TargetCode = null,
                        TargetDisplay = null,
                        Equivalence = "unmatched",
                        Comment = null,
                        GroupIndex = groupIndex
                    });
                }
            }

            if (mappingElements.Count > 0)
            {
                _context.TermConceptMapElements.AddRange(mappingElements);
                await _context.SaveChangesAsync(cancellationToken);
                totalImported += mappingElements.Count;
            }
        }

        return totalImported;
    }

    /// <summary>
    /// Metadata extracted from ConceptMap JSON.
    /// </summary>
    private class ConceptMapMetadata
    {
        public required string Url { get; init; }
        public string? Version { get; init; }
        public required string Name { get; init; }
        public string? SourceCanonical { get; init; }
        public string? TargetCanonical { get; init; }
    }

    /// <summary>
    /// Parses PropertiesJson string into PropertyValue and Designation collections.
    /// Format: { "property": [...], "designation": [...] }
    /// </summary>
    private (IReadOnlyList<PropertyValue>?, IReadOnlyList<Designation>?) ParsePropertiesJson(string? propertiesJson)
    {
        if (string.IsNullOrEmpty(propertiesJson))
        {
            return (null, null);
        }

        try
        {
            var json = JsonNode.Parse(propertiesJson)?.AsObject();
            if (json == null)
            {
                return (null, null);
            }

            // Parse properties
            List<PropertyValue>? properties = null;
            var propertyArray = json["property"]?.AsArray();
            if (propertyArray != null && propertyArray.Count > 0)
            {
                properties = new List<PropertyValue>();
                foreach (var prop in propertyArray)
                {
                    var code = prop?["code"]?.GetValue<string>();
                    var value = prop?["valueString"]?.GetValue<string>()
                        ?? prop?["valueCode"]?.GetValue<string>()
                        ?? prop?["valueCoding"]?["code"]?.GetValue<string>()
                        ?? prop?["valueBoolean"]?.GetValue<bool>().ToString();

                    if (code != null)
                    {
                        properties.Add(new PropertyValue(code, value));
                    }
                }
            }

            // Parse designations
            List<Designation>? designations = null;
            var designationArray = json["designation"]?.AsArray();
            if (designationArray != null && designationArray.Count > 0)
            {
                designations = new List<Designation>();
                foreach (var desig in designationArray)
                {
                    var language = desig?["language"]?.GetValue<string>();
                    var use = desig?["use"]?["code"]?.GetValue<string>();
                    var value = desig?["value"]?.GetValue<string>();

                    if (value != null)
                    {
                        designations.Add(new Designation(language, use, value));
                    }
                }
            }

            return (properties, designations);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse PropertiesJson: {ex.Message}");
            return (null, null);
        }
    }
}
