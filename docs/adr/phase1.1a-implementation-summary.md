# Phase 1.1a Implementation Summary: Streaming & Deferred Writes

**Date**: October 9, 2025
**Status**: Accepted
**ADRs Updated**: ADR-2502, ADR-2500

---

## Executive Summary

Following completion of two major investigations for Phase 1.1a (Bundle Processing Enhancement), we have made the decision to implement streaming bundle parsing and two-phase deferred writes **early in the roadmap** (Weeks 3-6) rather than deferring these optimizations to later phases.

**Key Decision**: Extend Phase 1.1 from 1 week to 5 weeks (add Phase 1.1a) to implement:
1. Two-phase channel architecture with deferred writes
2. Streaming bundle parser with Utf8JsonReader

**Timeline Impact**: +4 weeks to overall roadmap (112 weeks → 116 weeks)

**Rationale**: Low-cost, low-risk investment that validates advanced patterns early and yields 50-80% performance gains in future SQL/Cosmos implementations.

---

## Investigation 1: Bundle Deferred Writes

### Document
`docs/investigations/bundle-deferred-writes.md`

### Decision
**ACCEPTED**: Implement two-phase channel architecture in Phase 1.1a (Week 3 of overall plan)

### Key Findings

| Storage Layer | Single-Phase (Baseline) | Two-Phase (Deferred) | Improvement |
|--------------|------------------------|---------------------|-------------|
| File-based   | Baseline               | +5-10%              | Atomic commits |
| SQL Server   | N/A (future)           | +50-70%             | Bulk inserts |
| Cosmos DB    | N/A (future)           | +60-80%             | Batch API |

### Architecture Pattern

**Two-Phase Flow**:
```
Phase 1: Entry Execution (Parallel)
  Bundle Entry → Execute Handler → Queue Write → Return Task<ResourceKey>
                                       ↓
                                 Write Channel
                                       ↓
Phase 2: Batch Writing (Background)
  Write Channel → Batch Processor → IFhirRepository.BatchWriteAsync → DB
```

**Key Innovation**: TaskCompletionSource<ResourceKey> creates a "promise" that handlers return immediately, which the batch processor completes after writing.

### Components Created

1. **DeferredWriteOperation.cs**
   - Container for queued write operation
   - Includes TaskCompletionSource for async completion
   - Holds resource data (ResourceType, ResourceId, ISourceNode, RawJson)

2. **DeferredWriteCoordinator.cs**
   - Manages write queue using System.Threading.Channels
   - Provides QueueWriteAsync for handlers
   - Provides ProcessBatchAsync for batch processor
   - Uses TaskCreationOptions.RunContinuationsAsynchronously to prevent deadlocks

3. **IFhirRepository.BatchWriteAsync**
   - New method signature for batch operations
   - File-based implementation with manifest-based atomic commits
   - Future SQL implementation will use SqlBulkCopy
   - Future Cosmos implementation will use batch API

### Rationale for Early Implementation

1. **Low Cost**: 32 hours implementation time (Week 3 of plan)
2. **Minimal Risk**: File-based performance impact <5%, easy to revert if issues
3. **High Value**: Validates pattern before SQL implementation, avoiding costly rework
4. **Transaction Safety**: Manifest-based atomic commits for bundle transactions
5. **Team Learning**: Experience with TaskCompletionSource pattern, RunContinuationsAsynchronously flag

### Example Code

```csharp
// Handler queues write and returns Task
var resultTask = await _writeCoordinator.QueueWriteAsync(
    resourceType: "Patient",
    resourceId: "123",
    resource: sourceNode,
    rawJson: json,
    cancellationToken);

// Batch processor completes the promise
foreach (var operation in batch)
{
    var result = await _repository.CreateOrUpdateAsync(...);
    operation.CompletionSource.SetResult(result); // Promise completed!
}
```

---

## Investigation 2: Streaming Bundle Parser

### Document
`docs/investigations/bundle-streaming-parser.md`

### Decision
**ACCEPTED**: Implement Utf8JsonReader streaming parser with manual state machine in Phase 1.1a (Week 4 of overall plan)

**REJECTED**: JsonDocument.ParseAsync (would buffer entire bundle in memory)

### Key Findings

| Metric | Buffered (Current) | Streaming (Utf8JsonReader) | Improvement |
|--------|-------------------|---------------------------|-------------|
| Memory (1000-entry bundle) | ~100 MB | <1 MB | 99% reduction |
| Time to First Entry | 42 seconds | 50 ms | 840x faster |
| Throughput (high load) | Baseline | +50% | Parallel processing |
| Code Complexity | Simple | Medium | State machine required |

### Architecture Pattern

**Streaming Flow**:
```
HTTP Request Body Stream (chunked upload)
  ↓ (read 8KB chunks with ArrayPool<byte>)
Utf8JsonReader (parse incrementally with ReadOnlySpan<byte>)
  ↓ (manual state machine tracks JSON context)
Yield BundleEntryContext (IAsyncEnumerable)
  ↓
Channel → BundleEntryExecutor → DeferredWriteCoordinator
```

**Key Innovation**: Utf8JsonReader with ArrayPool<byte> for zero-copy, zero-allocation parsing.

### Components to Create

1. **BundleParserState.cs**
   - State machine helper for JSON navigation
   - Tracks depth, current property, entry context
   - Captures resource JSON as it's parsed
   - Builds BundleEntryContext when entry completes

2. **StreamingBundleParser.cs**
   - Utf8JsonReader implementation with incremental parsing
   - ArrayPool<byte> buffer management (rent/return pattern)
   - Handles unconsumed bytes across buffer boundaries
   - Returns IAsyncEnumerable<BundleEntryContext>

3. **PreferHeaderParser.cs**
   - Detects `Prefer: streaming` HTTP header
   - Routes to streaming vs buffered path
   - Validates streaming requirements (no urn:uuid references)

### Rationale for Early Implementation

1. **Wire-to-Database Streaming**: Process first entry while receiving second entry (true streaming)
2. **Production-Proven**: Pattern used by Microsoft FHIR Server ImportBundleParser
3. **Unlocks Large Bundles**: Enables 10k+ entry scenarios that would otherwise fail
4. **Minimal Complexity**: State machine encapsulated in BundleParserState helper class
5. **Opt-In Design**: Backward compatibility via `Prefer: streaming` header

### Limitations in Streaming Mode

- No `urn:uuid:` reference resolution (requires fully-resolved resource IDs)
- No conditional references (requires direct resource IDs like `Patient/123`)
- Batch bundles only (transaction bundles require reference resolution)

### Example Code

```csharp
// Opt-in via HTTP header
curl -X POST http://localhost:5000/ \
  -H "Content-Type: application/fhir+json" \
  -H "Prefer: streaming" \
  --data-binary @large-bundle.json

// Streaming parser yields entries incrementally
await foreach (var entry in _streamingParser.ParseStreamAsync(httpRequest.Body, ct))
{
    // Process entry immediately (before next entry arrives!)
    var response = await _entryExecutor.ExecuteAsync(entry, ct);
}
```

---

## Phase 1.1a Implementation Timeline

**Total**: 4 weeks, 76 hours (plus original Phase 1.1: 1 week, 16 hours = 5 weeks, 92 hours total)

### Week 1 (Original Phase 1.1): Bundle Processing Foundation
**Duration**: 16 hours
**Deliverables**:
- POST / with transaction bundles
- ASP.NET Core pipeline routing (no switch statements)
- System.Threading.Channels for parallel execution
- Reference resolution for urn:uuid:

### Week 2 (Phase 1.1a): Two-Phase Write Architecture
**Duration**: 32 hours
**Days 9-10** (16 hours):
- Implement DeferredWriteOperation with TaskCompletionSource
- Implement DeferredWriteCoordinator with bounded channel
- Configure TaskCreationOptions.RunContinuationsAsynchronously

**Days 10-11** (8 hours):
- Add IFhirRepository.BatchWriteAsync interface method
- Implement FileBasedFhirRepository.BatchWriteAsync with transaction manifest

**Day 11** (4 hours):
- Integrate DeferredWriteCoordinator with BundleProcessor

**Day 12** (4 hours):
- Update handlers to use coordinator
- Manual integration testing (verify atomic commits)

### Week 3 (Phase 1.1a): Streaming Bundle Parser
**Duration**: 28 hours
**Days 13-14** (16 hours):
- Create BundleParserState.cs with state machine logic
- Implement StreamingBundleParser.cs core (Utf8JsonReader loop)
- Add ArrayPool<byte> buffer management (rent/return)
- Implement state machine token processing (ProcessToken method)
- Add resource JSON capture logic (CaptureResourceToken method)

**Day 15** (6 hours):
- Create PreferHeaderParser.cs for header detection
- Update FhirEndpoints.HandleBundle with streaming path
- Add BundleProcessor.ProcessStreamAsync method

**Day 16** (6 hours):
- Error handling (partial parse errors, validation)
- Logging and diagnostics (log buffer usage, parse metrics)
- Manual integration testing (test with 1000-entry bundle)

### Week 4 (Phase 1.1a): Prefer: streaming Optimization
**Duration**: 12 hours
**Days 17-18** (8 hours):
- Refine streaming path error handling
  - Fail-fast for transaction bundles (abort on first error)
  - Partial success for batch bundles (continue processing)
- Validation for streaming requirements
  - Reject urn:uuid references
  - Reject conditional references

**Day 19** (4 hours):
- Performance tuning (buffer sizes, batch sizes)
- Performance benchmarks (buffered vs streaming)

### Week 5 (Phase 1.1a): Documentation and Integration
**Duration**: 4 hours
**Day 20**:
- Update ADR-2502 with Phase 1.1a outcomes
- Update ADR-2500 master roadmap timeline
- Update code documentation and inline comments
- Archive investigation documents with "Implemented" status

---

## Success Criteria

### Functional Requirements

**Two-Phase Architecture**:
- DeferredWriteCoordinator processes batch writes correctly
- TaskCompletionSource pattern completes handler promises after batch write
- Transaction manifest created and deleted for atomic commits
- Handlers return immediately without blocking on write completion

**Streaming Parser**:
- StreamingBundleParser parses 1000-entry bundle with <1MB memory usage
- `Prefer: streaming` header routes to streaming parser path
- Backward compatibility: standard path works without header
- State machine correctly handles JSON spanning buffer boundaries

### Performance Requirements

**Memory**:
- 1000-entry bundle (50MB): <1MB peak memory (streaming) vs ~100MB (buffered)
- ArrayPool<byte> shows zero heap allocations for buffers

**Latency**:
- Time to first entry: <100ms (streaming) vs >2 seconds (buffered)
- File-based bundles show atomic commit behavior (manifest visible during processing)

**Throughput**:
- Throughput improvement: +30-50% under high load
- File-based storage: +5-10% (atomic commits avoid partial writes)

### Code Quality

**Separation of Concerns**:
- DeferredWriteCoordinator isolated from business logic (handlers don't know about batching)
- BundleParserState encapsulates state machine complexity
- PreferHeaderParser separates routing decision from parsing logic

**Testability**:
- Components can be unit tested independently
- Unit tests deferred to later phase per user request
- Manual integration testing covers functionality

**Documentation**:
- TaskCompletionSource pattern explained in code comments
- State machine logic documented in BundleParserState
- Investigation documents provide comprehensive rationale

---

## Architectural Benefits

### Early Pattern Validation

**File Storage First**:
- Two-phase architecture proven with file storage before SQL/Cosmos implementation
- Low risk: file-based performance impact minimal (<5%)
- Easy revert: Can fall back to single-phase if issues arise

**Team Learning**:
- Experience with TaskCompletionSource pattern and RunContinuationsAsynchronously flag
- Understanding of Utf8JsonReader and state machine patterns
- Knowledge transfer before complex SQL/Cosmos implementations

### Foundation for Future Phases

**SQL Server (Phase 8/8a)**:
- BatchWriteAsync immediately enables 50-70% throughput gains via SqlBulkCopy
- No rework needed: interface already designed for bulk operations
- Tested pattern: Already validated with file storage

**Cosmos DB (Phase 9)**:
- BatchWriteAsync immediately enables 60-80% throughput gains via batch API (100 operations per request)
- Atomic semantics: Cosmos batch API aligns with two-phase design
- Proven scalability: Streaming parser handles large bundles efficiently

**Bulk Operations (Phase 13)**:
- Streaming parser pattern reusable for $import operations
- Channel-based processing already established
- Memory efficiency critical for bulk scenarios

### Production Readiness

**Atomic Commits**:
- Transaction bundles truly atomic via manifest-based recovery
- File deletion signals successful commit
- Partial failures detectable (manifest still exists)

**Memory Efficiency**:
- Enables 10k+ entry bundle scenarios
- No memory spikes under load
- Predictable memory usage (<1MB per bundle regardless of size)

**Wire-to-Database Streaming**:
- Reduced latency for first operation (process while receiving)
- Better user experience for large uploads
- Parallel processing: Entry 1 executing while entry 2 parsing

---

## Timeline Impact Analysis

### Original Plan (ADR-2500)
- Phase 1.1: Week 2 (16 hours)
- Phase 1.2: Week 3 (16 hours)
- Total to Phase 20: 112 weeks, 1,796 hours

### With Phase 1.1a Enhancement
- Phase 1.1: Week 2 (16 hours)
- Phase 1.1a: Weeks 3-6 (76 hours)
- Phase 1.2: Week 7 (16 hours)
- Total to Phase 20: 116 weeks, 1,872 hours

### Impact Summary
- **Additional Weeks**: +4 weeks (3.6% increase)
- **Additional Hours**: +76 hours (4.2% increase)
- **All Subsequent Phases Shifted**: Phase 1.2+ start 4 weeks later
- **Phase 20 End Date**: Week 116 (was Week 112)

### Justification for Timeline Extension

**Cost-Benefit Analysis**:
- **Cost**: 76 hours implementation + 4-week schedule shift
- **Benefit**: 50-80% throughput gains in future phases (SQL, Cosmos, Bulk)
- **Risk Mitigation**: Validates patterns early with low-risk file storage
- **ROI**: Investment pays off in Phase 8+ (weeks 34+)

**Alternative Considered**:
- Defer to Phase 8/8a: Would require SQL implementation rework
- Risk of late discovery: Complex patterns harder to debug in SQL/Cosmos context
- Testing debt: Harder to isolate issues with multiple moving parts

**Decision Rationale**:
- Early implementation = early validation = lower risk
- File storage provides safe testbed for advanced patterns
- 4-week investment justified by 50-80% future gains

---

## Implementation Plan Summary

### Components to Implement

**Week 3 (Two-Phase Architecture)**:
1. `Ignixa.Domain/Models/DeferredWriteOperation.cs` - Write operation container
2. `Ignixa.Application/Bundle/DeferredWriteCoordinator.cs` - Channel-based coordinator
3. `Ignixa.Domain/Abstractions/IFhirRepository.cs` - Add BatchWriteAsync method
4. `Ignixa.DataLayer.FileSystem/FileBasedFhirRepository.cs` - Implement BatchWriteAsync

**Week 4 (Streaming Parser)**:
1. `Ignixa.Application/Bundle/BundleParserState.cs` - State machine helper
2. `Ignixa.Application/Bundle/StreamingBundleParser.cs` - Utf8JsonReader implementation
3. `Ignixa.Api/Http/PreferHeaderParser.cs` - Header detection utility
4. `Ignixa.Application/Bundle/BundleProcessor.cs` - Add ProcessStreamAsync method

**Week 5 (Optimization)**:
1. Enhanced error handling in StreamingBundleParser
2. Validation logic for streaming requirements
3. Performance tuning (buffer sizes, batch sizes)

**Week 6 (Documentation)**:
1. Update ADR-2502 with Phase 1.1a section
2. Update ADR-2500 timeline and architectural decisions
3. Add code comments explaining patterns
4. Archive investigation documents

### Integration Points

**DeferredWriteCoordinator**:
- Used by: BundleProcessor.ProcessAsync (replaces direct repository calls)
- Calls: IFhirRepository.BatchWriteAsync (new method)
- Pattern: Producer-consumer with TaskCompletionSource

**StreamingBundleParser**:
- Used by: FhirEndpoints.HandleBundle (when `Prefer: streaming` header present)
- Returns: IAsyncEnumerable<BundleEntryContext>
- Pattern: Iterator with Utf8JsonReader state machine

**BundleProcessor**:
- New method: ProcessStreamAsync (for streaming entries)
- Existing method: ProcessAsync (for buffered entries)
- Integration: Both methods use DeferredWriteCoordinator

### Testing Strategy

**Manual Integration Tests** (Week 3):
- Create 100-entry transaction bundle
- Verify transaction manifest created and deleted
- Verify all entries written atomically
- Test rollback behavior (simulate failure mid-bundle)

**Manual Integration Tests** (Week 4):
- Create 1000-entry batch bundle
- Verify memory usage <1MB during parsing
- Measure time to first entry (<100ms)
- Test buffer boundary scenarios (JSON token split across chunks)

**Manual Integration Tests** (Week 5):
- Test error handling (partial parse errors)
- Test validation (urn:uuid rejection)
- Benchmark buffered vs streaming throughput
- Verify backward compatibility (no header = buffered path)

**Unit Tests**: Deferred to later phase per user request

---

## Risk Assessment and Mitigation

### Identified Risks

**Risk 1: Pattern Complexity**
- **Description**: TaskCompletionSource and state machines may be difficult to implement correctly
- **Likelihood**: Medium
- **Impact**: Medium (bugs could cause deadlocks or memory leaks)
- **Mitigation**:
  - Use TaskCreationOptions.RunContinuationsAsynchronously to prevent deadlocks
  - Encapsulate state machine in BundleParserState helper class
  - Comprehensive inline documentation explaining patterns
  - Manual integration testing to verify behavior

**Risk 2: Timeline Extension**
- **Description**: 4-week extension delays subsequent phases
- **Likelihood**: Certain
- **Impact**: Low (all phases shifted equally, no critical dependencies)
- **Mitigation**:
  - Update ADR-2500 with new timeline
  - Communicate timeline change to stakeholders
  - Justify with cost-benefit analysis (50-80% future gains)

**Risk 3: Streaming Limitations**
- **Description**: No urn:uuid or conditional references in streaming mode
- **Likelihood**: Certain (by design)
- **Impact**: Low (opt-in feature, buffered path still available)
- **Mitigation**:
  - Document limitations clearly in investigation and ADR
  - Provide clear error messages when validation fails
  - Maintain backward compatibility via HTTP header opt-in

**Risk 4: File-Based Performance**
- **Description**: Two-phase architecture may not show significant gains with file storage
- **Likelihood**: Medium
- **Impact**: Low (5-10% gain still valuable, main goal is pattern validation)
- **Mitigation**:
  - Focus success criteria on atomic commits, not just speed
  - Measure SQL/Cosmos gains in future phases to validate investment
  - Document learnings for future implementations

### Risk Summary

**Overall Risk Level**: Low-Medium

**Rationale**:
- Patterns are production-proven (Microsoft FHIR Server)
- File storage provides safe testbed
- Opt-in design limits blast radius
- Well-documented investigations reduce implementation risk

---

## Success Metrics

### Immediate Metrics (Phase 1.1a Completion)

**Functional**:
- All bundle transaction E2E tests pass (from BundleTransactionTests.cs)
- All bundle batch E2E tests pass (from BundleBatchTests.cs)
- Streaming mode correctly rejects urn:uuid references
- Backward compatibility: non-streaming path unaffected

**Performance**:
- 1000-entry bundle memory: <1MB (streaming) vs ~100MB (buffered)
- Time to first entry: <100ms (streaming) vs >2s (buffered)
- File-based bundles show transaction manifest during processing
- Throughput improvement: +5-10% (file-based)

**Code Quality**:
- DeferredWriteCoordinator <200 lines (well-focused)
- BundleParserState <300 lines (encapsulates complexity)
- StreamingBundleParser <400 lines (core logic only)
- Zero compiler warnings, StyleCop compliant

### Future Metrics (Phase 8+ Validation)

**SQL Server (Phase 8/8a)**:
- BatchWriteAsync with SqlBulkCopy shows 50-70% throughput improvement
- 1000-entry bundle: <1s (was 15-20s with individual inserts)
- Pattern requires zero rework (interface already correct)

**Cosmos DB (Phase 9)**:
- BatchWriteAsync with batch API shows 60-80% throughput improvement
- 1000-entry bundle: <1s (was 8-10s with individual operations)
- Streaming parser handles 10k+ entry bundles without memory issues

**Bulk Operations (Phase 13)**:
- $import reuses StreamingBundleParser pattern
- Memory usage <10MB for 100k-entry NDJSON file
- Throughput: 10,000 resources/min target achieved

---

## Key Decisions Summary

### Decision 1: Implement Phase 1.1a
**Status**: ACCEPTED
**Rationale**: Low cost (76 hours), high value (50-80% future gains), low risk (file storage testbed)

### Decision 2: Two-Phase Architecture
**Status**: ACCEPTED
**Pattern**: DeferredWriteCoordinator with TaskCompletionSource
**Benefits**: Atomic commits (file), bulk operations (SQL/Cosmos), clean separation

### Decision 3: Streaming Parser with Utf8JsonReader
**Status**: ACCEPTED
**Rejected Alternative**: JsonDocument.ParseAsync (would buffer entire bundle)
**Benefits**: 99% memory reduction, 840x faster first entry, wire-to-database streaming

### Decision 4: Opt-In Streaming Mode
**Status**: ACCEPTED
**Mechanism**: `Prefer: streaming` HTTP header
**Rationale**: Backward compatibility, clear limitations (no urn:uuid references)

### Decision 5: Timeline Extension
**Status**: ACCEPTED
**Impact**: +4 weeks (112 → 116 weeks total)
**Justification**: Early pattern validation prevents costly rework in SQL/Cosmos phases

---

## Next Steps

### Week 3 (Days 9-12): Two-Phase Write Architecture
1. Create DeferredWriteOperation.cs in Ignixa.Domain/Models/
2. Create DeferredWriteCoordinator.cs in Ignixa.Application/Bundle/
3. Update IFhirRepository interface (add BatchWriteAsync method)
4. Implement FileBasedFhirRepository.BatchWriteAsync
5. Integrate DeferredWriteCoordinator with BundleProcessor
6. Update handlers to use coordinator (CreateOrUpdatePatientHandler, etc.)
7. Manual integration testing (verify atomic commits)

### Week 4 (Days 13-16): Streaming Bundle Parser
1. Create BundleParserState.cs in Ignixa.Application/Bundle/
2. Create StreamingBundleParser.cs in Ignixa.Application/Bundle/
3. Add ArrayPool<byte> buffer management
4. Implement state machine token processing
5. Create PreferHeaderParser.cs in Ignixa.Api/Http/
6. Update FhirEndpoints.HandleBundle with streaming path
7. Add BundleProcessor.ProcessStreamAsync method
8. Manual integration testing (verify memory usage)

### Week 5 (Days 17-19): Optimization
1. Refine error handling (fail-fast vs partial success)
2. Add validation for streaming requirements
3. Performance tuning (buffer sizes, batch sizes)
4. Run performance benchmarks
5. Document performance results

### Week 6 (Day 20): Documentation
1. Update ADR-2502 with Phase 1.1a section (this document incorporated)
2. Update ADR-2500 timeline and architectural decisions
3. Add code comments explaining patterns
4. Update investigation documents with "Implemented" status
5. Create this summary document (phase1.1a-implementation-summary.md)

### Week 7 (Start Phase 1.2)
1. Proceed with Search Implementation (original Phase 1.2 plan)
2. InMemory search from microsoft/fhir-server
3. Capability statement generation
4. Authorization middleware

---

## References

### Investigation Documents
- `docs/investigations/bundle-deferred-writes.md` - Two-phase channel architecture analysis
- `docs/investigations/bundle-streaming-parser.md` - Utf8JsonReader streaming parser design
- `docs/investigations/bundle-processing-with-channels.md` - Original Phase 1.1 investigation

### ADR Documents
- `docs/adr/adr-2502-phase1.1-bundle-processing.md` - Updated with Phase 1.1a section
- `docs/adr/adr-2500-master-implementation-roadmap.md` - Updated timeline and architectural decisions
- `docs/adr/adr-2501-prototype-phase.md` - Prototype Phase (predecessor)

### External References
- [TaskCompletionSource<T>](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1)
- [System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- [Utf8JsonReader](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader)
- [ArrayPool<T>](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1)
- [Microsoft FHIR Server ImportBundleParser](https://github.com/microsoft/fhir-server/blob/main/src/Microsoft.Health.Fhir.Core/Features/Operations/Import/ImportBundleParser.cs)

---

## Appendix: Code Snippets

### Example 1: DeferredWriteCoordinator Usage

```csharp
// In BundleProcessor.ProcessAsync
var writeCoordinator = new DeferredWriteCoordinator(
    capacity: 100,
    _repository);

// Start batch processor in background
var batchProcessorTask = Task.Run(async () =>
{
    while (await writeCoordinator._writeChannel.Reader.WaitToReadAsync(ct))
    {
        var errors = await writeCoordinator.ProcessBatchAsync(
            batchSize: 50,
            ct);
    }
}, ct);

// Handlers queue writes
var resultKey = await writeCoordinator.QueueWriteAsync(
    resourceType: "Patient",
    resourceId: "123",
    resource: sourceNode,
    rawJson: json,
    ct);

// Wait for all writes to complete
writeCoordinator.CompleteWrites();
await batchProcessorTask;
```

### Example 2: StreamingBundleParser Usage

```csharp
// In FhirEndpoints.HandleBundle
bool useStreaming = PreferHeaderParser.IsStreamingPreferred(httpContext.Request);

if (useStreaming)
{
    logger.LogInformation("Using streaming bundle parser");

    var entryStream = _streamingParser.ParseStreamAsync(
        httpContext.Request.Body,
        ct);

    var responseBundle = await _bundleProcessor.ProcessStreamAsync(
        entryStream,
        options,
        ct);
}
else
{
    // Standard buffered path
    var responseBundle = await _bundleProcessor.ProcessAsync(
        bundle,
        options,
        ct);
}
```

### Example 3: BundleParserState State Machine

```csharp
private void ProcessToken(ref Utf8JsonReader reader, BundleParserState state)
{
    switch (reader.TokenType)
    {
        case JsonTokenType.PropertyName:
            state.CurrentProperty = reader.GetString();
            break;

        case JsonTokenType.StartObject:
            state.Depth++;
            if (state.IsInEntryArray && state.Depth == 2)
            {
                state.StartNewEntry();
            }
            break;

        case JsonTokenType.EndObject:
            state.Depth--;
            if (state.IsInEntryArray && state.Depth == 1)
            {
                state.CompleteEntry();
            }
            break;

        // ... other token types
    }
}
```

---

**Document Status**: Complete
**Last Updated**: October 9, 2025
**Authors**: ADR Implementation Team
**Approvals**: Phase 1.1a Enhancement Decision (Accepted)
