# Investigation: Bulk Import/Export with Streaming Channels

**Feature**: export
**Status**: Viable
**Created**: 2024-01-01
**Original ADR**: ADR-2512

## Context

FHIR Server v2 requires support for bulk data operations as defined by the [FHIR Bulk Data Access IG](https://hl7.org/fhir/uv/bulkdata/). These operations enable:
- **Bulk Export**: Export large datasets in NDJSON format to external storage (System/$export, Patient/$export, Group/$export)
- **Bulk Import**: Import large NDJSON files from external storage into the FHIR server

### Legacy Implementation Analysis

The legacy codebase (`src-old`) **already uses `System.Threading.Channels`** for bulk import:

**Import Pipeline (from `ImportResourceLoader.cs` and `SqlImporter.cs`):**
```csharp
// Producer: Loads resources from blob storage
public (Channel<ImportResource> resourceChannel, Task loadTask) LoadResources(...)
{
    var outputChannel = Channel.CreateBounded<ImportResource>(500); // Bounded channel
    var loadTask = Task.Run(async () => await LoadResourcesInternalAsync(outputChannel, ...));
    return (outputChannel, loadTask);
}

// Consumer: Reads from channel and batches for SQL import
await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
{
    resourceBatch.Add(resource);
    if (resourceBatch.Count < _importTaskConfiguration.TransactionSize)
        continue;
    await ImportResourcesInBuffer(resourceBatch, ...);
}
```

**Export Pipeline (from `ExportProcessingJob.cs`):**
- Uses job queue system (`IQueueClient`) for orchestration
- Pages through search results
- Writes NDJSON files to blob storage
- No explicit channels, but could benefit from streaming

### Key Observations

1. **Import already uses channels** - Proven pattern for streaming large files
2. **Import uses batching** - Processes resources in configurable batch sizes
3. **Export uses paging** - Processes search results page by page
4. **Both use blob storage** - Azure Blob Storage as intermediary

### Connection to Bundle Processing

Bundle processing (ADR 2511) and bulk import share **nearly identical architectures**:

**Bundle Transaction:**
- Entry Source: Bundle entries in HTTP request body
- Processing: Parse → Validate → Execute via ASP.NET pipeline → Collect responses
- Concurrency: Parallel within verb groups
- Output: Bundle response in HTTP body

**Bulk Import:**
- Entry Source: NDJSON lines from blob storage
- Processing: Parse → Validate → Execute via ASP.NET pipeline → Collect errors
- Concurrency: Parallel with bounded channels
- Output: Success/error counts + error file

**Shared Concepts:**
- Both process collections of FHIR resources
- Both need backpressure control
- Both validate and execute operations
- Both handle errors without failing entire batch
- Both can reuse the same pipeline-based execution model

## Decision

We will implement bulk import/export operations using **streaming channels architecture** that:
1. Reuses the bundle processing channel infrastructure from ADR 2511
2. Leverages ASP.NET Core pipeline for request execution
3. Supports backpressure and memory-efficient streaming
4. Works with any storage provider (Blob, S3, local file)

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     Bulk Export Pipeline                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. Search Query                                                │
│     └─> IFhirSearchService.SearchAsync()                       │
│         Returns: IAsyncEnumerable<ResourceWrapper>              │
│                                                                  │
│  2. Channel Pipeline                                            │
│     ┌─────────────┐      ┌──────────────┐     ┌──────────────┐│
│     │  Producer   │ ───> │   Bounded    │ ──> │  Consumers   ││
│     │  (Search)   │      │   Channel    │     │  (Writers)   ││
│     │             │      │  Capacity:   │     │              ││
│     │  Yields     │      │    1000      │     │  Serialize   ││
│     │  Resources  │      │              │     │  to NDJSON   ││
│     └─────────────┘      └──────────────┘     └──────────────┘│
│                                                       │         │
│  3. Storage Writer                                   │         │
│     └─> IBlobStorageClient.WriteAsync()  <──────────┘         │
│         Appends to NDJSON file in chunks                       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                     Bulk Import Pipeline                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. Storage Reader                                              │
│     └─> IBlobStorageClient.ReadLinesAsync()                    │
│         Returns: IAsyncEnumerable<string> (NDJSON lines)        │
│                                                                  │
│  2. Channel Pipeline (IDENTICAL to Bundle Processing!)          │
│     ┌─────────────┐      ┌──────────────┐     ┌──────────────┐│
│     │  Producer   │ ───> │   Bounded    │ ──> │  Consumers   ││
│     │  (Reader)   │      │   Channel    │     │  (Pipeline)  ││
│     │             │      │  Capacity:   │     │              ││
│     │  Yields     │      │    500       │     │  Execute via ││
│     │  Lines      │      │              │     │  ASP.NET     ││
│     └─────────────┘      └──────────────┘     │  Pipeline    ││
│                                                └──────────────┘│
│                                                       │         │
│  3. Repository Batch                                 │         │
│     └─> IFhirRepository.BulkUpsertAsync() <─────────┘         │
│         Batches writes for efficiency                          │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Implementation Details

### Bulk Import Architecture

**Step 1: Storage Abstraction**

```csharp
public interface IBlobStorageClient
{
    /// <summary>
    /// Downloads NDJSON file and streams lines as async enumerable
    /// </summary>
    IAsyncEnumerable<BlobLine> ReadLinesAsync(
        Uri blobUri,
        long startOffset = 0,
        long? maxBytes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads error log file
    /// </summary>
    Task<Uri> WriteErrorLogAsync(
        Uri containerUri,
        string fileName,
        IAsyncEnumerable<ImportError> errors,
        CancellationToken cancellationToken = default);
}

public record BlobLine(
    long LineNumber,
    long ByteOffset,
    int ByteLength,
    string Content);
```

**Step 2: Import Entry Context (Similar to BundleEntryContext)**

```csharp
public record ImportEntryContext
{
    public required long LineNumber { get; init; }
    public required long ByteOffset { get; init; }
    public required int ByteLength { get; init; }
    public required string ResourceJson { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public ImportMode Mode { get; init; }

    // Result
    public ImportEntryResult? Result { get; set; }
}

public record ImportEntryResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public OperationOutcome? OperationOutcome { get; init; }
}

public enum ImportMode
{
    IncrementalLoad,      // Standard: Skip existing versions
    InitialLoad,          // Allow negative version IDs for late arrivals
    Overwrite             // Replace existing resources
}
```

**Step 3: Reusable Channel-Based Import Executor**

```csharp
public class BulkImportExecutor
{
    private readonly IBlobStorageClient _blobClient;
    private readonly IFhirRepository _repository;
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly ILogger<BulkImportExecutor> _logger;

    public async Task<ImportResult> ImportAsync(
        ImportJobRequest request,
        CancellationToken cancellationToken)
    {
        var options = new ImportProcessingOptions
        {
            MaxParallelism = request.MaxParallelism ?? 4,
            ChannelCapacity = request.ChannelCapacity ?? 500,
            BatchSize = request.BatchSize ?? 100
        };

        // Create bounded channel for backpressure
        var channel = Channel.CreateBounded<ImportEntryContext>(
            new BoundedChannelOptions(options.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });

        // Producer: Read NDJSON lines from blob storage
        var producer = ProduceImportEntriesAsync(
            channel.Writer,
            request.InputUri,
            request.StartOffset,
            cancellationToken);

        // Consumers: Process entries in parallel
        var consumers = Enumerable.Range(0, options.MaxParallelism)
            .Select(_ => ConsumeImportEntriesAsync(
                channel.Reader,
                options,
                cancellationToken))
            .ToArray();

        await Task.WhenAll(consumers.Append(producer).ToArray());

        return CollectResults();
    }

    private async Task ProduceImportEntriesAsync(
        ChannelWriter<ImportEntryContext> writer,
        Uri inputUri,
        long startOffset,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var line in _blobClient.ReadLinesAsync(inputUri, startOffset, cancellationToken))
            {
                // Parse minimal info to create context
                var (resourceType, resourceId) = ParseResourceIdentity(line.Content);

                var entry = new ImportEntryContext
                {
                    LineNumber = line.LineNumber,
                    ByteOffset = line.ByteOffset,
                    ByteLength = line.ByteLength,
                    ResourceJson = line.Content,
                    ResourceType = resourceType,
                    ResourceId = resourceId
                };

                await writer.WriteAsync(entry, cancellationToken);
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task ConsumeImportEntriesAsync(
        ChannelReader<ImportEntryContext> reader,
        ImportProcessingOptions options,
        CancellationToken cancellationToken)
    {
        var batch = new List<ImportEntryContext>();

        await foreach (var entry in reader.ReadAllAsync(cancellationToken))
        {
            batch.Add(entry);

            if (batch.Count >= options.BatchSize)
            {
                await ProcessBatchAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        // Process remaining entries
        if (batch.Count > 0)
        {
            await ProcessBatchAsync(batch, cancellationToken);
        }
    }

    private async Task ProcessBatchAsync(
        List<ImportEntryContext> batch,
        CancellationToken cancellationToken)
    {
        var resourceWrappers = new List<ResourceWrapper>();

        foreach (var entry in batch)
        {
            try
            {
                // Parse and validate resource
                var sourceNode = JsonSourceNodeFactory.Parse(entry.ResourceJson);
                var wrapper = new ResourceWrapper(
                    entry.ResourceType,
                    entry.ResourceId,
                    GenerateVersionId(),
                    DateTimeOffset.UtcNow,
                    sourceNode);

                resourceWrappers.Add(wrapper);
                entry.Result = new ImportEntryResult { Success = true };
            }
            catch (Exception ex)
            {
                entry.Result = new ImportEntryResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        // Bulk upsert to repository (single transaction for batch)
        if (resourceWrappers.Any())
        {
            await _repository.BulkUpsertAsync(resourceWrappers, cancellationToken);
        }
    }
}
```

**Step 4: Reuse Bundle Pipeline Execution (Alternative Approach)**

Instead of direct repository calls, we can reuse the bundle pipeline architecture:

```csharp
private async Task ProcessEntryViaPipelineAsync(
    ImportEntryContext entry,
    CancellationToken cancellationToken)
{
    // Create mini HttpContext (IDENTICAL to bundle processing!)
    using var httpContext = _httpContextFactory.Create(new FeatureCollection());

    // Build POST request from import entry
    httpContext.Request.Method = "POST";
    httpContext.Request.Path = $"/{entry.ResourceType}";
    httpContext.Request.Headers.ContentType = "application/fhir+json";

    // Set resource body
    using var requestBody = new MemoryStream(Encoding.UTF8.GetBytes(entry.ResourceJson));
    httpContext.Request.Body = requestBody;

    using var responseBody = FhirStreamManager.GetStream("ImportResponse");
    httpContext.Response.Body = responseBody;

    // Execute through ASP.NET Core pipeline
    // This automatically discovers correct handler, validates, and persists!
    await _pipeline(httpContext);

    // Extract result
    entry.Result = new ImportEntryResult
    {
        Success = httpContext.Response.StatusCode is >= 200 and < 300,
        ErrorMessage = httpContext.Response.StatusCode >= 400
            ? await ReadResponseBodyAsync(responseBody)
            : null
    };
}
```

### Bulk Export Architecture

**Step 1: Export Entry Context**

```csharp
public record ExportEntryContext
{
    public required ResourceWrapper Resource { get; init; }
    public required string OutputFileName { get; init; }

    // Result
    public string? SerializedJson { get; set; }
    public Exception? Error { get; set; }
}
```

**Step 2: Channel-Based Export Executor**

```csharp
public class BulkExportExecutor
{
    private readonly IFhirSearchService _searchService;
    private readonly IBlobStorageClient _blobClient;
    private readonly ILogger<BulkExportExecutor> _logger;

    public async Task<ExportResult> ExportAsync(
        ExportJobRequest request,
        CancellationToken cancellationToken)
    {
        var options = new ExportProcessingOptions
        {
            MaxParallelism = request.MaxParallelism ?? 8,
            ChannelCapacity = request.ChannelCapacity ?? 1000,
            FilePartSize = request.FilePartSize ?? 10_000 // Resources per file part
        };

        // Create bounded channel
        var channel = Channel.CreateBounded<ExportEntryContext>(
            new BoundedChannelOptions(options.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });

        // Producer: Execute search and yield resources
        var producer = ProduceExportEntriesAsync(
            channel.Writer,
            request,
            options,
            cancellationToken);

        // Consumers: Serialize and write to blob storage
        var consumers = Enumerable.Range(0, options.MaxParallelism)
            .Select(i => ConsumeExportEntriesAsync(
                channel.Reader,
                request.OutputUri,
                options,
                cancellationToken))
            .ToArray();

        await Task.WhenAll(consumers.Append(producer).ToArray());

        return CollectResults();
    }

    private async Task ProduceExportEntriesAsync(
        ChannelWriter<ExportEntryContext> writer,
        ExportJobRequest request,
        ExportProcessingOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            long filePartNumber = 0;
            long resourcesInCurrentPart = 0;

            // Execute search for each resource type
            foreach (var resourceType in request.ResourceTypes)
            {
                var searchParams = new SearchParameters
                {
                    ResourceType = resourceType,
                    Since = request.Since,
                    Type = request.Type // System, Patient, Group
                };

                // Stream search results (IAsyncEnumerable)
                await foreach (var resource in _searchService.SearchStreamAsync(searchParams, cancellationToken))
                {
                    var entry = new ExportEntryContext
                    {
                        Resource = resource,
                        OutputFileName = $"{resourceType}-{filePartNumber:D6}.ndjson"
                    };

                    await writer.WriteAsync(entry, cancellationToken);

                    resourcesInCurrentPart++;
                    if (resourcesInCurrentPart >= options.FilePartSize)
                    {
                        filePartNumber++;
                        resourcesInCurrentPart = 0;
                    }
                }
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task ConsumeExportEntriesAsync(
        ChannelReader<ExportEntryContext> reader,
        Uri outputUri,
        ExportProcessingOptions options,
        CancellationToken cancellationToken)
    {
        // Group entries by output file
        var fileWriters = new Dictionary<string, BlobFileWriter>();

        await foreach (var entry in reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                // Serialize resource to NDJSON
                using var stream = FhirStreamManager.GetStream("ExportSerialize");
                await SerializeResourceAsync(entry.Resource, stream, cancellationToken);

                entry.SerializedJson = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);

                // Get or create blob writer for this file
                if (!fileWriters.TryGetValue(entry.OutputFileName, out var writer))
                {
                    var blobUri = new Uri(outputUri, entry.OutputFileName);
                    writer = await _blobClient.CreateAppendBlobAsync(blobUri, cancellationToken);
                    fileWriters[entry.OutputFileName] = writer;
                }

                // Append NDJSON line to blob
                await writer.AppendLineAsync(entry.SerializedJson, cancellationToken);
            }
            catch (Exception ex)
            {
                entry.Error = ex;
                _logger.LogError(ex, "Failed to export resource {ResourceType}/{ResourceId}",
                    entry.Resource.ResourceType, entry.Resource.ResourceId);
            }
        }

        // Flush and close all writers
        foreach (var writer in fileWriters.Values)
        {
            await writer.FlushAsync(cancellationToken);
            await writer.DisposeAsync();
        }
    }
}
```

### Job Orchestration

Both import and export are **long-running background operations** that require:
- Job status tracking
- Progress reporting
- Cancellation support
- Failure handling and retry

**Job Infrastructure:**

```csharp
public interface IBackgroundJobQueue
{
    ValueTask<string> EnqueueAsync<TJob, TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TJob : IBackgroundJob<TRequest>;

    Task<JobStatus> GetStatusAsync(string jobId, CancellationToken cancellationToken = default);
    Task CancelAsync(string jobId, CancellationToken cancellationToken = default);
}

public interface IBackgroundJob<TRequest>
{
    Task<JobResult> ExecuteAsync(
        string jobId,
        TRequest request,
        IProgress<JobProgress> progress,
        CancellationToken cancellationToken);
}

public record JobStatus(
    string JobId,
    JobState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    JobProgress? Progress,
    string? ErrorMessage);

public record JobProgress(
    long ProcessedItems,
    long TotalItems,
    long SuccessCount,
    long ErrorCount);

public enum JobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}
```

## Testing Strategy

**Unit Tests:**
- Channel producer/consumer logic
- Blob storage client (mocked)
- Serialization/deserialization
- Error handling

**Integration Tests:**
- End-to-end import from local file
- End-to-end export to local file
- Backpressure behavior under load
- Cancellation during processing

**E2E Tests (from src-old/test):**
- `Import/ImportTests.cs` - ALL tests must pass
- `Import/Import*SearchTests.cs` - Verify search works after import
- `Export/ExportTests.cs` - System/$export
- `Export/ExportDataTests.cs` - Patient/Group/$export
- `Export/ExportDataValidationTests.cs` - Export validation rules

## Performance Characteristics

**Import:**
- Streaming: Processes files larger than memory
- Backpressure: Bounded channel prevents overwhelming database
- Batching: Reduces transaction overhead (100 resources per batch)
- Parallelism: Multiple consumers process concurrently (default: 4)
- Throughput: Target 1000+ resources/second (depends on resource size and DB)

**Export:**
- Streaming: Search results streamed without loading all into memory
- Backpressure: Bounded channel prevents overwhelming blob storage
- Chunking: Files split into manageable parts (10,000 resources per file)
- Parallelism: Multiple consumers write concurrently (default: 8)
- Throughput: Target 2000+ resources/second (search is bottleneck)

## Consequences

### Positive

1. **Code Reuse**: Import and export share channel infrastructure
2. **Bundle Synergy**: Import can reuse bundle processing pipeline (ADR 2511)
3. **Memory Efficient**: Streaming prevents loading entire files/result sets
4. **Backpressure**: Bounded channels prevent resource exhaustion
5. **Scalable**: Parallelism tunable per deployment
6. **Testable**: Channel-based design easy to test with fake producers/consumers
7. **Storage Agnostic**: Blob abstraction supports Azure, AWS, local files

### Negative

1. **Complexity**: Channel coordination adds debugging complexity
2. **Error Handling**: Partial failures require careful error collection
3. **Job Management**: Requires background job infrastructure
4. **Storage Dependency**: Requires blob storage even for small exports

### Mitigations

- Extensive logging at channel boundaries
- Progress reporting for visibility
- Error logs collected and uploaded to blob storage
- Job queue with retry and status tracking
- Local file provider for development (F5 principle)

## Implementation Phases

**Phase 8: Bulk Export (~15 Claude Code hours)**
- Implement `IBlobStorageClient` with local file provider
- Implement `BulkExportExecutor` with channel pipeline
- Implement `IBackgroundJobQueue` with in-memory provider
- Add `$export` operation endpoints (System, Patient, Group)
- Add `ExportTests.cs` E2E test suite
- 80% test coverage minimum

**Phase 9: Bulk Import (~12 Claude Code hours)**
- Reuse blob storage client from Phase 8
- Implement `BulkImportExecutor` with channel pipeline
- Add `$import` operation endpoint
- Add `ImportTests.cs` E2E test suite
- Verify all `Import*SearchTests.cs` pass (search after import)
- 80% test coverage minimum

**Phase 13: Production Export (~8 Claude Code hours)**
- Azure Blob Storage provider
- AWS S3 provider
- Anonymization support (de-identification)
- Large dataset optimizations (>10M resources)

**Phase 14: Production Import (~6 Claude Code hours)**
- Import job resumption (restart from offset)
- Import validation (pre-check before processing)
- Initial load mode (negative version IDs)

## Related ADRs

- **ADR 2511**: Bundle Processing with Channels - Import reuses this architecture
- **ADR 2510**: Implementation Roadmap - Defines phasing
- **ADR 2501**: Core Architecture - Defines storage abstractions

## References

- [FHIR Bulk Data Access IG](https://hl7.org/fhir/uv/bulkdata/)
- [System.Threading.Channels Documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- Legacy Implementation: `src-old/Microsoft.Health.Fhir.Core/Features/Operations/Import/`
- Legacy Implementation: `src-old/Microsoft.Health.Fhir.Core/Features/Operations/Export/`
