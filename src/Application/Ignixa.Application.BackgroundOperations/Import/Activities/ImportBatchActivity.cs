// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.Common;
using System.Text.Json;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
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
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;
    private readonly ILogger<ImportBatchActivity> _logger;

    public ImportBatchActivity(
        IFhirRepositoryFactory repositoryFactory,
        IFhirVersionContext fhirVersionContext,
        ITenantConfigurationStore tenantConfigurationStore,
        IFhirRequestContextAccessor fhirContextAccessor,
        ILogger<ImportBatchActivity> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _fhirContextAccessor = fhirContextAccessor ?? throw new ArgumentNullException(nameof(fhirContextAccessor));
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

        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);

        // Establish the ambient request context for the duration of the activity so search indexing
        // resolves the same tenant-scoped base URIs the HTTP request path would have. Without this,
        // self-references are indexed as external here while the request path that first wrote them
        // collapsed them to internal, and the resource silently drops out of absolute-reference searches.
        var previousContext = _fhirContextAccessor.RequestContext;
        _fhirContextAccessor.RequestContext = FhirRequestContextFactory.CreateBackgroundContext(
            input.TenantId,
            tenantConfig,
            fhirVersion,
            input.ResourceType);

        try
        {
            // Get tenant repository
            var repository = await _repositoryFactory.GetRepositoryAsync(input.TenantId, CancellationToken.None);

            // 3. Parse and validate resources, build batch operations
            var operations = new List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>();
            var errors = new List<ImportErrorLogEntry>();

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

                    var typedElement = jsonNode.ToElement(schemaProvider);
                    IReadOnlyList<object> searchIndices = searchIndexer.Extract((IElement)typedElement).ToArray();

                    // Add to batch operations with entry index for surrogate ID calculation
                    operations.Add((input.ResourceType, resourceId, jsonNode, searchIndices, "PUT", entryIndex)); // Import uses PUT (upsert)
                }
                catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
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
                var transactionId = await repository.GetNextTransactionIdAsync(CancellationToken.None);
                _logger.LogDebug(
                    "Executing BatchWriteAsync for {Count} resources with transaction {TransactionId}",
                    operations.Count, transactionId);
                var keys = await repository.BatchWriteAsync(transactionId, operations, CancellationToken.None);
                try
                {
                    await repository.CommitTransactionAsync(transactionId, CancellationToken.None);
                }
                catch (Exception ex) when (ex is IOException or DbException or TimeoutException or InvalidOperationException or OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"Import commit outcome is indeterminate for transaction {transactionId.Value} ({operations.Count} resources). {ex.Message}", ex);
                }
                successCount = keys.Count;
                _logger.LogInformation("Batch write completed: {SuccessCount} resources written", successCount);
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
        finally
        {
            _fhirContextAccessor.RequestContext = previousContext;
        }
    }

}
