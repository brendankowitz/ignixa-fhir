# ADR 2502: Phase 1.1 - Bundle Processing with Channels

## Status

Accepted (with Phase 1.1a Enhancement)

## Context

Following the Prototype Phase, we add Bundle transaction support to enable atomic multi-resource operations. This phase adopts the ASP.NET Core pipeline pattern from `bundle-processing-with-channels.md` investigation.

**Related Investigations**:
- `bundle-processing-with-channels.md` - Channel-based parallel bundle execution
- `dynamic-fhir-routing.md` - Generic endpoint routing (eliminates per-resource controllers)
- `bundle-deferred-writes.md` - Two-phase channel architecture with deferred writes
- `bundle-streaming-parser.md` - Utf8JsonReader streaming parser for memory optimization

### Key Innovation: Pipeline Routing vs Switch Statements

**Problem**: Bundles can contain ANY FHIR interaction. Legacy code uses switch statements that must be kept in sync.

**Solution**: Use ASP.NET Core pipeline routing with mini `HttpContext` objects:

```csharp
// Create mini HttpContext for bundle entry
using var httpContext = _httpContextFactory.Create(...);
httpContext.Request.Method = entry.HttpVerb;  // PUT, POST, DELETE, etc.
httpContext.Request.Path = entry.RequestUrl;   // Patient/123

// Execute through pipeline - automatic routing!
await _pipeline(httpContext);
```

**Benefits**:
- No switch statements
- Supports ANY FHIR operation automatically
- New operations work immediately

## Decision

Implement Bundle transaction processing using:
1. **ASP.NET Core pipeline routing** for automatic handler discovery
2. **System.Threading.Channels** for parallel execution
3. **Reference resolution** for bundle-local references

### Architecture

```csharp
public class BundleProcessor
{
    private readonly IHttpContextFactory _httpContextFactory;
    private readonly RequestDelegate _pipeline;

    public async ValueTask<Bundle> ProcessTransactionAsync(
        Bundle bundle,
        CancellationToken ct)
    {
        // 1. Resolve references (urn:uuid: -> actual IDs)
        var referenceMap = await BuildReferenceMapAsync(bundle, ct);

        // 2. Group by HTTP verb (POST, then PUT, then DELETE)
        var groups = GroupEntriesByVerb(bundle.Entry);

        var results = new List<BundleEntryResponse>();

        foreach (var group in groups)
        {
            // 3. Process group in parallel using channels
            var groupResults = await ProcessVerbGroupWithChannelAsync(
                group.Entries,
                referenceMap,
                ct);

            results.AddRange(groupResults);
        }

        // 4. Build response bundle
        return CreateResponseBundle(results);
    }

    private async ValueTask<List<BundleEntryResponse>> ProcessVerbGroupWithChannelAsync(
        List<BundleEntryContext> entries,
        ReferenceResolutionContext referenceContext,
        CancellationToken ct)
    {
        // Create bounded channel for backpressure
        var channel = Channel.CreateBounded<BundleEntryContext>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        // Producer: feed entries
        var producer = Task.Run(async () =>
        {
            foreach (var entry in entries)
                await channel.Writer.WriteAsync(entry, ct);
            channel.Writer.Complete();
        }, ct);

        // Consumers: process in parallel (10 concurrent)
        var consumers = Enumerable.Range(0, 10)
            .Select(_ => ProcessEntriesFromChannelAsync(
                channel.Reader, referenceContext, ct))
            .ToArray();

        await Task.WhenAll(consumers.Append(producer).ToArray());

        return _results.ToList();
    }

    private async Task ProcessEntriesFromChannelAsync(
        ChannelReader<BundleEntryContext> reader,
        ReferenceResolutionContext referenceContext,
        CancellationToken ct)
    {
        await foreach (var entry in reader.ReadAllAsync(ct))
        {
            var response = await ExecuteEntryAsync(entry, referenceContext, ct);
            _results.Add(response);
        }
    }

    private async ValueTask<BundleEntryResponse> ExecuteEntryAsync(
        BundleEntryContext entry,
        ReferenceResolutionContext referenceContext,
        CancellationToken ct)
    {
        // Create mini HttpContext
        using var httpContext = _httpContextFactory.Create(new FeatureCollection());

        // Build request from bundle entry
        httpContext.Request.Method = entry.HttpVerb.ToString();
        httpContext.Request.Path = $"/{entry.RequestUrl}";

        // Resolve bundle-local references (urn:uuid:...)
        if (entry.Resource != null)
        {
            var resolvedResource = referenceContext.ResolveReferences(entry.Resource);
            httpContext.Request.Body = SerializeToStream(resolvedResource);
        }

        // Execute through ASP.NET Core pipeline
        // This automatically routes to correct handler!
        await _pipeline(httpContext);

        // Extract response
        return await ExtractResponseAsync(httpContext, ct);
    }
}
```

## Phase 1.1a: Streaming & Deferred Writes Enhancement

**Status**: Accepted (October 9, 2025)
**Timeline**: 4 weeks (76 hours)
**Rationale**: Early investigation revealed significant performance opportunities that justify extending Phase 1.1 before proceeding to Phase 1.2.

### Investigation Outcomes

Following completion of the initial Phase 1.1 planning, two critical investigations revealed optimization opportunities that should be implemented early:

#### Investigation 1: Bundle Deferred Writes (Two-Phase Channel Architecture)

**Document**: `docs/investigations/bundle-deferred-writes.md`

**Decision**: Implement two-phase channel architecture in Phase 1.1a

**Key Findings**:
- File-based storage: +5-10% throughput with atomic commit guarantees
- SQL Server (future): +50-70% throughput via bulk insert operations
- Cosmos DB (future): +60-80% throughput via batch API usage
- Zero memory overhead (same as single-phase approach)

**Architecture Pattern**:
```csharp
// Phase 1: Entry Execution (Parallel)
Bundle Entry → Execute Handler → Queue Write → Return Task<ResourceKey>
                                       ↓
                                 Write Channel

// Phase 2: Batch Writing (Background)
Write Channel → Batch Processor → IFhirRepository.BatchWriteAsync → DB
```

**Components Created**:
1. **DeferredWriteOperation** - Container for queued write operation with TaskCompletionSource
2. **DeferredWriteCoordinator** - Manages write queue and batch processing using System.Threading.Channels
3. **IFhirRepository.BatchWriteAsync** - New method for batch write operations

**Rationale for Early Implementation**:
1. Low cost to implement (32 hours in Week 1 of Phase 1.1a)
2. Minimal risk with file-based storage (<5% performance impact, easy revert)
3. High value for future SQL/Cosmos implementations (validates pattern early)
4. Better transaction safety (atomic commits via manifest-based pattern)
5. Team learning opportunity (TaskCompletionSource pattern with RunContinuationsAsynchronously)

**TaskCompletionSource Pattern**:
The coordinator uses `TaskCompletionSource<ResourceKey>` to create a "promise" that handlers return immediately. The batch processor later completes these promises after writing to the data layer, enabling parallel entry execution while maintaining atomic batch commits.

#### Investigation 2: Streaming Bundle Parser (Utf8JsonReader)

**Document**: `docs/investigations/bundle-streaming-parser.md`

**Decision**: Implement Utf8JsonReader streaming parser with manual state machine

**Key Findings**:
- 99% memory reduction (<1MB vs 100MB for 1000-entry bundle)
- 840x faster time to first entry (50ms vs 42 seconds)
- +50% throughput under high load
- Zero-copy JSON parsing with ArrayPool<byte> for buffer management

**Architecture Pattern**:
```csharp
HTTP Request Body Stream (chunked upload)
  ↓ (read 8KB chunks with ArrayPool)
Utf8JsonReader (parse incrementally with ReadOnlySpan<byte>)
  ↓ (manual state machine tracks JSON context)
Yield BundleEntryContext (IAsyncEnumerable)
  ↓
Channel → BundleEntryExecutor → DeferredWriteCoordinator
```

**Rejected Alternative**: JsonDocument.ParseAsync (would buffer entire bundle in memory)

**Components to Create**:
1. **BundleParserState** - State machine helper for JSON navigation and entry construction
2. **StreamingBundleParser** - Utf8JsonReader implementation with ArrayPool buffer management
3. **PreferHeaderParser** - Detection for `Prefer: streaming` HTTP header

**Rationale for Early Implementation**:
1. Enables true wire-to-database streaming (process first entry while receiving second entry)
2. Production-proven pattern (used by Microsoft FHIR Server ImportBundleParser)
3. Unlocks scenarios with very large bundles (10k+ entries) that would otherwise fail
4. Minimal complexity cost (state machine encapsulated in BundleParserState helper)
5. Opt-in via HTTP header (backward compatibility with buffered path)

**Limitations in Streaming Mode**:
- No `urn:uuid:` reference resolution (requires fully-resolved resource IDs)
- No conditional references (requires direct resource IDs)
- Batch bundles only (transaction bundles require reference resolution)

### Phase 1.1a Implementation Timeline

**Total**: 4 weeks, 76 hours (includes documentation and integration)

#### Week 1: Two-Phase Write Architecture (32 hours)
- Days 9-10: Implement DeferredWriteCoordinator and DeferredWriteOperation
- Days 10-11: Add IFhirRepository.BatchWriteAsync interface and file-based implementation
- Day 11: Integrate with BundleProcessor
- Day 12: Update handlers to use coordinator, manual integration testing

**Deliverables**:
- DeferredWriteOperation.cs with TaskCompletionSource pattern
- DeferredWriteCoordinator.cs with bounded channel management
- IFhirRepository.BatchWriteAsync method signature
- FileBasedFhirRepository.BatchWriteAsync with manifest-based atomic commits

#### Week 2: Streaming Bundle Parser (28 hours)
- Days 13-14: Create BundleParserState and StreamingBundleParser core logic
- Days 13-14: Implement ArrayPool buffer management and state machine token processing
- Day 15: Add PreferHeaderParser and integrate with FhirEndpoints.HandleBundle
- Day 16: Error handling, validation, logging, and manual integration testing

**Deliverables**:
- BundleParserState.cs with JSON navigation state tracking
- StreamingBundleParser.cs with Utf8JsonReader and incremental parsing
- PreferHeaderParser.cs for `Prefer: streaming` header detection
- BundleProcessor.ProcessStreamAsync method for streaming entry execution

#### Week 3: Prefer: streaming Optimization (12 hours)
- Days 17-18: Refine streaming path error handling (partial parse errors, validation)
- Day 19: Performance tuning (buffer sizes, batch sizes)
- Day 19: Documentation updates

**Deliverables**:
- Enhanced error handling for streaming mode (fail-fast for transactions, partial success for batches)
- Validation for streaming requirements (no urn:uuid references)
- Performance benchmarks comparing buffered vs streaming approaches

#### Week 4: Documentation and Integration (4 hours)
- Day 20: Update ADR-2502 with Phase 1.1a outcomes
- Day 20: Update code documentation and inline comments

**Deliverables**:
- Updated ADR documents
- Investigation documents archived with "Implemented" status
- Code comments explaining TaskCompletionSource and state machine patterns

### Success Criteria for Phase 1.1a

**Functional**:
- DeferredWriteCoordinator processes batch writes correctly
- TaskCompletionSource pattern completes handler promises after batch write
- StreamingBundleParser parses 1000-entry bundle with <1MB memory usage
- `Prefer: streaming` header routes to streaming parser path
- Backward compatibility: standard path works without header

**Performance**:
- File-based bundles show atomic commit behavior (transaction manifest created/deleted)
- 1000-entry bundle memory usage: <1MB (streaming) vs ~100MB (buffered)
- Time to first entry: <100ms (streaming) vs >2s (buffered)
- Throughput improvement: +30-50% under high load

**Code Quality**:
- Clean separation: DeferredWriteCoordinator isolated from business logic
- Testable: Components can be unit tested independently (tests deferred per user request)
- Well-documented: TaskCompletionSource and state machine patterns explained in comments

### Architectural Benefits

**Early Pattern Validation**:
- Two-phase architecture proven with file storage before SQL/Cosmos implementation
- Team gains experience with advanced patterns (TaskCompletionSource, Utf8JsonReader, state machines)
- Low risk: file-based impact minimal, can revert if issues arise

**Foundation for Future Phases**:
- SQL Server (Phase 8/8a): BatchWriteAsync immediately enables 50-70% throughput gains via SqlBulkCopy
- Cosmos DB (Phase 9): BatchWriteAsync immediately enables 60-80% throughput gains via batch API
- Bulk Operations (Phase 13): Streaming parser pattern reusable for $import operations

**Production Readiness**:
- Atomic commits for transaction bundles (manifest-based recovery)
- Memory efficiency for large bundles (enables 10k+ entry scenarios)
- Wire-to-database streaming (reduced latency for first operation)

## Implementation Plan (Week 2 - Original Phase 1.1)

**Note**: This section describes the original Phase 1.1 plan. Phase 1.1a (above) represents an enhancement that occurs before Phase 1.2, extending the timeline by 4 weeks but providing significant architectural and performance benefits.

### Deliverables

✅ **Dynamic endpoint routing** - Migrate from PatientController to generic handlers (see `dynamic-fhir-routing.md`)
✅ **Bundle endpoint** - `POST /` with transaction bundle
✅ **Channel-based parallel execution** - 10 concurrent operations
✅ **Reference resolution** - `urn:uuid:` → actual resource IDs
✅ **Verb ordering** - POST → PUT → DELETE
✅ **80% test coverage**

### Routing Migration (Days 1-3)

**Problem**: Current implementation uses `PatientController` with hardcoded routes. FHIR R4 has 145+ resource types - creating 145+ controllers is not scalable.

**Solution**: Implement generic endpoint routing with RequestDelegate handlers (see `dynamic-fhir-routing.md`):

1. **Day 1-2**: Create generic handlers
   - `GetResourceHandler` - Handles GET /{resourceType}/{id}
   - `CreateOrUpdateResourceHandler` - Handles PUT /{resourceType}/{id}
   - `DeleteResourceHandler` - Handles DELETE /{resourceType}/{id}
   - `SearchResourcesHandler` - Handles GET /{resourceType}

2. **Day 2-3**: Create FhirEndpoints.cs
   - `MapGet("/{resourceType}/{id}", HandleGetResource)`
   - `MapPut("/{resourceType}/{id}", HandlePutResource)`
   - `MapDelete("/{resourceType}/{id}", HandleDeleteResource)`
   - `MapGet("/{resourceType}", HandleSearchResource)`
   - `MapPost("/", HandleBundle)` - Bundle endpoint

3. **Day 3**: Update Program.cs
   - Remove: `app.MapControllers()`
   - Add: `app.MapFhirEndpoints()`

4. **Day 3-4**: Migrate tests
   - Convert PatientController unit tests to integration tests
   - Delete PatientController.cs

**Result**: Zero controllers, automatic support for all 145+ resource types

### Performance Target

- 100-resource bundle: <500ms

## Success Criteria

### E2E Tests (from src-old/test)

✅ `BundleTransactionTests.cs` - **ALL** transaction scenarios
✅ `BundleBatchTests.cs` - **ALL** batch scenarios

### Functional

✅ Transaction bundle creates multiple resources atomically
✅ Reference resolution works for `urn:uuid:` references
✅ Rollback on failure (future: Phase 3)
✅ Parallel execution with channels

## Consequences

### Positive

1. **Extensible**: New operations work automatically via routing
2. **Performant**: Channels enable parallel execution
3. **Proven Pattern**: Based on working microsoft/fhir-server code
4. **Early Optimization**: Phase 1.1a validates advanced patterns with minimal risk
5. **Memory Efficient**: Streaming parser enables very large bundles (10k+ entries)
6. **Atomic Commits**: Two-phase architecture provides transaction safety
7. **Future-Ready**: BatchWriteAsync foundation for SQL/Cosmos 50-80% throughput gains

### Negative

1. **Mini HttpContext overhead**: Slight performance cost vs direct calls
2. **Extended Timeline**: Phase 1.1a adds 4 weeks to original plan
3. **Pattern Complexity**: TaskCompletionSource and state machines require careful implementation
4. **Streaming Limitations**: No urn:uuid references or conditional references in streaming mode

### Mitigation

1. **Backward Compatibility**: Streaming mode is opt-in via `Prefer: streaming` header
2. **Comprehensive Documentation**: Investigation documents provide detailed rationale and examples
3. **Encapsulation**: State machine complexity isolated in BundleParserState helper
4. **Testing Deferred**: Unit tests deferred to later phase per user request, manual integration testing covers functionality

## Timeline Impact

**Original Phase 1.1**: Week 2 (16 hours)
**With Phase 1.1a Enhancement**: Weeks 2-5 (92 hours total)

**Breakdown**:
- Week 2: Original Phase 1.1 implementation (16 hours)
- Weeks 3-6: Phase 1.1a enhancement (76 hours)
  - Week 3: Two-Phase Write Architecture (32 hours)
  - Week 4: Streaming Bundle Parser (28 hours)
  - Week 5: Prefer: streaming Optimization (12 hours)
  - Week 6: Documentation and Integration (4 hours)

**Phase 1.2 Start Date**: Shifted from Week 3 to Week 7

## References

- Investigation: `bundle-processing-with-channels.md` - Channel-based parallel execution
- Investigation: `dynamic-fhir-routing.md` - Generic endpoint routing architecture
- Investigation: `bundle-deferred-writes.md` - Two-phase channel architecture (Phase 1.1a)
- Investigation: `bundle-streaming-parser.md` - Utf8JsonReader streaming parser (Phase 1.1a)
- Previous: ADR-2501 (Prototype Phase)
- Next: ADR-2503 (Phase 1.2 - Search Implementation)
