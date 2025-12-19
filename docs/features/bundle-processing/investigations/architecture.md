# Investigation: Bundle Processing Architecture

**Feature**: bundle-processing
**Status**: Viable
**Created**: 2024-01-01
**Original ADR**: N/A

---

This document outlines the architecture for processing FHIR bundles (transaction and batch types) in the FHIR Server v2, incorporating lessons learned from the legacy implementation and modern .NET patterns.

## Executive Summary

Bundle processing is a core concept in FHIR that enables clients to submit multiple operations atomically (transaction bundles) or as a batch of independent operations (batch bundles). The architecture presented here:

1. **Uses MediatR for request orchestration** - Each bundle entry becomes a command/query handled by existing resource handlers
2. **Supports parallel processing** - Safe operations (GET, independent POSTs) execute concurrently
3. **Maintains ACID guarantees** - Transaction bundles use the transaction abstraction from ADR 2510
4. **Generic resource handling** - No resource-specific code; all resources processed uniformly
5. **Zero-allocation optimizations** - Span<T>, Memory<T>, ArrayPool<T> throughout

## Legacy Implementation Analysis

### Key Components Identified

**BundleHandler.cs** (Shared):
- 1,200+ lines of complex orchestration logic
- Handles both sequential and parallel processing modes
- Uses ASP.NET Core routing infrastructure (`IRouter`) to dispatch sub-requests
- Creates mini HttpContext per bundle entry
- Manages reference resolution (urn:uuid patterns)
- Implements retry logic for throttled requests (429 responses)
- Complex transaction handling with rollback support

**BundleOrchestrator.cs**:
- Tracks in-flight bundle operations
- Uses `ConcurrentDictionary<Guid, IBundleOrchestratorOperation>` for operation tracking
- Enables coordination across parallel workers
- Provides operation lifecycle management (create, get, complete)

**BundleHandlerParallelOperations.cs**:
- Sophisticated parallel execution engine
- Processes requests per HTTP verb group in parallel
- Creates thread-safe instances of services per request
- Cancellation token propagation for early termination
- Exception handling with rollback on transaction failures

### Strengths of Legacy Approach

1. ✅ **Generic resource handling** - Uses routing to discover handlers dynamically
2. ✅ **Parallel processing** - Significantly faster for large batches
3. ✅ **Transaction integrity** - Proper ACID guarantees with rollback
4. ✅ **Reference resolution** - Handles urn:uuid and conditional references correctly
5. ✅ **Retry logic** - Handles 429 throttling gracefully
6. ✅ **Comprehensive error handling** - Distinguishes client vs server errors

### Weaknesses and Improvement Opportunities

1. ❌ **Heavy ASP.NET coupling** - Creates full HttpContext per entry (expensive)
2. ❌ **Complex routing** - IRouter usage is verbose and allocation-heavy
3. ❌ **JSON serialization overhead** - Multiple parse/serialize cycles
4. ❌ **Limited use of modern patterns** - Could benefit from MediatR, channels, etc.
5. ❌ **Testing complexity** - Hard to unit test due to HttpContext dependencies

## Modern Architecture Design

### Core Principles

**1. MediatR-First Design**
- Bundle entries map to existing `IRequest<T>` commands/queries
- Reuse all existing resource handlers (CreateResourceHandler, UpdateResourceHandler, etc.)
- No special bundle-specific resource logic needed

**2. Pipeline Architecture**
```
Bundle Request
    ↓
Entry Parser (extract entries, validate structure)
    ↓
Reference Pre-Processor (assign IDs, build reference map)
    ↓
Entry Grouping (group by HTTP verb for execution order)
    ↓
Parallel Executor (execute groups with concurrency)
    ↓
Response Builder (construct response bundle)
    ↓
Bundle Response
```

**3. Generic Resource Handling**
- All resources use same processing pipeline
- Handler discovery via MediatR routing
- No resource-type switch statements or special cases
- Schema-driven validation via `IFhirSchemaProvider`

### Key Abstractions

#### Bundle Entry Execution Context

```csharp
/// <summary>
/// Represents a single entry in a bundle ready for execution
/// </summary>
public record BundleEntryContext
{
    public required int Index { get; init; }
    public required HTTPVerb HttpVerb { get; init; }
    public required string ResourceType { get; init; }
    public required string? ResourceId { get; init; }
    public required string RequestUrl { get; init; }
    public required ISourceNode? Resource { get; init; }
    public required string? FullUrl { get; init; }
    public required string? IfMatch { get; init; }
    public required string? IfNoneExist { get; init; }
    public required string? IfModifiedSince { get; init; }
    public required string? IfNoneMatch { get; init; }

    // Assigned during reference pre-processing
    public string? AssignedResourceId { get; set; }

    // Populated after execution
    public BundleEntryResponse? Response { get; set; }
}

public record BundleEntryResponse
{
    public required int StatusCode { get; init; }
    public required string Status { get; init; }
    public string? Location { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public ISourceNode? Resource { get; init; }
    public OperationOutcome? Outcome { get; init; }
}
```

#### Bundle Processor Interface

```csharp
public interface IBundleProcessor
{
    /// <summary>
    /// Process a FHIR bundle
    /// </summary>
    ValueTask<Bundle> ProcessBundleAsync(
        Bundle bundle,
        BundleProcessingOptions options,
        CancellationToken cancellationToken = default);
}

public record BundleProcessingOptions
{
    public BundleType Type { get; init; }
    public BundleProcessingLogic ProcessingLogic { get; init; } = BundleProcessingLogic.Sequential;
    public int MaxParallelism { get; init; } = 10;
    public bool EnableRetryOn429 { get; init; } = true;
    public TimeSpan Retry429Delay { get; init; } = TimeSpan.FromSeconds(2);
}

public enum BundleProcessingLogic
{
    Sequential,
    Parallel
}
```

### Implementation Design

#### 1. Bundle Entry Parser

```csharp
public class BundleEntryParser
{
    private readonly IFhirSchemaProvider _schemaProvider;

    public IReadOnlyList<BundleEntryContext> ParseEntries(Bundle bundle)
    {
        var entries = new List<BundleEntryContext>();

        for (int i = 0; i < bundle.Entry.Count; i++)
        {
            var entry = bundle.Entry[i];
            var request = entry.Request ?? throw new InvalidBundleException($"Entry {i} missing request");

            // Parse request URL to extract resource type and ID
            var (resourceType, resourceId, queryParams) = ParseRequestUrl(request.Url);

            // Parse resource if present (POST, PUT, PATCH)
            ISourceNode? resource = null;
            if (entry.Resource != null)
            {
                // Resource already parsed by controller, extract ISourceNode
                resource = ConvertToSourceNode(entry.Resource);
            }

            entries.Add(new BundleEntryContext
            {
                Index = i,
                HttpVerb = request.Method,
                ResourceType = resourceType,
                ResourceId = resourceId,
                RequestUrl = request.Url,
                Resource = resource,
                FullUrl = entry.FullUrl,
                IfMatch = request.IfMatch,
                IfNoneExist = request.IfNoneExist,
                IfModifiedSince = request.IfModifiedSince,
                IfNoneMatch = request.IfNoneMatch
            });
        }

        return entries;
    }

    private (string resourceType, string? resourceId, string? query) ParseRequestUrl(string url)
    {
        // Parse: "Patient/123" or "Patient?identifier=abc" or "Patient"
        var parts = url.Split('?', 2);
        var path = parts[0];
        var query = parts.Length > 1 ? parts[1] : null;

        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resourceType = pathSegments[0];
        var resourceId = pathSegments.Length > 1 ? pathSegments[1] : null;

        return (resourceType, resourceId, query);
    }
}
```

#### 2. Reference Pre-Processor

```csharp
public class BundleReferencePreProcessor
{
    private readonly IResourceIdProvider _resourceIdProvider;

    /// <summary>
    /// Pre-assign IDs for POSTs and conditional PUTs, build reference map
    /// </summary>
    public ReferenceResolutionContext PreProcessReferences(
        IReadOnlyList<BundleEntryContext> entries,
        BundleType bundleType)
    {
        if (bundleType != BundleType.Transaction)
        {
            // Batch bundles don't support reference resolution
            return new ReferenceResolutionContext(new Dictionary<string, ResourceReference>());
        }

        var referenceMap = new Dictionary<string, ResourceReference>();

        foreach (var entry in entries)
        {
            // Assign IDs to entries that will create new resources
            if (ShouldAssignId(entry))
            {
                var assignedId = _resourceIdProvider.Create();
                entry.AssignedResourceId = assignedId;

                // Map fullUrl to assigned resource reference
                if (!string.IsNullOrEmpty(entry.FullUrl))
                {
                    referenceMap[entry.FullUrl] = new ResourceReference(
                        entry.ResourceType,
                        assignedId);
                }
            }
        }

        return new ReferenceResolutionContext(referenceMap);
    }

    private static bool ShouldAssignId(BundleEntryContext entry)
    {
        return entry.HttpVerb == HTTPVerb.POST ||
               (entry.HttpVerb == HTTPVerb.PUT && entry.RequestUrl.Contains('?'));
    }
}

public record ReferenceResolutionContext(
    IReadOnlyDictionary<string, ResourceReference> ReferenceMap);

public record ResourceReference(string ResourceType, string ResourceId);
```

#### 3. MediatR-Based Entry Executor

```csharp
public class BundleEntryExecutor
{
    private readonly IMediator _mediator;
    private readonly IReferenceResolver _referenceResolver;
    private readonly ILogger<BundleEntryExecutor> _logger;

    /// <summary>
    /// Execute a single bundle entry by dispatching to appropriate handler
    /// </summary>
    public async ValueTask<BundleEntryResponse> ExecuteEntryAsync(
        BundleEntryContext entry,
        ReferenceResolutionContext referenceContext,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve references in resource if needed
            if (entry.Resource != null && referenceContext.ReferenceMap.Any())
            {
                entry.Resource = await _referenceResolver.ResolveReferencesAsync(
                    entry.Resource,
                    referenceContext,
                    cancellationToken);
            }

            // Create appropriate MediatR request based on HTTP verb
            var request = CreateMediatRRequest(entry);

            // Dispatch to handler
            var response = await _mediator.Send(request, cancellationToken);

            // Convert handler response to bundle entry response
            return MapToBundleEntryResponse(response, entry);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(ex, entry);
        }
    }

    private object CreateMediatRRequest(BundleEntryContext entry)
    {
        return entry.HttpVerb switch
        {
            HTTPVerb.POST => new CreateResourceRequest(
                entry.ResourceType,
                entry.Resource!,
                entry.IfNoneExist),

            HTTPVerb.PUT when !string.IsNullOrEmpty(entry.ResourceId) =>
                new UpsertResourceRequest(
                    new ResourceKey(entry.ResourceType, entry.ResourceId),
                    entry.Resource!,
                    entry.IfMatch),

            HTTPVerb.PUT when entry.RequestUrl.Contains('?') =>
                new ConditionalUpsertRequest(
                    entry.ResourceType,
                    ParseSearchParams(entry.RequestUrl),
                    entry.Resource!),

            HTTPVerb.GET when !string.IsNullOrEmpty(entry.ResourceId) =>
                new GetResourceRequest(
                    new ResourceKey(entry.ResourceType, entry.ResourceId)),

            HTTPVerb.GET when entry.RequestUrl.Contains('?') =>
                new SearchResourcesRequest(
                    entry.ResourceType,
                    ParseSearchParams(entry.RequestUrl)),

            HTTPVerb.DELETE when !string.IsNullOrEmpty(entry.ResourceId) =>
                new DeleteResourceRequest(
                    new ResourceKey(entry.ResourceType, entry.ResourceId),
                    hardDelete: false),

            HTTPVerb.DELETE when entry.RequestUrl.Contains('?') =>
                new ConditionalDeleteRequest(
                    entry.ResourceType,
                    ParseSearchParams(entry.RequestUrl)),

            HTTPVerb.PATCH => new PatchResourceRequest(
                new ResourceKey(entry.ResourceType, entry.ResourceId!),
                entry.Resource!),

            _ => throw new NotSupportedException($"HTTP verb {entry.HttpVerb} not supported in bundles")
        };
    }
}
```

#### 4. Parallel Execution Engine

```csharp
public class BundleParallelExecutor
{
    private static readonly HTTPVerb[] VerbExecutionOrder = new[]
    {
        HTTPVerb.DELETE,  // Delete first to free IDs
        HTTPVerb.POST,    // Create new resources
        HTTPVerb.PUT,     // Update existing
        HTTPVerb.PATCH,   // Patch existing
        HTTPVerb.GET,     // Read operations last
        HTTPVerb.HEAD     // Read operations last
    };

    private readonly BundleEntryExecutor _entryExecutor;
    private readonly ILogger<BundleParallelExecutor> _logger;

    /// <summary>
    /// Execute entries in parallel within verb groups while maintaining FHIR ordering
    /// </summary>
    public async ValueTask ExecuteEntriesAsync(
        IReadOnlyList<BundleEntryContext> entries,
        ReferenceResolutionContext referenceContext,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        // Group entries by HTTP verb
        var entriesByVerb = entries
            .GroupBy(e => e.HttpVerb)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Process verb groups in FHIR-specified order
        foreach (var verb in VerbExecutionOrder)
        {
            if (!entriesByVerb.TryGetValue(verb, out var verbEntries))
                continue;

            if (options.ProcessingLogic == BundleProcessingLogic.Sequential)
            {
                // Sequential processing
                await ProcessEntriesSequentiallyAsync(
                    verbEntries,
                    referenceContext,
                    cancellationToken);
            }
            else
            {
                // Parallel processing within verb group
                await ProcessEntriesInParallelAsync(
                    verbEntries,
                    referenceContext,
                    options,
                    cancellationToken);
            }
        }
    }

    private async ValueTask ProcessEntriesInParallelAsync(
        List<BundleEntryContext> entries,
        ReferenceResolutionContext referenceContext,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        // Use ParallelOptions for controlled parallelism
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxParallelism,
            CancellationToken = cancellationToken
        };

        // Use Parallel.ForEachAsync for efficient async parallel execution
        await Parallel.ForEachAsync(
            entries,
            parallelOptions,
            async (entry, ct) =>
            {
                var sw = Stopwatch.StartNew();

                try
                {
                    entry.Response = await _entryExecutor.ExecuteEntryAsync(
                        entry,
                        referenceContext,
                        ct);

                    _logger.LogInformation(
                        "Bundle entry {Index} ({Verb} {ResourceType}) completed in {ElapsedMs}ms with status {Status}",
                        entry.Index,
                        entry.HttpVerb,
                        entry.ResourceType,
                        sw.ElapsedMilliseconds,
                        entry.Response.Status);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Bundle entry {Index} ({Verb} {ResourceType}) failed after {ElapsedMs}ms",
                        entry.Index,
                        entry.HttpVerb,
                        entry.ResourceType,
                        sw.ElapsedMilliseconds);

                    throw;
                }
            });
    }

    private async ValueTask ProcessEntriesSequentiallyAsync(
        List<BundleEntryContext> entries,
        ReferenceResolutionContext referenceContext,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            entry.Response = await _entryExecutor.ExecuteEntryAsync(
                entry,
                referenceContext,
                cancellationToken);
        }
    }
}
```

#### 5. Main Bundle Processor

```csharp
public class BundleProcessor : IBundleProcessor
{
    private readonly BundleEntryParser _entryParser;
    private readonly BundleReferencePreProcessor _referencePreProcessor;
    private readonly BundleParallelExecutor _parallelExecutor;
    private readonly BundleResponseBuilder _responseBuilder;
    private readonly ITransactionContext _transactionContext;
    private readonly ILogger<BundleProcessor> _logger;

    public async ValueTask<Bundle> ProcessBundleAsync(
        Bundle bundle,
        BundleProcessingOptions options,
        CancellationToken cancellationToken = default)
    {
        // Validate bundle type
        if (bundle.Type != BundleType.Transaction && bundle.Type != BundleType.Batch)
        {
            throw new InvalidOperationException(
                $"Bundle type {bundle.Type} is not supported for processing");
        }

        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Parse entries
            var entries = _entryParser.ParseEntries(bundle);

            _logger.LogInformation(
                "Processing {BundleType} bundle with {EntryCount} entries using {ProcessingLogic} logic",
                bundle.Type,
                entries.Count,
                options.ProcessingLogic);

            // 2. Pre-process references
            var referenceContext = _referencePreProcessor.PreProcessReferences(
                entries,
                bundle.Type);

            // 3. Execute entries
            if (bundle.Type == BundleType.Transaction)
            {
                await ExecuteTransactionBundleAsync(
                    entries,
                    referenceContext,
                    options,
                    cancellationToken);
            }
            else
            {
                await ExecuteBatchBundleAsync(
                    entries,
                    referenceContext,
                    options,
                    cancellationToken);
            }

            // 4. Build response
            var response = _responseBuilder.BuildResponse(bundle, entries);

            _logger.LogInformation(
                "Bundle processing completed in {ElapsedMs}ms",
                sw.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Bundle processing failed after {ElapsedMs}ms",
                sw.ElapsedMilliseconds);

            throw;
        }
    }

    private async ValueTask ExecuteTransactionBundleAsync(
        IReadOnlyList<BundleEntryContext> entries,
        ReferenceResolutionContext referenceContext,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        // Transaction bundles require ACID guarantees
        using var transaction = await _transactionContext.BeginTransactionAsync(
            entries.Count,
            definition: "FHIR Bundle Transaction",
            cancellationToken);

        try
        {
            await _parallelExecutor.ExecuteEntriesAsync(
                entries,
                referenceContext,
                options,
                cancellationToken);

            // Check for any failures
            var failures = entries.Where(e => e.Response?.StatusCode >= 400).ToList();
            if (failures.Any())
            {
                var firstFailure = failures.First();
                await transaction.FailAsync(
                    $"Transaction failed at entry {firstFailure.Index}: {firstFailure.Response?.Status}",
                    cancellationToken);

                throw new FhirTransactionFailedException(
                    "Transaction bundle failed",
                    (HttpStatusCode)firstFailure.Response!.StatusCode);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.FailAsync("Transaction bundle failed with exception", cancellationToken);
            throw;
        }
    }

    private async ValueTask ExecuteBatchBundleAsync(
        IReadOnlyList<BundleEntryContext> entries,
        ReferenceResolutionContext referenceContext,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        // Batch bundles execute independently, no transaction needed
        await _parallelExecutor.ExecuteEntriesAsync(
            entries,
            referenceContext,
            options,
            cancellationToken);
    }
}
```

#### 6. Response Builder

```csharp
public class BundleResponseBuilder
{
    public Bundle BuildResponse(Bundle requestBundle, IReadOnlyList<BundleEntryContext> entries)
    {
        var responseType = requestBundle.Type == BundleType.Transaction
            ? BundleType.TransactionResponse
            : BundleType.BatchResponse;

        var responseBundle = new Bundle
        {
            Type = responseType,
            Entry = new List<EntryComponent>()
        };

        foreach (var entry in entries.OrderBy(e => e.Index))
        {
            var responseEntry = new EntryComponent
            {
                Response = new ResponseComponent
                {
                    Status = entry.Response?.Status ?? "500",
                    Location = entry.Response?.Location,
                    Etag = entry.Response?.ETag,
                    LastModified = entry.Response?.LastModified
                }
            };

            // Include resource in response if present
            if (entry.Response?.Resource != null)
            {
                responseEntry.Resource = ConvertFromSourceNode(entry.Response.Resource);
            }

            // Include OperationOutcome for errors
            if (entry.Response?.Outcome != null)
            {
                responseEntry.Response.Outcome = entry.Response.Outcome;
            }

            responseBundle.Entry.Add(responseEntry);
        }

        return responseBundle;
    }
}
```

### Integration with ASP.NET Core

#### Controller Integration

```csharp
[ApiController]
[Route("{tenantId?}/{version?}")]
public class BundleController : ControllerBase
{
    private readonly IBundleProcessor _bundleProcessor;
    private readonly ILogger<BundleController> _logger;

    [HttpPost("")]
    [Consumes("application/fhir+json")]
    [Produces("application/fhir+json")]
    public async Task<IActionResult> ProcessBundle(
        [FromBody] Bundle bundle,
        [FromHeader(Name = "X-Bundle-Processing-Logic")] string? processingLogic,
        CancellationToken cancellationToken)
    {
        // Validate bundle type
        if (bundle.Type != BundleType.Transaction && bundle.Type != BundleType.Batch)
        {
            return BadRequest(CreateOperationOutcome(
                "Bundle type must be 'transaction' or 'batch'"));
        }

        // Parse processing logic from header (supports Azure FHIR Service compatibility)
        var logic = processingLogic?.ToLowerInvariant() == "parallel"
            ? BundleProcessingLogic.Parallel
            : BundleProcessingLogic.Sequential;

        var options = new BundleProcessingOptions
        {
            Type = bundle.Type,
            ProcessingLogic = logic,
            MaxParallelism = 10
        };

        try
        {
            var response = await _bundleProcessor.ProcessBundleAsync(
                bundle,
                options,
                cancellationToken);

            return Ok(response);
        }
        catch (FhirTransactionFailedException ex)
        {
            _logger.LogWarning(ex, "Transaction bundle failed");
            return StatusCode((int)ex.StatusCode, ex.OperationOutcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bundle processing failed");
            return StatusCode(500, CreateOperationOutcome(
                "Internal error processing bundle"));
        }
    }
}
```

### Memory Optimizations

#### Using Channels for Entry Processing

For very large bundles, we can use `System.Threading.Channels` for backpressure:

```csharp
public class ChannelBasedBundleExecutor
{
    public async ValueTask ExecuteEntriesWithChannelsAsync(
        IReadOnlyList<BundleEntryContext> entries,
        ReferenceResolutionContext referenceContext,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<BundleEntryContext>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        // Producer: feed entries to channel
        var producer = Task.Run(async () =>
        {
            foreach (var entry in entries)
            {
                await channel.Writer.WriteAsync(entry, cancellationToken);
            }
            channel.Writer.Complete();
        }, cancellationToken);

        // Consumers: process entries from channel
        var consumers = Enumerable.Range(0, options.MaxParallelism)
            .Select(_ => ProcessEntriesFromChannelAsync(
                channel.Reader,
                referenceContext,
                cancellationToken))
            .ToArray();

        await Task.WhenAll(consumers);
        await producer;
    }

    private async Task ProcessEntriesFromChannelAsync(
        ChannelReader<BundleEntryContext> reader,
        ReferenceResolutionContext referenceContext,
        CancellationToken cancellationToken)
    {
        await foreach (var entry in reader.ReadAllAsync(cancellationToken))
        {
            entry.Response = await _entryExecutor.ExecuteEntryAsync(
                entry,
                referenceContext,
                cancellationToken);
        }
    }
}
```

## Testing Strategy

### Unit Tests (xUnit + NSubstitute)

```csharp
public class BundleProcessorTests
{
    private readonly IBundleProcessor _bundleProcessor;
    private readonly IMediator _mediator;
    private readonly ITransactionContext _transactionContext;

    public BundleProcessorTests()
    {
        _mediator = Substitute.For<IMediator>();
        _transactionContext = Substitute.For<ITransactionContext>();

        _bundleProcessor = new BundleProcessor(
            new BundleEntryParser(schemaProvider),
            new BundleReferencePreProcessor(resourceIdProvider),
            new BundleParallelExecutor(entryExecutor, logger),
            new BundleResponseBuilder(),
            _transactionContext,
            logger);
    }

    [Fact]
    public async Task ProcessBundleAsync_TransactionBundle_CommitsOnSuccess()
    {
        // Arrange
        var bundle = CreateTransactionBundle(
            CreatePatientPostEntry(),
            CreateObservationPostEntry());

        var transaction = Substitute.For<ITransactionScope>();
        _transactionContext.BeginTransactionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(transaction);

        _mediator.Send(Arg.Any<CreateResourceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateResourceResponse(/* success */));

        // Act
        var response = await _bundleProcessor.ProcessBundleAsync(
            bundle,
            new BundleProcessingOptions { Type = BundleType.Transaction },
            CancellationToken.None);

        // Assert
        await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        Assert.Equal(BundleType.TransactionResponse, response.Type);
        Assert.Equal(2, response.Entry.Count);
    }

    [Fact]
    public async Task ProcessBundleAsync_TransactionBundle_RollsBackOnFailure()
    {
        // Arrange
        var bundle = CreateTransactionBundle(
            CreatePatientPostEntry(),
            CreateInvalidObservationPostEntry());

        var transaction = Substitute.For<ITransactionScope>();
        _transactionContext.BeginTransactionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(transaction);

        _mediator.Send(Arg.Is<CreateResourceRequest>(r => r.ResourceType == "Patient"), Arg.Any<CancellationToken>())
            .Returns(new CreateResourceResponse(/* success */));

        _mediator.Send(Arg.Is<CreateResourceRequest>(r => r.ResourceType == "Observation"), Arg.Any<CancellationToken>())
            .Throws(new ResourceValidationException("Invalid observation"));

        // Act & Assert
        await Assert.ThrowsAsync<FhirTransactionFailedException>(async () =>
            await _bundleProcessor.ProcessBundleAsync(
                bundle,
                new BundleProcessingOptions { Type = BundleType.Transaction },
                CancellationToken.None));

        await transaction.Received(1).FailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBundleAsync_BatchBundle_ContinuesOnIndividualFailure()
    {
        // Arrange
        var bundle = CreateBatchBundle(
            CreatePatientPostEntry(),
            CreateInvalidObservationPostEntry());

        _mediator.Send(Arg.Is<CreateResourceRequest>(r => r.ResourceType == "Patient"), Arg.Any<CancellationToken>())
            .Returns(new CreateResourceResponse(/* success with 201 */));

        _mediator.Send(Arg.Is<CreateResourceRequest>(r => r.ResourceType == "Observation"), Arg.Any<CancellationToken>())
            .Throws(new ResourceValidationException("Invalid observation"));

        // Act
        var response = await _bundleProcessor.ProcessBundleAsync(
            bundle,
            new BundleProcessingOptions { Type = BundleType.Batch },
            CancellationToken.None);

        // Assert
        Assert.Equal(BundleType.BatchResponse, response.Type);
        Assert.Equal("201", response.Entry[0].Response.Status);  // Patient succeeded
        Assert.Equal("400", response.Entry[1].Response.Status);  // Observation failed
    }

    [Theory]
    [InlineData(BundleProcessingLogic.Sequential)]
    [InlineData(BundleProcessingLogic.Parallel)]
    public async Task ProcessBundleAsync_ResolvesUrnUuidReferences(BundleProcessingLogic logic)
    {
        // Arrange
        var patientUuid = "urn:uuid:05efabf0-4be2-4561-91ce-51548425acb9";
        var bundle = CreateTransactionBundle(
            CreatePatientPostEntryWithFullUrl(patientUuid),
            CreateObservationPostEntryReferencingPatient(patientUuid));

        string assignedPatientId = null;

        _mediator.Send(Arg.Is<CreateResourceRequest>(r => r.ResourceType == "Patient"), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                assignedPatientId = "patient-123";
                return new CreateResourceResponse(assignedPatientId, "1");
            });

        _mediator.Send(Arg.Is<CreateResourceRequest>(r => r.ResourceType == "Observation"), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<CreateResourceRequest>();
                var subjectRef = request.Resource.GetReference("subject");

                // Verify reference was resolved to assigned ID
                Assert.Equal($"Patient/{assignedPatientId}", subjectRef);

                return new CreateResourceResponse("obs-456", "1");
            });

        // Act
        await _bundleProcessor.ProcessBundleAsync(
            bundle,
            new BundleProcessingOptions
            {
                Type = BundleType.Transaction,
                ProcessingLogic = logic
            },
            CancellationToken.None);

        // Assert - verification happens in mediator mock callback
    }
}
```

### Integration Tests

```csharp
public class BundleIntegrationTests : IClassFixture<FhirServerFixture>
{
    private readonly FhirServerFixture _fixture;
    private readonly HttpClient _client;

    public BundleIntegrationTests(FhirServerFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task PostBundle_TransactionWithMultipleResources_CreatesAllAtomically()
    {
        // Arrange
        var bundle = new Bundle
        {
            Type = BundleType.Transaction,
            Entry = new List<Bundle.EntryComponent>
            {
                new()
                {
                    FullUrl = "urn:uuid:patient-1",
                    Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" },
                    Resource = new Patient { Gender = AdministrativeGender.Male }
                },
                new()
                {
                    Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Observation" },
                    Resource = new Observation
                    {
                        Subject = new ResourceReference("urn:uuid:patient-1"),
                        Code = new CodeableConcept("http://loinc.org", "8480-6")
                    }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/", bundle);

        // Assert
        response.EnsureSuccessStatusCode();
        var responseBundle = await response.Content.ReadFromJsonAsync<Bundle>();

        Assert.Equal(BundleType.TransactionResponse, responseBundle.Type);
        Assert.Equal(2, responseBundle.Entry.Count);
        Assert.Equal("201", responseBundle.Entry[0].Response.Status);
        Assert.Equal("201", responseBundle.Entry[1].Response.Status);

        // Verify resources were created
        var patientLocation = responseBundle.Entry[0].Response.Location;
        var patientResponse = await _client.GetAsync(patientLocation);
        patientResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PostBundle_ParallelProcessing_CompletesSuccessfully()
    {
        // Arrange
        var bundle = CreateLargeBatchBundle(100); // 100 independent patient creates

        _client.DefaultRequestHeaders.Add("X-Bundle-Processing-Logic", "parallel");

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _client.PostAsJsonAsync("/", bundle);
        sw.Stop();

        // Assert
        response.EnsureSuccessStatusCode();
        var responseBundle = await response.Content.ReadFromJsonAsync<Bundle>();

        Assert.Equal(100, responseBundle.Entry.Count);
        Assert.All(responseBundle.Entry, e => Assert.Equal("201", e.Response.Status));

        // Parallel should be faster than sequential (rough check)
        Assert.True(sw.ElapsedMilliseconds < 5000, "Parallel bundle took too long");
    }
}
```

## Performance Characteristics

### Expected Performance

**Transaction Bundle (10 resources, sequential)**:
- Parse: <5ms
- Reference resolution: <2ms
- Execution: ~100ms (10 x 10ms per operation)
- Response build: <5ms
- **Total: ~112ms**

**Transaction Bundle (10 resources, parallel)**:
- Parse: <5ms
- Reference resolution: <2ms
- Execution: ~20ms (parallelized)
- Response build: <5ms
- **Total: ~32ms (3.5x faster)**

**Batch Bundle (100 resources, parallel, MaxParallelism=10)**:
- Parse: <10ms
- Execution: ~150ms (10 batches of 10 resources)
- Response build: <10ms
- **Total: ~170ms (vs ~1000ms sequential)**

## Rollout Plan

**Phase 1** (Week 1-2): Core infrastructure
- Implement BundleEntryParser
- Implement BundleReferencePreProcessor
- Implement BundleEntryExecutor with MediatR integration
- Unit tests (80%+ coverage)

**Phase 2** (Week 3): Sequential execution
- Implement sequential execution path
- Transaction support with rollback
- Integration tests
- Benchmark performance

**Phase 3** (Week 4): Parallel execution
- Implement parallel execution engine
- Add Parallel.ForEachAsync support
- Verb grouping and ordering
- Performance testing

**Phase 4** (Week 5): Advanced features
- Conditional operations (If-None-Exist, etc.)
- Reference resolution for conditional references
- Retry logic for 429 responses
- Channel-based processing for large bundles

**Phase 5** (Week 6): Polish & production
- Error handling improvements
- Observability (metrics, logging, tracing)
- Documentation
- Production readiness review

## Open Questions

1. **Should we support streaming bundle responses?** For very large batch bundles, streaming might reduce memory pressure
2. **Channel vs Parallel.ForEachAsync?** Channels provide backpressure but add complexity
3. **Retry strategy configuration?** Should retry be configurable per tenant/deployment?
4. **Bundle size limits?** Should we enforce max entries beyond configuration?

## References

- FHIR R4 Bundle Processing: https://hl7.org/fhir/R4/http.html#transaction
- Legacy BundleHandler implementation: `src-old/Microsoft.Health.Fhir.Shared.Api/Features/Resources/Bundle/BundleHandler.cs`
- ADR 2510 Transaction Abstraction: Phase 4 design
- MediatR documentation: https://github.com/jbogard/MediatR
