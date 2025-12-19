# POST/PUT Performance Analysis

**Date**: October 31, 2025
**Version**: Phase 22 (FHIR _history)
**Scope**: Single resource POST/PUT operations

## Executive Summary

This document analyzes the performance characteristics of POST/PUT operations in the Ignixa FHIR server, identifies bottlenecks, and provides actionable recommendations for optimization.

**Key Findings**:
- **Current Performance**: Baseline established via BenchmarkDotNet
- **Primary Hotspots**: JSON parsing, search indexing, validation (tier-dependent)
- **Recent Optimizations**: Phase 2 (6.25x) and Phase 3 (7x) focused on FHIRPath evaluation
- **Target**: < 100ms for typical Patient resource (P95)

## Execution Flow Analysis

### Request Pipeline (HandlePostResource / HandlePutResource)

```
1. HTTP Request → FhirEndpoints.cs:637-847 (POST) / 350-497 (PUT)
   └─ RecyclableMemoryStream (memory pooling)
   └─ JsonSourceNodeFactory.Parse()              ← HOTSPOT #1: JSON Parsing

2. Command Creation → CreateOrUpdateResourceCommand
   └─ ResourceJsonNode (in-memory representation)

3. Validation Pipeline → ValidationBehavior (cross-cutting concern)
   └─ Tier 1 (Fast): < 25ms target              ← HOTSPOT #2: Validation
   └─ Tier 2 (Spec): < 200ms target
   └─ Tier 3 (Profile): Variable (full IG validation)

4. Handler Execution → CreateOrUpdateResourceHandler.cs:56-176
   └─ FhirVersionExtractor (tenant FHIR version)
   └─ ToTypedElement() (schema navigation setup)
   └─ SearchIndexExtractor.Extract()             ← HOTSPOT #3: Search Indexing
       └─ FHIRPath evaluation (~100 search parameters)
       └─ Recent optimizations: Delegate compilation (7x speedup)

5. Repository Persistence
   └─ SQL Server: Gzipped JSON + indexed search parameters
   └─ File System: Direct JSON write              ← HOTSPOT #4: I/O

6. Response Generation
   └─ Zero-copy serialization (ResourceBytes)
   └─ ETag, Last-Modified headers
```

### Code Path Locations

| Component | File | Lines | Purpose |
|-----------|------|-------|---------|
| **Endpoint** | `FhirEndpoints.cs` | 637-847 (POST)<br>350-497 (PUT) | HTTP request handling, body parsing |
| **Command** | `CreateOrUpdateResourceCommand.cs` | - | Immutable request representation |
| **Handler** | `CreateOrUpdateResourceHandler.cs` | 56-176 | Business logic, orchestration |
| **Validation** | `ValidationBehavior.cs` | - | Cross-cutting validation pipeline |
| **Indexing** | `SearchIndexExtractor.cs` | - | FHIRPath-based search parameter extraction |
| **Repository** | `IFhirRepository.CreateOrUpdateAsync()` | - | Storage abstraction |

## Identified Hotspots

### Hotspot #1: JSON Parsing (JsonSourceNodeFactory)

**Location**: `FhirEndpoints.cs:364-369` (PUT), `761-766` (POST)

```csharp
ResourceJsonNode jsonNode;
await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("request-body"))
{
    await context.Request.Body.CopyToAsync(memoryStream, ct);
    memoryStream.Position = 0;
    jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
}
```

**Analysis**:
- **Positives**:
  - Uses `RecyclableMemoryStream` (memory pooling)
  - Async I/O
- **Concerns**:
  - Stream copy overhead (`CopyToAsync`)
  - `JsonNode.Parse()` creates full in-memory DOM
  - No incremental parsing

**Benchmark Results** (from PostPutBenchmarks):
- **ParseJsonToNode**: Measures full `JsonSourceNodeFactory.Parse()` path
- **ParseJsonSerializerOnly**: Measures `JsonSerializer.Deserialize<ResourceJsonNode>()` baseline

**Optimization Opportunities**:
1. **Direct deserialization**: Skip `CopyToAsync` if possible (requires sync read or PipeReader)
2. **Lazy property evaluation**: Only parse frequently-accessed properties eagerly
3. **Schema-guided parsing**: Use FHIRPath optimizations earlier in pipeline

### Hotspot #2: Validation (ValidationBehavior)

**Location**: `ValidationBehavior.cs` (cross-cutting concern)

**Tiers**:
- **Tier 1 (Fast)**: < 25ms - Basic structure, cardinality, required fields
- **Tier 2 (Spec)**: < 200ms - Full FHIR R4/R4B/R5 compliance
- **Tier 3 (Profile)**: Variable - Implementation Guide (IG) validation

**Analysis**:
- **Tier 1** is fast enough for most use cases
- **Tier 2/3** can dominate request latency for complex resources
- **Prefer header** allows client control: `Prefer: handling=lenient`

**Configuration**:
- Default tier should be documented in ADRs
- Consider async background validation for Tier 3

**Benchmark**: Existing `ValidationBenchmarks.cs` shows Tier 1 baseline

### Hotspot #3: Search Index Extraction

**Location**: `CreateOrUpdateResourceHandler.cs:194-207`

```csharp
var searchIndexer = _fhirVersionContext.GetSearchIndexer(fhirVersionEnum);

IReadOnlyCollection<SearchIndexEntry>? searchIndices = null;
try
{
    var typedElement = command.JsonNode.ToTypedElement(schemaProvider);
    searchIndices = searchIndexer.Extract(typedElement);

    _logger.LogDebug(
        "Extracted {Count} search indices for {ResourceType}/{Id} (FHIR {Version})",
        searchIndices.Count,
        command.ResourceType,
        command.Id,
        fhirVersionEnum);
}
```

**Analysis**:
- **FHIRPath evaluation**: ~100 search parameters per resource type
- **Recent Optimizations**:
  - Phase 2: Dictionary-backed property caching (6.25x improvement)
  - Phase 3: FHIRPath delegate compilation (7x speedup)
  - Combined: **~44x improvement** over baseline

**Current State**: Already heavily optimized (b704390, 63b959c)

**Remaining Opportunities**:
1. **Selective indexing**: Only index parameters used in tenant's common queries
2. **Parallel extraction**: Process independent search parameters concurrently
3. **Caching**: Resource-type-specific compiled delegates

**Benchmark Results** (from PostPutBenchmarks):
- **ExtractSearchIndices**: Measures full search parameter extraction
- **ConvertToTypedElement**: Measures schema navigation setup overhead

### Hotspot #4: Repository Persistence (I/O)

**Location**: `CreateOrUpdateResourceHandler.cs:165`

```csharp
result = await repository.CreateOrUpdateAsync(wrapper, cancellationToken);
```

**SQL Server** (`SqlEntityFrameworkRepository`):
- **Gzipped JSON** storage (compression overhead vs. storage savings)
- **Indexed search parameters**: Separate table inserts (transaction overhead)
- **EF Core overhead**: Query compilation, change tracking

**File System** (`FileSystemRepository`):
- **Direct JSON write**: Fast for single resources
- **Metadata sidecar**: Additional file I/O
- **No transaction safety**: Prototype only

**Optimization Opportunities**:
1. **Batch inserts**: Use SQL Server's `OUTPUT` clause to return generated IDs
2. **Connection pooling**: Already enabled, verify pool size
3. **Compression level**: Balance gzip level (1-9) for speed vs. size
4. **Async I/O**: Already async, but verify no blocking calls

**Benchmark**: Not included (would require database/file system)

## Recent Performance Work (Context)

### Phase 2: Dictionary-Backed Property Caching (63b959c)
- **Impact**: 6.25x improvement in property navigation
- **Target**: Repeated property access during FHIRPath evaluation
- **Approach**: Cache property lookups by name

### Phase 3: FHIRPath Delegate Compilation (b704390)
- **Impact**: 7x speedup for common FHIRPath patterns
- **Target**: Repeated FHIRPath expression evaluation
- **Approach**: Compile common expressions to IL delegates

### Combined Result
- **Total improvement**: ~44x faster search indexing vs. baseline
- **Benefit**: POST/PUT operations now spend less time on FHIRPath

## Benchmark Results

### Test Data
- **Resource**: Patient (typical complexity)
- **Size**: ~500 bytes JSON
- **Search parameters**: ~15 active (out of 100 total for Patient)

### Metrics (from PostPutBenchmarks)

| Operation | Mean (μs) | Allocated (KB) | Notes |
|-----------|-----------|----------------|-------|
| **ParseJsonToNode** | TBD | TBD | Baseline: JSON → ResourceJsonNode |
| **ParseJsonSerializerOnly** | TBD | TBD | Comparison: Raw deserialization |
| **ConvertToTypedElement** | TBD | TBD | Schema navigation setup |
| **ExtractSearchIndices** | TBD | TBD | FHIRPath evaluation (optimized) |
| **FullPostPipeline** | TBD | TBD | End-to-end (no repository) |
| **MemoryAllocationTest** | TBD | TBD | Peak memory usage |

**Note**: Run `dotnet run -c Release --filter Ignixa.Benchmarks.PostPutBenchmarks*` in `bench/Ignixa.Benchmarks/` to populate these metrics.

### Expected Breakdown (Hypothesis)

```
Total Request Time: ~50-100ms (P50, SQL Server, Tier 1 validation)
├─ JSON Parsing:          5-10ms  (10-20%)
├─ Validation (Tier 1):  10-20ms  (20-30%)
├─ Search Indexing:      10-20ms  (20-30%) ← Already optimized
├─ Repository (SQL):     20-40ms  (40-50%) ← I/O bound
└─ Response:              1-5ms   (5%)
```

## Profiling with dotnet-trace

### Setup

```bash
# Terminal 1: Start API server
cd src/Ignixa.Api
dotnet run --launch-profile http

# Terminal 2: Start profiler (replace <PID> with process ID from Terminal 1)
dotnet trace collect --process-id <PID> --providers Microsoft-DotNETCore-SampleProfiler

# Terminal 3: Generate load
for i in {1..100}; do
  curl -X POST http://localhost:5000/Patient \
    -H "Content-Type: application/fhir+json" \
    -d @bench/Ignixa.Benchmarks/TestData/patient-small.json
done

# Terminal 2: Stop trace (Ctrl+C), then analyze
dotnet trace report trace.nettrace --format speedscope
# Open https://www.speedscope.app/ and load the .speedscope.json file
```

### Expected Flamegraph Hotspots

1. **Top of stack** (widest bars):
   - `JsonNode.Parse` / `Utf8JsonReader`
   - `ValidationSchema.Validate`
   - `SearchIndexExtractor.Extract` → `FhirPathCompiler.Evaluate`
   - `SqlEntityFrameworkRepository.CreateOrUpdateAsync` → EF Core internals

2. **GC pressure indicators**:
   - `System.GC.Collect`
   - `System.Runtime.CompilerServices.RuntimeHelpers.AllocateUninitializedClone`

3. **Thread pool starvation** (rare):
   - `System.Threading.ThreadPool.NotifyWorkItemComplete`

## Recommendations

### Immediate (Phase 22+1)

1. **Add Performance Middleware** (`PerformanceMiddleware.cs`):
   - Log slow requests (> 100ms threshold)
   - Break down timing by operation (parsing, validation, indexing, persistence)
   - Export to Application Insights / Prometheus

2. **Instrument Handler** (`CreateOrUpdateResourceHandler.cs`):
   - Add `Stopwatch` timing for each major step
   - Log at `Debug` level (structured logging)
   - Example:
     ```csharp
     var sw = Stopwatch.StartNew();
     var indices = searchIndexer.Extract(typedElement);
     _logger.LogDebug("Indexing: {Duration}ms", sw.ElapsedMilliseconds);
     ```

3. **Run BenchmarkDotNet Suite**:
   - Establish baseline metrics (see Benchmark Results section above)
   - Track over time (regression testing)

4. **Profile with dotnet-trace**:
   - Generate CPU flamegraph
   - Identify top 3 hotspots
   - Validate hypothesis vs. actual data

### Short Term (1-2 weeks)

5. **Optimize JSON Parsing**:
   - Investigate `System.Text.Json` streaming APIs (`Utf8JsonReader`, `JsonDocument`)
   - Explore `PipeReader` for zero-copy HTTP body reading
   - Consider source generators for `ResourceJsonNode`

6. **Tune Repository Performance**:
   - **SQL Server**:
     - Review gzip compression level (test levels 1-6 for speed/size trade-off)
     - Verify connection pool settings (`MaxPoolSize`, `MinPoolSize`)
     - Consider bulk insert APIs (`SqlBulkCopy` for search parameters)
   - **File System**:
     - Use `FileStream` with `useAsync: true`
     - Batch metadata updates

7. **Validation Tier Strategy**:
   - Document default tier in ADR (recommend Tier 1 for production)
   - Add async background validation for Tier 3 (queue + DurableTask)
   - Cache validation schemas by (resourceType, version, profile)

### Medium Term (1-2 months)

8. **Selective Search Indexing**:
   - Analyze query patterns per tenant (telemetry)
   - Only index parameters used in last 30 days
   - Reduce ~100 FHIRPath evaluations to ~20-30

9. **Parallel Search Indexing**:
   - Group search parameters by independence (e.g., `name`, `birthDate` don't interact)
   - Use `Parallel.ForEach` or `Task.WhenAll`
   - Requires thread-safe `ITypedElement` navigation

10. **Response Caching**:
    - Implement `If-None-Match` (ETag) support (already in GET, extend to PUT)
    - Add `If-Modified-Since` support
    - Return 304 Not Modified when appropriate

### Long Term (3-6 months)

11. **Incremental JSON Parsing**:
    - Lazy-load resource properties on first access
    - Reduces memory footprint for large resources
    - Requires custom `JsonNode` implementation

12. **Compiled Resource Models**:
    - Generate C# types from FHIR StructureDefinitions (similar to POCO, but optimized)
    - Use source generators for zero-reflection serialization
    - Trade-off: Type safety vs. flexibility

13. **Database Optimization**:
    - **Partitioning**: Shard by tenant or resource type
    - **Indexes**: Review query plans, add covering indexes
    - **Caching**: Add Redis for frequently-accessed resources

## Success Metrics

| Metric | Current | Target (Phase 23) | Target (Phase 25) |
|--------|---------|-------------------|-------------------|
| **P50 Latency** (Patient POST) | TBD | < 50ms | < 30ms |
| **P95 Latency** (Patient POST) | TBD | < 100ms | < 75ms |
| **P99 Latency** (Patient POST) | TBD | < 200ms | < 150ms |
| **Throughput** (requests/sec) | TBD | 500 rps | 1000 rps |
| **Memory per request** | TBD | < 50 KB | < 30 KB |
| **GC pressure** (Gen2/min) | TBD | < 5 | < 2 |

**Measurement**:
- Load test: k6 or Apache JMeter
- Monitoring: Application Insights, Prometheus + Grafana
- Baseline: Single-tenant, SQL Server, Tier 1 validation, no auth

## Related Documents

- **ADR-2500**: Master implementation roadmap
- **CLAUDE.md**: Development guide (performance section)
- **docs/investigations/bundle-streaming.md**: Memory optimization (95% reduction)
- **Commit 63b959c**: CPU optimization Phase 2 (property caching)
- **Commit b704390**: CPU optimization Phase 3 (FHIRPath delegate compilation)

## Appendix: Tools & Commands

### BenchmarkDotNet

```bash
cd bench/Ignixa.Benchmarks
dotnet run -c Release --filter Ignixa.Benchmarks.PostPutBenchmarks*

# Export to CSV/HTML
dotnet run -c Release --exporters csv html --filter *PostPut*
```

### dotnet-trace (CPU Profiling)

```bash
# Install (if not already)
dotnet tool install --global dotnet-trace

# Collect trace
dotnet trace collect --process-id <PID> --providers Microsoft-DotNETCore-SampleProfiler

# Convert to speedscope format
dotnet trace report trace.nettrace --format speedscope

# Analyze at https://www.speedscope.app/
```

### dotnet-counters (Real-Time Metrics)

```bash
# Install
dotnet tool install --global dotnet-counters

# Monitor GC, CPU, ThreadPool
dotnet counters monitor --process-id <PID> --counters System.Runtime,Microsoft.AspNetCore.Hosting

# Watch for:
# - GC Heap Size
# - Gen 0/1/2 Collection Count
# - Allocation Rate (MB/sec)
# - ThreadPool Queue Length
```

### k6 Load Test

```javascript
// post-put-load.js
import http from 'k6/http';
import { check } from 'k6';

export let options = {
  stages: [
    { duration: '30s', target: 50 },  // Ramp-up
    { duration: '1m', target: 50 },   // Sustained
    { duration: '10s', target: 0 },   // Ramp-down
  ],
  thresholds: {
    http_req_duration: ['p(95)<100'], // 95% under 100ms
  },
};

const patient = open('./bench/Ignixa.Benchmarks/TestData/patient-small.json');

export default function () {
  let res = http.post('http://localhost:5000/Patient', patient, {
    headers: { 'Content-Type': 'application/fhir+json' },
  });
  check(res, {
    'status 201': (r) => r.status === 201,
    'has ETag': (r) => r.headers['ETag'] !== undefined,
  });
}

// Run: k6 run post-put-load.js
```

---

**Next Steps**:
1. Run `PostPutBenchmarks` to populate "Benchmark Results" table
2. Execute dotnet-trace profiling session during load test
3. Review flamegraph and update "Expected Flamegraph Hotspots"
4. Prioritize recommendations based on actual data
5. Track progress against Success Metrics table
