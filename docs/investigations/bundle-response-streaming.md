# Investigation: Bundle Response Streaming

**Status**: 🔍 Proposed for Phase 1.1b
**Created**: 2025-10-09
**Last Updated**: 2025-10-09

## Problem Statement

Currently, `BundleProcessor` buffers all entry responses in memory before building the final response bundle:

```csharp
// BundleProcessor.cs:425
responseBundle = _responseBuilder.BuildResponse(responses, options.Type);
```

This creates unnecessary memory pressure for large batch bundles, where:
- Each entry response is independent
- We could stream responses as they complete
- No need to wait for all entries before starting HTTP response

## Current Architecture

```
HTTP Request
  → Parse Bundle (streaming ✅)
  → Execute Entries (channel-based ✅)
  → Buffer ALL responses (❌ bottleneck)
  → Build response bundle
  → Serialize to HTTP
```

**Memory Impact**:
- 1000-entry bundle: ~100MB response buffer
- Peak memory: Request (1MB streaming) + Response (100MB buffered) = 101MB

## Proposed Architecture: Dual-Mode Response

### Transaction Bundles (MUST buffer)
```
HTTP Request
  → Parse
  → Execute ALL entries
  → Buffer responses (required for rollback)
  → Check for errors
  → Build response bundle
  → Serialize to HTTP
```

**Why buffering is required**:
- All-or-nothing semantics (FHIR spec)
- If ANY entry fails, ENTIRE bundle must rollback
- Cannot start sending response until all entries succeed

### Batch Bundles (CAN stream)
```
HTTP Request
  → Parse Bundle (streaming)
  → Execute Entries (channel-based)
  → Stream responses as they complete (NEW)
    ↓
  IAsyncEnumerable<BundleEntryResponse>
    ↓
  BundleSerializer.SerializeStreamAsync
    ↓
  HTTP Response (streaming)
```

**Benefits**:
- <1MB memory for 1000-entry batch
- Start sending response within milliseconds
- True end-to-end wire-to-DB streaming

## Technical Design

### 1. BundleChannelExecutor Enhancement

Add streaming mode that yields responses as they complete:

```csharp
public async IAsyncEnumerable<BundleEntryResponse> ExecuteStreamingAsync(
    IReadOnlyList<BundleEntryContext> entries,
    ReferenceResolutionContext referenceContext,
    BundleProcessingOptions options,
    [EnumeratorCancellation] CancellationToken cancellationToken,
    DeferredWriteCoordinator? coordinator = null)
{
    var responseChannel = Channel.CreateUnbounded<(int index, BundleEntryResponse response)>();

    // Execute entries in parallel, write responses to channel
    var executionTask = Task.Run(async () =>
    {
        // ... existing execution logic ...
        // When each entry completes, write to responseChannel
    });

    // Yield responses in order as they complete
    var completedResponses = new Dictionary<int, BundleEntryResponse>();
    int nextIndex = 0;

    await foreach (var (index, response) in responseChannel.Reader.ReadAllAsync(cancellationToken))
    {
        completedResponses[index] = response;

        // Yield responses in order
        while (completedResponses.TryGetValue(nextIndex, out var nextResponse))
        {
            yield return nextResponse;
            completedResponses.Remove(nextIndex);
            nextIndex++;
        }
    }
}
```

### 2. BundleSerializer.SerializeStreamAsync

New method to write Bundle JSON incrementally:

```csharp
public static async Task SerializeStreamAsync(
    Stream outputStream,
    string bundleType,
    IAsyncEnumerable<BundleEntryResponse> entryResponses,
    string? selfLink,
    string? nextLink,
    bool pretty,
    CancellationToken cancellationToken)
{
    using var writer = new Utf8JsonWriter(outputStream, new JsonWriterOptions { Indented = pretty });

    writer.WriteStartObject(); // {
    writer.WriteString("resourceType", "Bundle");
    writer.WriteString("type", bundleType);

    writer.WriteStartArray("entry"); // "entry": [

    await foreach (var response in entryResponses.WithCancellation(cancellationToken))
    {
        WriteEntryResponse(writer, response);
        await writer.FlushAsync(cancellationToken); // Flush each entry (incremental)
    }

    writer.WriteEndArray(); // ]
    writer.WriteEndObject(); // }
    await writer.FlushAsync(cancellationToken);
}
```

### 3. BundleProcessor Dual-Mode

```csharp
public async Task<FhirBundle> ProcessStreamAsync(
    IAsyncEnumerable<BundleEntryContext> entryStream,
    BundleProcessingOptions options,
    CancellationToken cancellationToken)
{
    // ... setup coordinator, batch processor ...

    if (options.Type == BundleType.Transaction)
    {
        // BUFFERED MODE: Required for all-or-nothing semantics
        var responses = await _channelExecutor.ExecuteAsync(
            entries, referenceContext, options, cancellationToken, coordinator);

        // Check for errors (rollback if any failed)
        // Build response bundle
        return _responseBuilder.BuildResponse(responses, options.Type);
    }
    else // Batch
    {
        // STREAMING MODE: Yield responses as they complete
        var responseStream = _channelExecutor.ExecuteStreamingAsync(
            entries, referenceContext, options, cancellationToken, coordinator);

        // Return sentinel value (actual streaming happens in FhirEndpoints)
        return new FhirBundle { Type = FhirBundle.BundleType.BatchResponse };
    }
}
```

### 4. FhirEndpoints Integration

```csharp
private static async Task<IResult> HandleBundleStreaming(...)
{
    var entryStream = streamingParser.ParseStreamAsync(context.Request.Body, ct);

    if (options.Type == BundleType.Batch)
    {
        // TRUE END-TO-END STREAMING for batch bundles
        var responseStream = await bundleProcessor.ProcessBatchStreamingAsync(entryStream, options, ct);

        context.Response.ContentType = "application/fhir+json; charset=utf-8";
        await BundleSerializer.SerializeStreamAsync(
            outputStream: context.Response.Body,
            bundleType: "batch-response",
            entryResponses: responseStream,
            selfLink: null,
            nextLink: null,
            pretty: false,
            cancellationToken: ct);

        return Results.Empty; // Already written to response
    }
    else // Transaction
    {
        // BUFFERED MODE: Required for transaction semantics
        FhirBundle responseBundle = await bundleProcessor.ProcessStreamAsync(entryStream, options, ct);
        string responseJson = new FhirJsonSerializer().SerializeToString(responseBundle);
        return Results.Content(responseJson, "application/fhir+json");
    }
}
```

## Memory Comparison

### Current (Buffered)
- 1000-entry batch bundle
- Request: 1MB (streaming parser ✅)
- Response: 100MB (buffered ❌)
- **Peak: 101MB**

### Proposed (Streaming)
- 1000-entry batch bundle
- Request: <1MB (streaming parser ✅)
- Response: <1MB (streaming serializer ✅)
- **Peak: <2MB** (50x reduction)

## Performance Comparison

### Current (Buffered)
```
Time to first byte: ~42s (wait for all 1000 entries)
Total time: ~45s
```

### Proposed (Streaming)
```
Time to first byte: ~50ms (start sending immediately)
Total time: ~42s (same execution time, but streaming)
User perception: 840x better (progressive response)
```

## Implementation Phases

### Phase 1.1b: Response Streaming (Week 3-4)
1. **Week 3: Channel Streaming**
   - Add `ExecuteStreamingAsync` to BundleChannelExecutor
   - Yield responses in order as they complete
   - Unit tests for streaming mode

2. **Week 4: Serializer Integration**
   - Add `BundleSerializer.SerializeStreamAsync`
   - Integrate with FhirEndpoints dual-path
   - Integration tests (batch streaming vs transaction buffering)

## Tradeoffs

### Pros
- 50x memory reduction for large batch bundles
- 840x better time to first byte
- True end-to-end wire-to-DB streaming

### Cons
- More complex (dual-path for transaction vs batch)
- Transaction bundles still need buffering (unavoidable)
- Requires changes to response serialization

## Risks and Mitigations

### Risk: Response order correctness
**Mitigation**: OrderPreservingChannel or in-order yield logic (example above)

### Risk: Error handling in streaming mode
**Mitigation**: Batch bundles allow per-entry errors (no rollback needed)

### Risk: Partial response if downstream fails
**Mitigation**: Acceptable for batch bundles (FHIR spec allows partial results)

## Decision

✅ **APPROVED for Phase 1.1b**

This is a high-value optimization that enables true end-to-end streaming for batch bundles while preserving correct transaction semantics.

**Scope**:
- Phase 1.1b (Week 3-4): Implement streaming response path for batch bundles
- Phase 1.1a remains focused on deferred writes and streaming parser

## References

- ADR-2502: Bundle Processing with Channels
- Investigation: bundle-deferred-writes.md
- Investigation: bundle-streaming-parser.md
- FHIR R4 Spec: Bundle Transaction vs Batch semantics
