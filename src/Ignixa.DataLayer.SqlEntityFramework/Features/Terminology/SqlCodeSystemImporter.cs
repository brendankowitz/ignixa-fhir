// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Text.Json.Nodes;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

    public SqlCodeSystemImporter(
        FhirDbContext context,
        ISystemRepository systemRepository,
        ILogger<SqlCodeSystemImporter> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _systemRepository = systemRepository ?? throw new ArgumentNullException(nameof(systemRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TerminologyImportResult> ImportCodeSystemAsync(
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

        // CRITICAL FIX #2: Import concurrency control
        // Check if another thread is already importing this resource
        // Reload from database to get latest status (avoid stale data from PackageResource parameter)
        var currentStatus = await _context.PackageResources
            .Where(pr => pr.PackageResourceId == packageResource.PackageResourceId)
            .Select(pr => pr.TerminologyImportStatus)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentStatus == nameof(TerminologyImportStatus.InProgress))
        {
            _logger.LogInformation(
                "Import already in progress for '{Canonical}' (PackageResourceId: {PackageResourceId}), skipping",
                packageResource.Canonical,
                packageResource.PackageResourceId);

            return TerminologyImportResult.CreateSkipped();
        }

        try
        {
            // 1. Parse CodeSystem JSON
            JsonObject codeSystem = ParseCodeSystemJson(packageResource.ResourceJson);

            // 2. Check content hash (skip if unchanged)
            string newContentHash = packageResource.ComputeContentHash();
            if (packageResource.ContentHash == newContentHash &&
                packageResource.TerminologyImportStatus == TerminologyImportStatus.Completed)
            {
                _logger.LogInformation(
                    "CodeSystem '{Canonical}' content unchanged (hash: {Hash}), skipping import",
                    packageResource.Canonical,
                    newContentHash);

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

                packageResource.TerminologyImportStatus = TerminologyImportStatus.Skipped;
                packageResource.ContentHash = newContentHash;
                packageResource.ImportStartDate = DateTimeOffset.UtcNow;
                packageResource.ImportCompletedDate = DateTimeOffset.UtcNow;
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

                packageResource.TerminologyImportStatus = TerminologyImportStatus.Skipped;
                packageResource.ContentHash = newContentHash;
                packageResource.ImportStartDate = DateTimeOffset.UtcNow;
                packageResource.ImportCompletedDate = DateTimeOffset.UtcNow;
                packageResource.ImportErrorMessage = "Supplement import not yet implemented";
                await _context.SaveChangesAsync(cancellationToken);

                return TerminologyImportResult.CreateSkipped();
            }

            // 4. Get or create SystemId
            int systemId = await _systemRepository.GetOrCreateAsync(metadata.Url, cancellationToken);

            // 5. Begin transaction
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Update import status to InProgress
                packageResource.TerminologyImportStatus = TerminologyImportStatus.InProgress;
                packageResource.ImportStartDate = DateTimeOffset.UtcNow;
                packageResource.ContentHash = newContentHash;
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
                packageResource.TerminologyImportStatus = TerminologyImportStatus.Completed;
                packageResource.ImportCompletedDate = DateTimeOffset.UtcNow;
                packageResource.ImportedConceptCount = concepts.Count;
                packageResource.ImportErrorMessage = null;

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
            packageResource.TerminologyImportStatus = TerminologyImportStatus.Failed;
            packageResource.ImportCompletedDate = DateTimeOffset.UtcNow;
            packageResource.ImportErrorMessage = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

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

        // Check if another thread is already importing this resource
        var currentStatus = await _context.PackageResources
            .Where(pr => pr.PackageResourceId == packageResource.PackageResourceId)
            .Select(pr => pr.TerminologyImportStatus)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentStatus == nameof(TerminologyImportStatus.InProgress))
        {
            _logger.LogInformation(
                "Import already in progress for '{Canonical}' (PackageResourceId: {PackageResourceId}), skipping",
                packageResource.Canonical,
                packageResource.PackageResourceId);

            return TerminologyImportResult.CreateSkipped();
        }

        try
        {
            // 1. Parse ValueSet JSON
            JsonObject valueSet = ParseValueSetJson(packageResource.ResourceJson);

            // 2. Check content hash (skip if unchanged)
            string newContentHash = packageResource.ComputeContentHash();
            if (packageResource.ContentHash == newContentHash &&
                packageResource.TerminologyImportStatus == TerminologyImportStatus.Completed)
            {
                _logger.LogInformation(
                    "ValueSet '{Canonical}' content unchanged (hash: {Hash}), skipping import",
                    packageResource.Canonical,
                    newContentHash);

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

            // 4. Begin transaction
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Update import status to InProgress
                packageResource.TerminologyImportStatus = TerminologyImportStatus.InProgress;
                packageResource.ImportStartDate = DateTimeOffset.UtcNow;
                packageResource.ContentHash = newContentHash;
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

                // 7. Import expansion entries if present
                int importedCount = 0;
                var expansion = valueSet["expansion"];
                if (expansion != null)
                {
                    importedCount = await ImportExpansionEntries(
                        expansion.AsObject(),
                        termValueSet.TermValueSetId,
                        cancellationToken);

                    if (importedCount > 0)
                    {
                        termValueSet.IsExpanded = true;
                        termValueSet.LastExpansionDate = DateTimeOffset.UtcNow;
                        termValueSet.ExpansionCodeCount = importedCount;
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }

                // 8. Update PackageResource import status
                packageResource.TerminologyImportStatus = TerminologyImportStatus.Completed;
                packageResource.ImportCompletedDate = DateTimeOffset.UtcNow;
                packageResource.ImportedConceptCount = importedCount;
                packageResource.ImportErrorMessage = null;
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
            packageResource.TerminologyImportStatus = TerminologyImportStatus.Failed;
            packageResource.ImportCompletedDate = DateTimeOffset.UtcNow;
            packageResource.ImportErrorMessage = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

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

        // Check if another thread is already importing this resource
        var currentStatus = await _context.PackageResources
            .Where(pr => pr.PackageResourceId == packageResource.PackageResourceId)
            .Select(pr => pr.TerminologyImportStatus)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentStatus == nameof(TerminologyImportStatus.InProgress))
        {
            _logger.LogInformation(
                "Import already in progress for '{Canonical}' (PackageResourceId: {PackageResourceId}), skipping",
                packageResource.Canonical,
                packageResource.PackageResourceId);

            return TerminologyImportResult.CreateSkipped();
        }

        try
        {
            // 1. Parse ConceptMap JSON
            JsonObject conceptMap = ParseConceptMapJson(packageResource.ResourceJson);

            // 2. Check content hash (skip if unchanged)
            string newContentHash = packageResource.ComputeContentHash();
            if (packageResource.ContentHash == newContentHash &&
                packageResource.TerminologyImportStatus == TerminologyImportStatus.Completed)
            {
                _logger.LogInformation(
                    "ConceptMap '{Canonical}' content unchanged (hash: {Hash}), skipping import",
                    packageResource.Canonical,
                    newContentHash);

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

            // 4. Begin transaction
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Update import status to InProgress
                packageResource.TerminologyImportStatus = TerminologyImportStatus.InProgress;
                packageResource.ImportStartDate = DateTimeOffset.UtcNow;
                packageResource.ContentHash = newContentHash;
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
                packageResource.TerminologyImportStatus = TerminologyImportStatus.Completed;
                packageResource.ImportCompletedDate = DateTimeOffset.UtcNow;
                packageResource.ImportedConceptCount = importedCount;
                packageResource.ImportErrorMessage = null;
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
            packageResource.TerminologyImportStatus = TerminologyImportStatus.Failed;
            packageResource.ImportCompletedDate = DateTimeOffset.UtcNow;
            packageResource.ImportErrorMessage = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

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

            // Get SystemId (0 if system is null/empty)
            int systemId = 0;
            if (!string.IsNullOrEmpty(system))
            {
                systemId = await _systemRepository.GetOrCreateAsync(system, cancellationToken);
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
}
