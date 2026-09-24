// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Streams a single NDJSON file through a Channel-based pipeline for high-throughput import.
/// Architecture matches streaming export (ExportWorkerActivity) and bundle processing (BundleChannelExecutor).
///
/// Pipeline:
/// 1. Producer thread: Downloads file and reads line-by-line, writes to Channel
/// 2. Consumer threads (8): Read from Channel, batch resources (100), execute BatchWriteAsync
/// 3. Bounded channel provides backpressure when consumers are slower than producer
///
/// Performance:
/// - Single file: 8 consumers × 100 resources/batch × 2.5 batches/sec ≈ 2,000 resources/sec
/// - Multi-file: Orchestration runs multiple instances in parallel for >10K resources/sec
/// </summary>
public class StreamingImportFileActivity : AsyncTaskActivity<StreamingImportFileInput, StreamingImportFileOutput>
{
    // GetNextTransactionIdAsync reserves 1,000 surrogate IDs in the SQL repository.
    private const int MaxBatchSize = 1000;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IBlobStorageClient _blobStorageClient;
    private readonly IConfiguration _configuration;
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;
    private readonly ILogger<StreamingImportFileActivity> _logger;

    public StreamingImportFileActivity(
        IFhirRepositoryFactory repositoryFactory,
        IFhirVersionContext fhirVersionContext,
        ITenantConfigurationStore tenantConfigurationStore,
        IBlobStorageClient blobStorageClient,
        IConfiguration configuration,
        IFhirRequestContextAccessor fhirContextAccessor,
        ILogger<StreamingImportFileActivity> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _blobStorageClient = blobStorageClient ?? throw new ArgumentNullException(nameof(blobStorageClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _fhirContextAccessor = fhirContextAccessor ?? throw new ArgumentNullException(nameof(fhirContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<StreamingImportFileOutput> ExecuteAsync(
        TaskContext context,
        StreamingImportFileInput input)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting streaming import: Job={JobId}, File={FileUrl}, Type={ResourceType}",
            input.JobId,
            input.FileUrl,
            input.ResourceType);

        // Validate import mode
        if (input.Mode != "InitialLoad" && input.Mode != "IncrementalLoad")
        {
            throw new InvalidOperationException(
                $"Invalid import mode: {input.Mode}. Supported modes: InitialLoad, IncrementalLoad");
        }

        var errors = new List<ImportErrorLogEntry>();
        var successCount = 0;

        // Establish the ambient request context for the duration of the activity so search indexing
        // resolves the same tenant-scoped base URIs the HTTP request path would have. Without this,
        // self-references are indexed as external here while the request path that first wrote them
        // collapsed them to internal, and the resource silently drops out of absolute-reference searches.
        var previousContext = _fhirContextAccessor.RequestContext;
        _fhirContextAccessor.RequestContext = FhirRequestContextFactory.CreateBackgroundContext(
            input.TenantId,
            resourceType: input.ResourceType);

        try
        {
            // Get tenant configuration to determine FHIR version
            var tenantConfig = await _tenantConfigurationStore.GetTenantConfigurationAsync(
                input.TenantId,
                CancellationToken.None);

            if (tenantConfig == null)
            {
                throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive");
            }

            // Get FHIR schema and indexer using tenant's configured FHIR version
            var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            var schemaProvider = _fhirVersionContext.GetSchemaProvider(fhirVersion, input.TenantId);
            var searchIndexer = _fhirVersionContext.GetSearchIndexer(fhirVersion, input.TenantId);

            // Read global configuration for consumer count
            var consumerCount = _configuration.GetValue<int>("Import:ConsumerCount", 8);
            if (consumerCount < 1) consumerCount = 1;
            var batchSize = Math.Clamp(input.BatchSize, 1, MaxBatchSize);

            // Create bounded channel for streaming resources from producer to consumers
            var channel = Channel.CreateBounded<ImportEntry>(
                new BoundedChannelOptions(input.ChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,  // Backpressure: producer waits if consumers are slow
                    SingleReader = false,  // Multiple consumers
                    SingleWriter = true    // Single producer
                });
            using var pipelineCancellation = new CancellationTokenSource();

            // Shared state for tracking results across consumers
            var successCountLock = new object();
            var errorsLock = new object();

            // Producer task: Download file and stream resources to channel
            var producerTask = Task.Run(async () =>
            {
                var completed = false;
                try
                {
                    await ProduceResourcesAsync(input.FileUrl, channel.Writer, pipelineCancellation.Token);
                    completed = true;
                }
                catch (OperationCanceledException) when (pipelineCancellation.IsCancellationRequested)
                {
                    // A failed consumer stops the producer; Task.WhenAll still propagates that failure.
                }
                finally
                {
                    channel.Writer.TryComplete();
                    if (!completed)
                    {
                        await pipelineCancellation.CancelAsync();
                    }
                    _logger.LogDebug("Producer completed for file {FileUrl}", input.FileUrl);
                }
            }, CancellationToken.None);

            // Consumer tasks: Read from channel, batch, and write to database
            // ConsumerCount is a global setting read from configuration
            // Each consumer gets its own repository instance (DbContext is not thread-safe)
            var consumerTasks = Enumerable.Range(0, consumerCount)
                .Select(consumerId => Task.Run(async () =>
                {
                    var localSuccessCount = 0;
                    var localErrors = new List<ImportErrorLogEntry>();

                    var completed = false;
                    try
                    {
                        var consumerRepository = await _repositoryFactory.GetRepositoryAsync(input.TenantId, CancellationToken.None);
                        await foreach (var entry in channel.Reader.ReadAllAsync(pipelineCancellation.Token))
                        {
                            pipelineCancellation.Token.ThrowIfCancellationRequested();
                            var batchOperations = new List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>();

                            // Add current entry to batch
                            batchOperations.Add(PrepareResource(
                                entry,
                                input.ResourceType,
                                schemaProvider,
                                searchIndexer,
                                localErrors));

                            // Try to fill batch with more entries (non-blocking)
                            while (batchOperations.Count < batchSize &&
                                   channel.Reader.TryRead(out var nextEntry))
                            {
                                batchOperations.Add(PrepareResource(
                                    nextEntry,
                                    input.ResourceType,
                                    schemaProvider,
                                    searchIndexer,
                                    localErrors));
                            }

                            // Filter out null entries (parse errors)
                            var validOperations = batchOperations
                                .Where(op => op.resource != null!)
                                .Select((op, index) => (op.resourceType, op.resourceId, op.resource, op.searchIndexes, op.httpMethod, entryIndex: index))
                                .ToList();

                            if (validOperations.Count > 0)
                            {
                                pipelineCancellation.Token.ThrowIfCancellationRequested();
                                _logger.LogDebug(
                                    "Consumer {ConsumerId} executing batch of {Count} resources",
                                    consumerId, validOperations.Count);

                                // A file can outgrow a reserved range, and blank lines are not resource offsets.
                                var transactionId = await consumerRepository.GetNextTransactionIdAsync(CancellationToken.None);
                                var keys = await consumerRepository.BatchWriteAsync(
                                    transactionId, validOperations, CancellationToken.None);
                                try
                                {
                                    await consumerRepository.CommitTransactionAsync(transactionId, CancellationToken.None);
                                }
                                catch (Exception ex) when (ex is IOException or DbException or TimeoutException or InvalidOperationException or OperationCanceledException)
                                {
                                    throw new InvalidOperationException(
                                        $"Import commit outcome is indeterminate for transaction {transactionId.Value} ({validOperations.Count} resources). {ex.Message}", ex);
                                }
                                localSuccessCount += keys.Count;
                                _logger.LogDebug(
                                    "Consumer {ConsumerId} completed batch: {Count} resources written",
                                    consumerId, keys.Count);
                            }
                        }
                        completed = true;
                    }
                    catch (OperationCanceledException) when (pipelineCancellation.IsCancellationRequested)
                    {
                        // Another pipeline task failed; do not start more writes.
                    }
                    finally
                    {
                        if (!completed)
                        {
                            await pipelineCancellation.CancelAsync();
                        }
                        // Aggregate local results into shared state
                        lock (successCountLock)
                        {
                            successCount += localSuccessCount;
                        }

                        if (localErrors.Count > 0)
                        {
                            lock (errorsLock)
                            {
                                errors.AddRange(localErrors);
                            }
                        }

                        _logger.LogDebug(
                            "Consumer {ConsumerId} completed: {SuccessCount} success, {ErrorCount} errors",
                            consumerId,
                            localSuccessCount,
                            localErrors.Count);
                    }
                }, CancellationToken.None))
                .ToArray();

            // Wait for producer and all consumers to complete
            await Task.WhenAll(consumerTasks.Append(producerTask));

            stopwatch.Stop();

            _logger.LogInformation(
                "Streaming import completed: Job={JobId}, File={FileUrl}, Success={SuccessCount}, Errors={ErrorCount}, Duration={Duration:F2}s, Throughput={Throughput:F0} resources/sec",
                input.JobId,
                input.FileUrl,
                successCount,
                errors.Count,
                stopwatch.Elapsed.TotalSeconds,
                stopwatch.Elapsed.TotalSeconds > 0 ? successCount / stopwatch.Elapsed.TotalSeconds : 0);

            return new StreamingImportFileOutput
            {
                FileUrl = input.FileUrl,
                ResourceType = input.ResourceType,
                SuccessCount = successCount,
                ErrorCount = errors.Count,
                Duration = stopwatch.Elapsed,
                Errors = errors
            };
        }
        finally
        {
            stopwatch.Stop();
            _fhirContextAccessor.RequestContext = previousContext;
        }
    }

    /// <summary>
    /// Producer: Reads file from blob storage and streams resources to channel line-by-line.
    /// </summary>
    private async Task ProduceResourcesAsync(
        string fileUrl,
        ChannelWriter<ImportEntry> writer,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Producer starting: {FileUrl}", fileUrl);

        // Use blob storage abstraction to read file
        // fileUrl is the blob path (e.g., "tenant/1/import/Patient.ndjson")
        var stream = await _blobStorageClient.ReadBlobAsync(fileUrl, cancellationToken);

        await using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var lineNumber = 0;
            string? line;

            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue; // Skip empty lines
                }

                // Write to channel (blocks if channel is full - backpressure)
                await writer.WriteAsync(
                    new ImportEntry { LineNumber = lineNumber, ResourceJson = line },
                    cancellationToken);

                // Log progress periodically
                if (lineNumber % 10_000 == 0)
                {
                    _logger.LogDebug(
                        "Producer progress: {LineNumber} lines read from {FileUrl}",
                        lineNumber,
                        fileUrl);
                }
            }

            _logger.LogDebug(
                "Producer finished: {LineNumber} lines read from {FileUrl}",
                lineNumber,
                fileUrl);
        }
    }

    /// <summary>
    /// Prepares a resource for batch write: parses JSON, extracts indices, generates ID if needed.
    /// Returns tuple for BatchWriteAsync, or null resource if parse fails.
    /// </summary>
    private (string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)
        PrepareResource(
            ImportEntry entry,
            string expectedResourceType,
            IFhirSchemaProvider schemaProvider,
            ISearchIndexer searchIndexer,
            List<ImportErrorLogEntry> errors)
    {
        try
        {
            // Parse JSON to ResourceJsonNode
            var jsonNode = JsonSourceNodeFactory.Parse(entry.ResourceJson);

            // Validate resource type matches expected type
            if (jsonNode.ResourceType != expectedResourceType)
            {
                errors.Add(new ImportErrorLogEntry
                {
                    ResourceType = expectedResourceType,
                    ResourceId = jsonNode.Id ?? "unknown",
                    ErrorCode = "InvalidResourceType",
                    ErrorMessage = $"Expected {expectedResourceType}, got {jsonNode.ResourceType}",
                    ResourceJson = entry.ResourceJson
                });
                return (expectedResourceType, "unknown", null!, Array.Empty<object>(), "PUT", entry.LineNumber);
            }

            // Generate ID if missing
            var resourceId = jsonNode.Id;
            if (string.IsNullOrEmpty(resourceId))
            {
                resourceId = Guid.NewGuid().ToString("N");
                jsonNode.Id = resourceId;
                _logger.LogDebug("Generated ID for {ResourceType}: {Id}", expectedResourceType, resourceId);
            }

            var typedElement = jsonNode.ToElement(schemaProvider);
            IReadOnlyList<object> searchIndices = searchIndexer.Extract((IElement)typedElement).ToArray();

            // Return tuple for BatchWriteAsync
            return (expectedResourceType, resourceId, jsonNode, searchIndices, "PUT", entry.LineNumber);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            _logger.LogError(ex, "Error parsing resource at line {LineNumber}", entry.LineNumber);
            errors.Add(new ImportErrorLogEntry
            {
                ResourceType = expectedResourceType,
                ResourceId = "unknown",
                ErrorCode = "ParseError",
                ErrorMessage = ex.Message,
                ResourceJson = entry.ResourceJson
            });
            return (expectedResourceType, "unknown", null!, Array.Empty<object>(), "PUT", entry.LineNumber);
        }
    }

    /// <summary>
    /// Internal struct for channel communication.
    /// </summary>
    private struct ImportEntry
    {
        public int LineNumber { get; init; }
        public required string ResourceJson { get; init; }
    }
}
