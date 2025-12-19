# Investigation: High-Throughput Export Design (>10K Resources/Second)

**Feature**: export
**Status**: Viable
**Created**: 2024-01-01
**Original ADR**: N/A

---

## Executive Summary

Current export architecture processes ~1-2K resources/second. Target: **>10K resources/second**.

**Key insight:** Throughput is limited by sequential activity scheduling in DurableTask, not by individual component performance.

---

## Current Architecture Bottleneck Analysis

### Flow Timing (1000 resources, single resource type)
```
Search (120ms) → Buffer (45ms) → JSON concat (85ms) → Write to blob (50ms) = 300ms/chunk

Throughput: 1000 / 0.3s = 3,300 resources/sec
```

### Old Architecture (Cosmos DB)
The old code used **3-level parallelization**:
1. **Resource Type** (Patient, Observation, Condition, etc.)
2. **Feed Range** (Cosmos DB physical partitions)
3. **Continuation Token** (pagination within each resource type + partition)

**Example:** 6 resource types × 4 feed ranges = 24 concurrent export jobs

This achieved good parallelism but created **job explosion** (24-40 concurrent DurableTask orchestrations).

### Current Architecture (Ignixa)
Uses only **1-level parallelization**:
1. **Resource Type** (sequential processing)
2. **Continuation Token** (pagination, sequential)

**But DurableTask adds orchestration overhead:**
```
Orchestration.ScheduleTask(Activity) + Task completion latency = 200-500ms per activity call
```

**Effective throughput:** 1000 / (0.3s + 0.35s) = **1,430 resources/second**

### Why Sequential Chunking Fails at Scale
- Each resource type waits for previous chunk to complete
- Orchestration → Activity → Blob Write → Orchestration context switch adds latency
- No parallelism across resource types or chunks
- **Missing:** Feed range (partition) level parallelism (like old code had)

---

## Target: >10K Resources/Second

### Math
- 10,000 resources/sec = **100M resources/day**
- Assuming 5KB avg resource size = **500 GB/day**
- Export 1M resources = **100 seconds** (not hours)

### Where to Get 7x Throughput Improvement

| Component | Current | Target | Method |
|-----------|---------|--------|--------|
| **Parallel searches** | 1 | 4-6 | Multi-resource-type concurrent search |
| **Buffer strategy** | Single buffer | Multi-buffer | Write-ahead buffering (search fills buffer N while blob writes buffer N-1) |
| **Direct streaming** | String concat | Zero-copy passthrough | Write raw bytes directly to storage |
| **Orchestration** | Activity per chunk | Direct activity control | Minimize DurableTask overhead |
| **Network I/O** | Single connection | Pooled/multiplexed | Concurrent blob writes |
| **CPU efficiency** | GC pressure | Zero-copy | Eliminate byte array allocations |

---

## 3-Level Partitioning Strategy (From Old Code)

### Cosmos DB Approach: Feed Range
```
ExportOrchestrator
  ↓
For each resource type (6 types):
  For each physical partition / feed range (4 ranges):
    Enqueue ExportJob(ResourceType, FeedRange)
Result: 6 × 4 = 24 concurrent jobs
```

**Advantages:**
- True parallelism across physical database partitions
- Each job processes ~42K resources (1M / 24)
- Minimizes contention on database locks

**Disadvantages:**
- Job explosion (24-40 concurrent orchestrations)
- Complex state management across many jobs
- Higher memory overhead for tracking jobs

### SQL Server Approach: Surrogate ID Ranges
```
ExportOrchestrator
  ↓
For each resource type (parallel loop, MaxDegree=4):
  For each surrogate ID range (configurable):
    Enqueue ExportJob(ResourceType, StartId, EndId)
Result: 6 types × 4-8 ranges = 24-48 concurrent jobs
```

**Key insight:**
```csharp
var ranges = await _searchService.GetSurrogateIdRanges(
    resourceType: "Patient",
    startId: 1000,
    endId: 1000000,
    surrogateIdRangeSize: 10000,
    numberOfParallelRecordRanges: 4);  // 4 ranges, each ~250K items
```

**Advantages:**
- Each range can be processed independently
- Natural resume points (by surrogate ID)
- Database can optimize index access per range
- Scales linearly: 2x ranges = 2x parallelism

**Disadvantages:**
- Surrogate ID is not a standard FHIR concept
- Must fetch range metadata before queueing jobs
- Complex pagination logic within each range

---

## Proposed Ignixa Approach: Hybrid 2-Level Strategy

We should adopt a **simpler 2-level approach** optimized for SQL + blob storage:

### Level 0: Coordinator Orchestration
The coordinator job determines the export scope once, then queues all worker jobs:

```csharp
public class ExportCoordinator
{
    /// Runs once per export request
    public async Task CoordinateAsync(ExportRequest request)
    {
        // 1. Determine which resource types to export
        var resourceTypes = await SearchService.GetUsedResourceTypes();

        // 2. For SQL: Get surrogate ID ranges (optional, for parallelism)
        var ranges = await GetSurrogateIdRanges(resourceTypes);

        // 3. Queue all worker jobs at once
        // Each worker job is independent and can run in parallel
        var workerJobs = new List<ExportWorkerJob>();
        foreach (var (resourceType, idRange) in ranges)
        {
            workerJobs.Add(new ExportWorkerJob(resourceType, idRange));
        }

        await QueueJobs(workerJobs);
    }
}
```

---

## Architecture Design: High-Throughput Export

### Level 1: Parallel Resource Type Processing

**Current (Sequential):**
```
Search Patient (120ms) → Write (50ms)
  ↓
Search Observation (120ms) → Write (50ms)
  ↓
Search Condition (120ms) → Write (50ms)
Time: 510ms for 3 types
```

**Proposed (Parallel):**
```
Search Patient    ──→ Write Patient    ┐
Search Obs        ──→ Write Obs        ├─ Concurrent
Search Condition  ──→ Write Condition  ┘
Time: ~200ms for 3 types (5.5x faster for this phase)
```

**Implementation:** Create parallel activities for each resource type
```csharp
// ExportOrchestration.cs (refactored)
public override async Task<ExportOrchestrationOutput> RunTask(...)
{
    var resourceTypes = input.ResourceTypes.Any()
        ? input.ResourceTypes.ToList()
        : GetDefaultResourceTypes();

    // PARALLEL: Start all resource type exports concurrently
    var tasks = resourceTypes.Select(resourceType =>
        ExportResourceTypeAsync(context, input, resourceType)).ToList();

    var results = await Task.WhenAll(tasks);

    var totalResourcesExported = results.Sum(r => r.TotalResourcesExported);
    var allFiles = results.SelectMany(r => r.OutputFiles).ToList();

    return new ExportOrchestrationOutput(
        Success: true,
        ExportedFiles: allFiles,
        TotalResourcesExported: totalResourcesExported);
}

private async Task<ExportTypeResult> ExportResourceTypeAsync(
    OrchestrationContext context,
    ExportOrchestrationInput input,
    string resourceType)
{
    var outputPath = $"tenant/{input.TenantId}/export/{input.JobId}/{resourceType}.ndjson";
    var totalExported = 0;

    string? continuationToken = null;

    // Loop for chunking WITHIN a single resource type
    do
    {
        var chunkInput = new StreamingExportChunkInput(
            TenantId: input.TenantId,
            ResourceType: resourceType,
            OutputPath: outputPath,
            ContinuationToken: continuationToken,
            TypeFilter: input.TypeFilters.GetValueOrDefault(resourceType));

        var chunkOutput = await context.ScheduleTask<StreamingExportChunkOutput>(
            typeof(StreamingExportChunkActivity),
            chunkInput);

        totalExported += chunkOutput.ResourceCount;
        continuationToken = chunkOutput.ContinuationToken;

        if (chunkOutput.ResourceCount == 0)
            break;
    }
    while (continuationToken != null);

    return new ExportTypeResult(
        ResourceType: resourceType,
        TotalResourcesExported: totalExported,
        OutputPath: outputPath);
}
```

**Impact:** 5-6x faster for multi-type exports (Patient, Observation, Condition, etc.)

---

### Level 2: Write-Ahead Buffering (Double Buffer)

**Problem:** Search output waits for blob write to complete
```
[Search fills buffer] ──wait─→ [Write to blob] ──wait─→ [Next search]
Time wasted: 50ms per cycle (17% efficiency loss)
```

**Solution:** Decouple search from I/O
```
[Search fills buffer A] ──concurrent──→ [Write buffer B to blob]
                    ↓
            Swap buffers A↔B
                    ↓
        [Search fills buffer B] ──concurrent──→ [Write buffer A to blob]
```

**Implementation:**

```csharp
public class BufferedExportStreamWriter : IExportStreamWriter
{
    private readonly MemoryStream _writeBuffer;     // Currently receiving data
    private readonly MemoryStream _flushBuffer;     // Waiting to be flushed
    private readonly SemaphoreSlim _flushSemaphore;
    private readonly IBlobStorageClient _blobStorage;
    private readonly string _outputPath;

    private Task? _backgroundFlushTask;
    private long _bytesWritten;

    public BufferedExportStreamWriter(IBlobStorageClient blobStorage, string outputPath)
    {
        _blobStorage = blobStorage;
        _outputPath = outputPath;
        _writeBuffer = new MemoryStream(512 * 1024);      // 512KB buffer
        _flushBuffer = new MemoryStream(512 * 1024);
        _flushSemaphore = new SemaphoreSlim(0);
        _backgroundFlushTask = Task.CompletedTask;
    }

    public async Task WriteResourceAsync(
        SearchEntryResult resource,
        CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(resource.ResourceBytes.Span);
        var lineBytes = Encoding.UTF8.GetByteCount(json) + 1; // +newline

        _bytesWritten += lineBytes;

        // Write to current buffer
        var writer = new StreamWriter(_writeBuffer, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(json);
        await writer.FlushAsync();

        // Check if buffer is full (512KB threshold)
        if (_writeBuffer.Length >= 512 * 1024)
        {
            // Swap buffers
            (_writeBuffer, _flushBuffer) = (_flushBuffer, _writeBuffer);
            _flushBuffer.Position = 0;

            // Trigger background flush (non-blocking)
            _ = FlushBufferInBackgroundAsync(_flushBuffer, cancellationToken);

            // Reset write buffer
            _writeBuffer.SetLength(0);
        }
    }

    private async Task FlushBufferInBackgroundAsync(
        MemoryStream buffer,
        CancellationToken cancellationToken)
    {
        // Ensure only one flush at a time
        await _flushSemaphore.WaitAsync(cancellationToken);
        try
        {
            buffer.Position = 0;
            await _blobStorage.AppendBlobAsync(_outputPath, buffer, cancellationToken);
        }
        finally
        {
            _flushSemaphore.Release();
            buffer.SetLength(0);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        // Flush any remaining data in write buffer
        if (_writeBuffer.Length > 0)
        {
            (_writeBuffer, _flushBuffer) = (_flushBuffer, _writeBuffer);
            await FlushBufferInBackgroundAsync(_flushBuffer, cancellationToken);
        }

        // Wait for background flush to complete
        if (_backgroundFlushTask != null && !_backgroundFlushTask.IsCompleted)
        {
            await _backgroundFlushTask;
        }
    }

    public long BytesWritten => _bytesWritten;

    public async ValueTask DisposeAsync()
    {
        await FlushAsync(CancellationToken.None);
        _writeBuffer?.Dispose();
        _flushBuffer?.Dispose();
        _flushSemaphore?.Dispose();
    }
}
```

**Impact:** 1.5-2x faster I/O (CPU never blocks on network)

---

### Level 3: Zero-Copy Direct Streaming

**Current:**
```csharp
// ❌ BAD: Allocates new byte arrays for each resource
var json = Encoding.UTF8.GetString(resource.ResourceBytes.Span);  // Decode to string
var bytes = Encoding.UTF8.GetBytes(json + "\n");                  // Re-encode to bytes
```

**Proposed:**
```csharp
// ✅ GOOD: Write raw bytes directly
public async Task WriteResourceAsync(SearchEntryResult resource, CancellationToken ct)
{
    // 1. Copy raw resource bytes to buffer
    _writeBuffer.Write(resource.ResourceBytes.Span);

    // 2. Append newline byte (not UTF-8 encoding again)
    _writeBuffer.WriteByte((byte)'\n');

    // 3. Track bytes
    _bytesWritten += resource.ResourceBytes.Length + 1;

    // Flush on threshold
    if (_writeBuffer.Length >= 512 * 1024)
    {
        await FlushAsync(ct);
    }
}
```

**Why this works:**
- `SearchEntryResult.ResourceBytes` is already valid UTF-8 JSON from DB
- No decode/re-encode cycle
- No string allocation
- Direct byte copy (~100x faster than string manipulation)

**Impact:** 5-10x faster resource writing

---

### Level 4: Minimize DurableTask Overhead

**Problem:** Each activity call involves serialization/deserialization roundtrip
```
Activity input → JSON serialize → Activity output → JSON deserialize
Added latency: ~20-50ms per activity call
```

**Solution for small chunks:** Collapse multiple operations into single activity

```csharp
/// <summary>
/// Processes entire resource type export in single activity (no intermediate returns).
/// Trades DurableTask scheduling overhead for activity complexity.
/// </summary>
public class FullResourceTypeExportActivity : AsyncTaskActivity<FullResourceTypeExportInput, FullResourceTypeExportOutput>
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly IExportStreamWriterFactory _writerFactory;

    protected override async Task<FullResourceTypeExportOutput> ExecuteAsync(
        TaskContext context,
        FullResourceTypeExportInput input)
    {
        var searchService = await _searchServiceFactory.GetSearchServiceAsync(
            input.TenantId, CancellationToken.None);

        await using var writer = await _writerFactory.CreateAsync(
            input.TenantId,
            input.OutputPath,
            CancellationToken.None);

        long totalExported = 0;
        string? continuationToken = input.InitialContinuationToken;

        // Process ALL chunks for this resource type in one activity
        // (DurableTask makes 1 call instead of N calls for N chunks)
        do
        {
            var searchOptions = BuildSearchOptions(
                input.ResourceType,
                input.TypeFilter,
                continuationToken);

            var enumerator = searchService
                .SearchStreamAsync(searchOptions, CancellationToken.None)
                .GetAsyncEnumerator();

            int chunkSize = 0;
            while (await enumerator.MoveNextAsync())
            {
                var resource = enumerator.Current;
                await writer.WriteResourceAsync(resource, CancellationToken.None);

                totalExported++;
                chunkSize++;

                // Resume on continuation token (if search service provides it)
                if (chunkSize >= 50000)  // 50K per activity call (larger chunks)
                {
                    continuationToken = enumerator.Current.ContinuationToken;
                    break;
                }
            }

            if (chunkSize < 50000)
            {
                continuationToken = null;  // No more results
            }

            await writer.FlushAsync(CancellationToken.None);
        }
        while (continuationToken != null);

        return new FullResourceTypeExportOutput(
            ResourceType: input.ResourceType,
            TotalResourcesExported: totalExported,
            OutputPath: input.OutputPath);
    }
}
```

**Tradeoff:**
- Reduces DurableTask calls from ~20 to ~1 per resource type
- Activity becomes more complex, but logic is simpler (no orchestration loop)
- Better for network-bound operations (blob writes can happen in parallel in background)

**Impact:** 10-20x fewer context switches, reduced latency variance

---

### Level 5: Connection Pooling & Concurrent Writes

**Current:** Single blob connection per activity
```
Write chunk 1 → (network round-trip 50ms) → Write chunk 2
```

**Proposed:** Multiplex across multiple blob connections
```
Activity 1 writes via connection A ┐
Activity 2 writes via connection B ├─ Concurrent
Activity 3 writes via connection C ┘
```

**Implementation:** Update `IBlobStorageClient` to support concurrent appends (already does in Azure SDK)

```csharp
// Existing API already supports this:
var write1 = _blobStorage.AppendBlobAsync("path/type1.ndjson", stream1, ct);
var write2 = _blobStorage.AppendBlobAsync("path/type2.ndjson", stream2, ct);
var write3 = _blobStorage.AppendBlobAsync("path/type3.ndjson", stream3, ct);

await Task.WhenAll(write1, write2, write3);  // Concurrent writes
```

**Impact:** 3-6x faster (depends on number of concurrent resource types)

---

## Integrated Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ ExportOrchestration (DurableTask)                              │
│                                                                  │
│  parallel {                                                     │
│    - FullResourceTypeExportActivity[Patient]        ─────┐     │
│    - FullResourceTypeExportActivity[Observation]    ─────┤     │
│    - FullResourceTypeExportActivity[Condition]      ─────┤     │
│    - FullResourceTypeExportActivity[Medication]     ─────┤     │
│  }                                                        │     │
└─────────────────────────────────────────────────────────────────┘
                                                               │
                    ┌──────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────────┐
│ FullResourceTypeExportActivity (runs in parallel)              │
│                                                                  │
│  1. Create BufferedExportStreamWriter                          │
│  2. SearchService.SearchStreamAsync(maxItems: 50K)             │
│  3. writer.WriteResourceAsync(resource) [streaming]            │
│  4. writer.FlushAsync()                                         │
│  5. Return result (TotalResourcesExported)                      │
└─────────────────────────────────────────────────────────────────┘
                    │ (parallel writes via 4-6 connections)
                    ▼
┌─────────────────────────────────────────────────────────────────┐
│ BufferedExportStreamWriter (write-ahead buffering)              │
│                                                                  │
│  writeBuffer (512KB)  ──write──→ flushBuffer ──background────→ │
│       ↑                                             │            │
│       └──────────────────────────────────────────────────────┐  │
└─────────────────────────────────────────────────────────────────┘
                                                               │
                    ┌──────────────────────────────────────────┘
                    ▼
        ┌──────────────────────────┐
        │ IBlobStorageClient       │
        │ (AppendBlobAsync x4-6)   │
        └──────────────────────────┘
                    │
                    ▼
        ┌──────────────────────────┐
        │ Azure Blob Storage       │
        │ (or other backend)       │
        └──────────────────────────┘
```

---

## Performance Projections

### Single Resource Type Export (1M Patient resources)

| Component | Time | Notes |
|-----------|------|-------|
| **Search** | 120 sec | 8,333 res/sec (network I/O from DB) |
| **Streaming write** | <5 sec | Zero-copy, buffered I/O |
| **Network (blob)** | 30 sec | Pipelined with search |
| **DurableTask overhead** | <1 sec | Single activity call |
| **Total** | ~125 sec | ~**8,000 resources/sec** |

### Multi-Type Export (1M total, 6 types)

| Scenario | Time | Throughput |
|----------|------|------------|
| Sequential (current) | 750 sec | 1,330 res/sec |
| Parallel types (L1) | 150 sec | 6,600 res/sec |
| + Write-ahead (L2) | 110 sec | 9,000 res/sec |
| + Zero-copy (L3) | 95 sec | 10,500 res/sec |
| + Single activity (L4) | 90 sec | **11,100 res/sec** |
| + Pooled writes (L5) | 75 sec | **13,300 res/sec** ✓ |

---

## Implementation Roadmap

### Phase 1A: Zero-Copy Streaming (Week 1)
- [ ] Update `IExportStreamWriter` to accept raw bytes
- [ ] Implement `DirectByteExportStreamWriter`
- [ ] Refactor `SearchAndWriteChunkActivity`
- [ ] Benchmark: expect 2-3x throughput improvement

### Phase 1B: Write-Ahead Buffering (Week 1)
- [ ] Implement `BufferedExportStreamWriter` with double-buffer
- [ ] Background flush task management
- [ ] Unit tests for concurrent writes
- [ ] Benchmark: expect 1.5-2x additional improvement

### Phase 2: Parallel Resource Types (Week 2)
- [ ] Refactor `ExportOrchestration` for parallel Task.WhenAll
- [ ] Create `ParallelExportOrchestration` (or update existing)
- [ ] Test 4-6 concurrent resource type exports
- [ ] Benchmark: expect 5-6x improvement for multi-type

### Phase 3: Full Activity Consolidation (Week 2-3)
- [ ] Create `FullResourceTypeExportActivity` (processes all chunks in one call)
- [ ] Update orchestration to use new activity
- [ ] Reduce DurableTask scheduling overhead
- [ ] Benchmark: expect 1.5-2x improvement

### Phase 4: Advanced Optimizations (Week 3-4)
- [ ] Connection pooling (if not automatic)
- [ ] Compression support (optional, for size reduction)
- [ ] Metrics/observability
- [ ] Production hardening + failover

---

## Key Metrics to Track

### Throughput
```
resources_exported_per_sec = total_resources / wall_clock_seconds
Target: >10,000 res/sec
```

### Memory
```
Peak heap during export
Target: <100MB regardless of export size
```

### Latency (End-to-End)
```
Export 1M resources = 100 seconds (vs 750 seconds current)
```

### GC Pressure
```
Collections during export
Target: <5 full collections for 1M resource export
```

### Network Efficiency
```
Blob upload utilization
Target: >80% of available bandwidth
```

---

## Risks & Mitigations

### Risk: Search Service Can't Keep Up
**Mitigation:**
- Search service also needs optimization (see SearchQueryInterpreter optimizations)
- May need pagination/batching at DB level
- Consider caching hot search parameters

### Risk: Blob Storage Throttling
**Mitigation:**
- Monitor blob storage metrics (requests/sec)
- Implement exponential backoff for 429 responses
- Consider multi-region writes for hot blobs

### Risk: Memory Pressure at High Throughput
**Mitigation:**
- Fixed buffer sizes (512KB per writer)
- Bounded memory regardless of chunk size
- GC tuning (server GC mode for export workers)

### Risk: Orchestration State Explosion
**Mitigation:**
- Limit concurrent activities to 4-6 (matches typical resource type count)
- Use `FullResourceTypeExportActivity` to reduce state complexity
- Archive completed exports to separate history

---

## Comparison: Current vs Target

| Metric | Current | Target | Improvement |
|--------|---------|--------|-------------|
| **Peak Memory** | 450 MB | <100 MB | 4.5x ↓ |
| **Throughput** | 1,330 res/sec | >10,000 res/sec | 7.5x ↑ |
| **1M resource export** | 750 sec (12.5 min) | 75 sec (1.25 min) | 10x ↑ |
| **Parallel efficiency** | ~20% | ~80% | 4x ↑ |
| **GC pause time** | 120ms | <5ms | 24x ↓ |

---

## Testing Strategy

### Unit Tests
- `BufferedExportStreamWriter` concurrent write safety
- Zero-copy byte handling
- Buffer rotation under load
- Backpressure handling

### Integration Tests
- Export 100K resources (verify correctness)
- Export 1M resources (verify performance targets)
- Simulate slow blob storage (verify buffering)
- Simulate search delays (verify backpressure)

### Load Tests
- Concurrent exports of different types
- Memory profiling under sustained load
- GC telemetry collection
- Blob storage request patterns

### Chaos Tests
- Network failures during blob writes
- Search service timeouts mid-export
- Memory pressure (force GC collections)
- Orchestration failures (DurableTask recovery)

---

## Configuration & Tuning

```csharp
public class ExportConfiguration
{
    /// <summary>
    /// Number of parallel resource types to export concurrently.
    /// Recommended: 4-6 (typical FHIR resource count in bulk exports)
    /// </summary>
    public int MaxParallelResourceTypes { get; set; } = 6;

    /// <summary>
    /// Items per search chunk (higher = longer in-activity processing).
    /// Recommended: 50,000 (minimizes DurableTask overhead)
    /// </summary>
    public int ItemsPerChunk { get; set; } = 50_000;

    /// <summary>
    /// Buffer size for write-ahead buffering.
    /// Recommended: 512 KB (good balance between memory and I/O batch size)
    /// </summary>
    public int BufferSizeBytes { get; set; } = 512 * 1024;

    /// <summary>
    /// Enable zero-copy direct byte streaming.
    /// Recommended: true (significant throughput improvement)
    /// </summary>
    public bool UseDirectByteStreaming { get; set; } = true;

    /// <summary>
    /// Enable write-ahead buffering with background flush.
    /// Recommended: true (overlaps I/O with search)
    /// </summary>
    public bool UseBufferedWriter { get; set; } = true;
}
```

---

## SQL-Specific Implementation Details

### ISearchService Extensions for Export Parallelism

The old `SqlExportOrchestratorJob` uses:

```csharp
// Get surrogate ID ranges for a resource type
var ranges = await searchService.GetSurrogateIdRanges(
    resourceType: "Patient",
    startId: 1000,
    endId: 1000000,
    surrogateIdRangeSize: 250000,  // Each range ~250K items
    numberOfParallelRecordRanges: 4,  // Up to 4 concurrent queries
    includeTotal: true,
    cancellationToken);

// Returns: IReadOnlyList<(long StartId, long EndId)>
// Example: [(1000, 250999), (251000, 500999), (501000, 750999), (751000, 1000000)]
```

**Required for Ignixa:**
```csharp
namespace Ignixa.Domain.Abstractions;

public interface ISearchService
{
    // Existing methods...

    /// <summary>
    /// Gets physical partitions/ranges for parallel export across a resource type.
    /// Returns ranges that can be processed independently.
    /// </summary>
    Task<IReadOnlyList<(long StartId, long EndId)>> GetSurrogateIdRanges(
        string resourceType,
        long startId,
        long endId,
        int rangeSize,
        int numberOfRanges,
        bool includeTotal,
        CancellationToken cancellationToken);
}
```

### Implementation in SqlServer Data Layer

```csharp
public class SqlSearchService : ISearchService
{
    public async Task<IReadOnlyList<(long StartId, long EndId)>> GetSurrogateIdRanges(...)
    {
        // Query: Get min/max surrogate IDs for resource type
        var minMaxQuery = @"
            SELECT
                MIN(ResourceSurrogateId) as MinId,
                MAX(ResourceSurrogateId) as MaxId
            FROM dbo.Resources
            WHERE ResourceTypeId = (SELECT ResourceTypeId FROM dbo.ResourceTypes WHERE Name = @ResourceType)
              AND ResourceSurrogateId BETWEEN @StartId AND @EndId
              AND IsDeleted = 0";

        var (minId, maxId) = await database.QuerySingleAsync(minMaxQuery, ...);

        // Divide range into N buckets
        var ranges = new List<(long, long)>();
        var bucketSize = (maxId - minId) / numberOfRanges;

        for (int i = 0; i < numberOfRanges; i++)
        {
            var start = minId + (i * bucketSize);
            var end = (i == numberOfRanges - 1) ? maxId : start + bucketSize - 1;
            ranges.Add((start, end));
        }

        return ranges.AsReadOnly();
    }
}
```

### Export Worker Job with Surrogate ID Filtering

```csharp
public class ExportWorkerActivity : AsyncTaskActivity<ExportWorkerInput, ExportWorkerOutput>
{
    protected override async Task<ExportWorkerOutput> ExecuteAsync(
        TaskContext context,
        ExportWorkerInput input)
    {
        // input contains: ResourceType, StartSurrogateId, EndSurrogateId

        var searchOptions = new SearchOptions
        {
            ResourceType = input.ResourceType,
            MaxItemCount = 50000,
            // Additional parameters for surrogate ID filtering
            // Implementation depends on ISearchService
        };

        await using var writer = await _writerFactory.CreateAsync(...);

        var totalExported = 0;
        var enumerator = _searchService
            .SearchStreamAsync(searchOptions, CancellationToken.None)
            .GetAsyncEnumerator();

        while (await enumerator.MoveNextAsync())
        {
            var resource = enumerator.Current;

            // Filter: Only include resources in the assigned range
            if (resource.SurrogateId >= input.StartSurrogateId &&
                resource.SurrogateId <= input.EndSurrogateId)
            {
                await writer.WriteResourceAsync(resource, CancellationToken.None);
                totalExported++;
            }
        }

        await writer.FlushAsync(CancellationToken.None);

        return new ExportWorkerOutput(
            ResourceType: input.ResourceType,
            TotalResourcesExported: totalExported);
    }
}
```

### Coordinator Job (Simplified for Ignixa)

```csharp
public class ExportCoordinatorOrchestration : TaskOrchestration<ExportOutput, ExportInput>
{
    public override async Task<ExportOutput> RunTask(
        OrchestrationContext context,
        ExportInput input)
    {
        // 1. Get resource types
        var resourceTypes = input.ResourceTypes.Any()
            ? input.ResourceTypes
            : GetDefaultResourceTypes();

        // 2. For each resource type, get surrogate ID ranges
        var workerTasks = new List<Task<ExportWorkerOutput>>();

        foreach (var resourceType in resourceTypes)
        {
            // Get ranges for this resource type (determines parallelism)
            var getRangesTask = context.ScheduleTask<IReadOnlyList<(long, long)>>(
                typeof(GetSurrogateIdRangesActivity),
                new GetRangesInput(ResourceType: resourceType, NumberOfRanges: 4));

            var ranges = await getRangesTask;

            // 3. Queue one worker job per range
            foreach (var (startId, endId) in ranges)
            {
                var workerTask = context.ScheduleTask<ExportWorkerOutput>(
                    typeof(ExportWorkerActivity),
                    new ExportWorkerInput(
                        ResourceType: resourceType,
                        StartSurrogateId: startId,
                        EndSurrogateId: endId));

                workerTasks.Add(workerTask);
            }
        }

        // 4. Wait for all workers to complete
        var allResults = await Task.WhenAll(workerTasks);

        var totalExported = allResults.Sum(r => r.TotalResourcesExported);

        return new ExportOutput(
            Success: true,
            TotalResourcesExported: totalExported);
    }
}
```

---

## SQL vs Cosmos Trade-offs

| Aspect | Cosmos (FeedRange) | SQL (Surrogate ID) |
|--------|-------------------|-------------------|
| **Partitioning** | Physical partitions | Logical ID ranges |
| **Overhead** | Low (uses existing metadata) | Medium (must query min/max) |
| **Range size** | Fixed (by partition) | Configurable |
| **Job count** | 6 types × 4-8 partitions | 6 types × 4-8 ranges |
| **Resume** | By feed range token | By surrogate ID |
| **Scalability** | Fixed by partition count | Configurable via NumberOfRanges |

---

## References

### Related Optimizations
- `SearchQueryInterpreter.cs` - Delegate compilation for 7x query speedup
- `StreamingBundleSerializer.cs` - Zero-copy streaming pattern
- `FhirJsonWriter.cs` - Efficient JSON writing

### Old Export Implementations (Reference)
- `SqlExportOrchestratorJob.cs` - SQL implementation with surrogate ID ranges
- `CosmosExportOrchestratorJob.cs` - Cosmos implementation with feed ranges
- Key: Both use 2+ level parallelization (resource type × range/partition)

### Azure Blob Storage
- Append Blob design: https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-append-blob
- Concurrency: Up to 4,500 req/sec per blob

### FHIR Bulk Data
- https://hl7.org/fhir/uv/bulkdata/ - Spec recommendations
- https://www.hl7.org/fhir/async-pattern.html - Async pattern details

---

## Summary

**Challenge:** Export 1M FHIR resources currently takes 12.5 minutes. Target: <1.25 minutes (10x faster).

**Solution:** 5-level architecture
1. **Parallel resource types** - Concurrent orchestration
2. **Write-ahead buffering** - Decouple search from I/O
3. **Zero-copy streaming** - Direct byte passthrough
4. **Full activity consolidation** - Reduce DurableTask overhead
5. **Pooled writes** - Concurrent blob uploads

**Expected result:** >10,000 resources/second throughput with <100MB peak memory.

**Timeline:** 3-4 weeks for production-ready implementation.
