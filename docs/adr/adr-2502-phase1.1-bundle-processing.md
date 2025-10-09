# ADR 2502: Phase 1.1 - Bundle Processing with Channels

## Status

Proposed

## Context

Following the Prototype Phase, we add Bundle transaction support to enable atomic multi-resource operations. This phase adopts the ASP.NET Core pipeline pattern from `bundle-processing-with-channels.md` investigation.

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

## Implementation Plan (Week 2)

### Deliverables

✅ **Bundle endpoint** - `POST /` with transaction bundle
✅ **Channel-based parallel execution** - 10 concurrent operations
✅ **Reference resolution** - `urn:uuid:` → actual resource IDs
✅ **Verb ordering** - POST → PUT → DELETE
✅ **80% test coverage**

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

### Negative

1. **Mini HttpContext overhead**: Slight performance cost vs direct calls

## References

- Investigation: `bundle-processing-with-channels.md`
- Previous: ADR-2501 (Prototype Phase)
- Next: ADR-2503 (Phase 1.2 - Search Implementation)
