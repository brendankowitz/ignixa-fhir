// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Specification;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Imports a batch of resources using BatchWriteAsync for optimal performance.
/// Uses same batching pattern as bundle transactions but optimized for bulk import.
/// Phase 4: Uses BatchWriteAsync instead of individual writes (10-100x faster).
/// Phase 5: Supports import modes (InitialLoad/IncrementalLoad).
///
/// Import Modes:
/// - **IncrementalLoad** (default): Standard mode for ongoing data updates.
///   Server auto-assigns version IDs, performs full validation.
///
/// - **InitialLoad**: Optimized mode for initial bulk data loading.
///   Currently uses same BatchWriteAsync path (already optimized).
///   Future: May skip certain validations, preserve source version IDs.
/// </summary>
public class ImportBatchActivity : AsyncTaskActivity<ImportBatchInput, ImportBatchOutput>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly ILogger<ImportBatchActivity> _logger;

    public ImportBatchActivity(
        IFhirRepositoryFactory repositoryFactory,
        IFhirVersionContext fhirVersionContext,
        ITenantConfigurationStore tenantConfigurationStore,
        ILogger<ImportBatchActivity> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<ImportBatchOutput> ExecuteAsync(
        TaskContext context,
        ImportBatchInput input)
    {
        // Validate import mode
        if (input.Mode != "InitialLoad" && input.Mode != "IncrementalLoad")
        {
            _logger.LogError("Invalid import mode: {Mode}. Supported modes: InitialLoad, IncrementalLoad", input.Mode);
            return new ImportBatchOutput
            {
                SuccessCount = 0,
                ErrorCount = input.Resources.Count,
                Errors = new List<ImportErrorLogEntry>
                {
                    new ImportErrorLogEntry
                    {
                        ResourceType = input.ResourceType,
                        ResourceId = "N/A",
                        ErrorCode = "InvalidMode",
                        ErrorMessage = $"Invalid import mode: {input.Mode}. Supported modes: InitialLoad, IncrementalLoad",
                        ResourceJson = string.Empty
                    }
                }
            };
        }

        _logger.LogInformation(
            "Importing batch of {ResourceCount} {ResourceType} resources (mode: {Mode}) using BatchWriteAsync",
            input.Resources.Count,
            input.ResourceType,
            input.Mode);

        // Get tenant configuration to determine FHIR version
        var tenantConfig = await _tenantConfigurationStore.GetTenantConfigurationAsync(
            input.TenantId,
            CancellationToken.None);

        if (tenantConfig == null)
        {
            throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive");
        }

        // Get tenant repository
        var repository = await _repositoryFactory.GetRepositoryAsync(input.TenantId, CancellationToken.None);

        // Allocate transaction ID from tenant's repository (Isolated mode)
        // In isolated mode: Each tenant has its own database, so tenant repository = tenant database
        // In distributed mode: Must use system repository (Partition 0) for transaction allocation
        //                      TODO: Repository factory should handle this logic based on mode
        var transactionId = await repository.GetNextTransactionIdAsync(CancellationToken.None);

        _logger.LogDebug(
            "Allocated transaction ID {TransactionId} for import batch (Tenant {TenantId})",
            transactionId,
            input.TenantId);

        // 3. Parse and validate resources, build batch operations
        var operations = new List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>();
        var errors = new List<ImportErrorLogEntry>();
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);

        var schemaProvider = _fhirVersionContext.GetSchemaProvider(fhirVersion, input.TenantId);
        var searchIndexer = _fhirVersionContext.GetSearchIndexer(fhirVersion, input.TenantId);

        for (int entryIndex = 0; entryIndex < input.Resources.Count; entryIndex++)
        {
            var resourceJson = input.Resources[entryIndex];
            try
            {
                // Parse JSON to ResourceJsonNode
                var jsonNode = JsonSourceNodeFactory.Parse(resourceJson);

                // Validate resource type matches expected type
                if (jsonNode.ResourceType != input.ResourceType)
                {
                    errors.Add(new ImportErrorLogEntry
                    {
                        ResourceType = input.ResourceType,
                        ResourceId = jsonNode.Id ?? "unknown",
                        ErrorCode = "InvalidResourceType",
                        ErrorMessage = $"Expected {input.ResourceType}, got {jsonNode.ResourceType}",
                        ResourceJson = resourceJson
                    });
                    continue;
                }

                // Generate ID if missing
                var resourceId = jsonNode.Id;
                if (string.IsNullOrEmpty(resourceId))
                {
                    resourceId = Guid.NewGuid().ToString("N");
                    jsonNode.Id = resourceId;
                    _logger.LogDebug("Generated ID for {ResourceType}: {Id}", input.ResourceType, resourceId);
                }

                // Extract search indices (best effort)
                IReadOnlyList<object> searchIndices = Array.Empty<object>();
                try
                {
                    var typedElement = jsonNode.ToElement(schemaProvider);
                    var indices = searchIndexer.Extract((IElement)typedElement);
                    searchIndices = indices.ToArray();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to extract search indices for {ResourceType}/{Id}, skipping indexing",
                        input.ResourceType,
                        resourceId);
                }

                // Add to batch operations with entry index for surrogate ID calculation
                operations.Add((input.ResourceType, resourceId, jsonNode, searchIndices, "PUT", entryIndex)); // Import uses PUT (upsert)
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing resource");
                errors.Add(new ImportErrorLogEntry
                {
                    ResourceType = input.ResourceType,
                    ResourceId = "unknown",
                    ErrorCode = "ParseError",
                    ErrorMessage = ex.Message,
                    ResourceJson = resourceJson
                });
            }
        }

        // 4. Execute batch write if we have valid operations
        var successCount = 0;
        if (operations.Count > 0)
        {
            try
            {
                _logger.LogDebug(
                    "Executing BatchWriteAsync for {Count} resources with transaction {TransactionId}",
                    operations.Count,
                    transactionId);

                var keys = await repository.BatchWriteAsync(transactionId, operations, CancellationToken.None);

                // 5. Commit transaction
                await repository.CommitTransactionAsync(transactionId, CancellationToken.None);

                successCount = keys.Count;

                _logger.LogInformation(
                    "Batch write completed: {SuccessCount} resources written",
                    successCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch write");

                // Mark all operations as failed
                foreach (var op in operations)
                {
                    errors.Add(new ImportErrorLogEntry
                    {
                        ResourceType = op.resourceType,
                        ResourceId = op.resourceId,
                        ErrorCode = "BatchWriteError",
                        ErrorMessage = ex.Message,
                        ResourceJson = op.resource.SerializeToString()
                    });
                }
            }
        }

        var errorCount = errors.Count;

        _logger.LogInformation(
            "Import batch completed: {SuccessCount} success, {ErrorCount} errors",
            successCount,
            errorCount);

        return new ImportBatchOutput
        {
            SuccessCount = successCount,
            ErrorCount = errorCount,
            Errors = errors
        };
    }

}
