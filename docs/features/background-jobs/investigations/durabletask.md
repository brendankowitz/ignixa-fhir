# Investigation: Background Jobs with DurableTask Framework

**Feature**: Background Jobs
**Date**: 2025-10-14
**Status**: Complete

## Executive Summary

For long-running background operations ($reindex, $export, $import, $bulk-delete), we will use the Azure DurableTask framework instead of custom job implementations. This provides enterprise-grade workflow orchestration with persistent state management.

**Key Decision**: Use DurableTask for all background operations requiring persistence, monitoring, and fault tolerance.

## Problem Statement

### Background Operations in FHIR Server

FHIR servers require long-running background operations:

1. **$reindex**: Reindex thousands/millions of resources when search parameters change
2. **$export**: Export large datasets to NDJSON files
3. **$import**: Import bulk NDJSON data with validation
4. **$bulk-delete**: Delete resources matching criteria
5. **$bulk-update**: Update resources in batches
6. **Subscription processing**: Process subscription events asynchronously

**Requirements**:
- ✅ Persistent state (survives server restarts)
- ✅ Progress monitoring (percentage complete, estimated time)
- ✅ Fault tolerance (retry failed tasks, handle crashes)
- ✅ Cancellation support (user can cancel long operations)
- ✅ Scalability (parallel processing, multiple workers)
- ✅ Durability (guaranteed completion or explicit failure)

### Legacy Approach: Custom Task Framework

The old microsoft/fhir-server used a custom task framework:

**Problems**:
- ❌ Custom state persistence (SQL table with serialized state)
- ❌ Custom retry logic (prone to bugs)
- ❌ Limited scalability (single-threaded execution)
- ❌ No built-in monitoring/observability
- ❌ Difficult to test
- ❌ Maintenance burden

**Example** (legacy):
```csharp
public class ReindexJob
{
    public string JobId { get; set; }
    public string Status { get; set; }  // Running, Completed, Failed
    public int Progress { get; set; }
    public string? Error { get; set; }
    public string? SerializedState { get; set; }  // JSON blob
}
```

## DurableTask Framework

### What is DurableTask?

**Source**: https://github.com/Azure/durabletask

**Description**: C# library for creating long-running, persistent workflows using simple async/await patterns.

**Key Features**:
- ✅ Linear scalability (add more worker machines)
- ✅ Multiple persistence backends (Azure Storage, SQL Server, Redis, In-Memory)
- ✅ Built-in state management
- ✅ Automatic retry and fault tolerance
- ✅ Workflow patterns: Orchestrations (workflows) + Activities (tasks)
- ✅ Powers Azure Durable Functions

**Developed By**: Microsoft Azure team
**License**: MIT (open source)

### Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Orchestration (Long-running workflow)                  │
│  - ReindexOrchestration                                 │
│  - ExportOrchestration                                  │
│  - Async/await code that coordinates activities         │
├─────────────────────────────────────────────────────────┤
│  Activities (Individual units of work)                  │
│  - ReindexBatchActivity                                 │
│  - ExportResourcesActivity                              │
│  - Stateless, retriable, idempotent                     │
├─────────────────────────────────────────────────────────┤
│  DurableTask Runtime                                    │
│  - State persistence                                    │
│  - Task scheduling                                      │
│  - Retry logic                                          │
│  - Worker coordination                                  │
├─────────────────────────────────────────────────────────┤
│  Storage Backend                                        │
│  - SQL Server (recommended for Ignixa)                  │
│  - Azure Storage (for cloud)                            │
│  - In-Memory (for testing)                              │
└─────────────────────────────────────────────────────────┘
```

### Core Concepts

#### 1. Orchestration

A long-running workflow written in C# using async/await:

```csharp
// Ignixa.Api/Features/Reindex/ReindexOrchestration.cs
namespace Ignixa.Features.Reindex;

public class ReindexOrchestration : TaskOrchestration<ReindexResult, ReindexRequest>
{
    public override async Task<ReindexResult> RunTask(
        OrchestrationContext context,
        ReindexRequest input)
    {
        // 1. Get total resource count
        var totalCount = await context.ScheduleTask<int>(
            typeof(CountResourcesActivity),
            input.ResourceType);

        // 2. Process in batches
        const int batchSize = 1000;
        var batches = (int)Math.Ceiling((double)totalCount / batchSize);
        var processed = 0;

        for (int i = 0; i < batches; i++)
        {
            // 3. Schedule batch activity
            var batchInput = new ReindexBatchInput
            {
                ResourceType = input.ResourceType,
                SearchParameterUrl = input.SearchParameterUrl,
                Offset = i * batchSize,
                Count = batchSize
            };

            var batchResult = await context.ScheduleTask<int>(
                typeof(ReindexBatchActivity),
                batchInput);

            processed += batchResult;

            // 4. Update progress (persisted automatically)
            context.SetCustomStatus(new
            {
                TotalCount = totalCount,
                Processed = processed,
                PercentComplete = (double)processed / totalCount * 100
            });
        }

        // 5. Mark search parameter as Enabled
        await context.ScheduleTask<bool>(
            typeof(EnableSearchParameterActivity),
            input.SearchParameterUrl);

        return new ReindexResult
        {
            TotalProcessed = processed,
            Status = "Completed"
        };
    }
}
```

**Key Points**:
- Orchestration code replays from beginning on every continuation
- State checkpointed at each `await` (survives crashes)
- No complex state management - just write normal async code

#### 2. Activity

A stateless, retriable unit of work:

```csharp
// Ignixa.Api/Features/Reindex/Activities/ReindexBatchActivity.cs
namespace Ignixa.Features.Reindex.Activities;

public class ReindexBatchActivity : TaskActivity<ReindexBatchInput, int>
{
    private readonly IFhirRepository _repository;
    private readonly ISearchIndexBuilder _indexBuilder;
    private readonly ISearchParameterRepository _searchParamRepo;

    public override async Task<int> Execute(
        TaskContext context,
        ReindexBatchInput input)
    {
        // 1. Get search parameter definition
        var searchParam = await _searchParamRepo.GetByUrlAsync(
            input.SearchParameterUrl,
            context.CancellationToken);

        if (searchParam == null)
            throw new InvalidOperationException($"Search parameter not found: {input.SearchParameterUrl}");

        // 2. Fetch batch of resources
        var resources = await _repository.SearchAsync(
            input.ResourceType,
            parameters: null,
            count: input.Count,
            offset: input.Offset,
            context.CancellationToken);

        // 3. Reindex each resource
        var indexed = 0;
        foreach (var resource in resources)
        {
            // Extract search values
            var values = await _indexBuilder.ExtractSearchParameterValuesAsync(
                resource.Instance,
                searchParam,
                context.CancellationToken);

            // Update index
            await _indexBuilder.IndexAsync(
                resource.ResourceKey,
                searchParam.Url,
                values,
                context.CancellationToken);

            indexed++;
        }

        return indexed;
    }
}

public record ReindexBatchInput(
    string ResourceType,
    string SearchParameterUrl,
    int Offset,
    int Count);
```

**Key Points**:
- Activities are stateless and idempotent
- Can be retried on failure
- Dependency injection works normally
- Can run in parallel across multiple workers

### Supported Storage Backends

| Backend | Use Case | Ignixa Recommendation |
|---------|----------|----------------------|
| **SQL Server** | Production on-premises | ✅ **PRIMARY** (Phases 8+) |
| **Azure Storage** | Production cloud | ✅ Cloud deployment |
| **Netherite** | High-performance cloud | ✅ Large-scale cloud |
| **Redis** | Distributed state | ⚠️ Experimental |
| **In-Memory Emulator** | Testing/development | ✅ Phase 1-7 (F5 experience) |

**Recommendation for Ignixa**:
- **Phase 1-7** (File/InMemory): In-Memory Emulator (zero external dependencies)
- **Phase 8+** (SQL Server): SQL Server backend (shared with resource storage)
- **Cloud Deployment**: Azure Storage or Netherite

### Workflow Patterns

#### Pattern 1: Fan-Out/Fan-In

Process batches in parallel, aggregate results:

```csharp
public override async Task<ExportResult> RunTask(
    OrchestrationContext context,
    ExportRequest input)
{
    // 1. Get all resource IDs
    var resourceIds = await context.ScheduleTask<List<string>>(
        typeof(GetResourceIdsActivity),
        input.ResourceType);

    // 2. Fan-out: Export in parallel batches
    var batchSize = 1000;
    var batches = resourceIds.Chunk(batchSize).ToList();

    var exportTasks = batches.Select((batch, index) =>
        context.ScheduleTask<string>(
            typeof(ExportBatchActivity),
            new ExportBatchInput
            {
                ResourceIds = batch.ToList(),
                OutputPath = $"{input.OutputPath}/part-{index}.ndjson"
            })).ToList();

    // 3. Fan-in: Wait for all batches
    var batchResults = await Task.WhenAll(exportTasks);

    // 4. Consolidate
    var manifestPath = await context.ScheduleTask<string>(
        typeof(CreateManifestActivity),
        new ManifestInput { BatchPaths = batchResults.ToList() });

    return new ExportResult { ManifestPath = manifestPath };
}
```

#### Pattern 2: Human Interaction (Approval)

Wait for external event (e.g., user approval):

```csharp
public override async Task<BulkDeleteResult> RunTask(
    OrchestrationContext context,
    BulkDeleteRequest input)
{
    // 1. Preview what will be deleted
    var preview = await context.ScheduleTask<DeletePreview>(
        typeof(PreviewDeleteActivity),
        input.SearchCriteria);

    // 2. Wait for approval (timeout: 1 hour)
    var approved = await context.WaitForExternalEvent<bool>(
        "ApprovalEvent",
        TimeSpan.FromHours(1));

    if (!approved)
    {
        return new BulkDeleteResult { Status = "Cancelled" };
    }

    // 3. Execute deletion
    var deleted = await context.ScheduleTask<int>(
        typeof(ExecuteDeleteActivity),
        input.SearchCriteria);

    return new BulkDeleteResult
    {
        Status = "Completed",
        DeletedCount = deleted
    };
}
```

**Trigger approval**:
```csharp
// User approves via API
POST /$bulk-delete/job-123/approve

await _orchestrationClient.RaiseEventAsync(
    instanceId: "job-123",
    eventName: "ApprovalEvent",
    eventData: true);
```

#### Pattern 3: Monitor/Polling

Periodically check external state:

```csharp
public override async Task RunTask(
    OrchestrationContext context,
    SubscriptionWatchdogInput input)
{
    while (!context.IsReplaying)
    {
        // 1. Check for missed transactions
        var missedTransactions = await context.ScheduleTask<List<long>>(
            typeof(CheckMissedTransactionsActivity),
            input.LastProcessedTransactionId);

        // 2. Process missed transactions
        if (missedTransactions.Any())
        {
            foreach (var txId in missedTransactions)
            {
                await context.ScheduleTask<bool>(
                    typeof(ProcessTransactionActivity),
                    txId);
            }
        }

        // 3. Wait 30 seconds before next check
        await context.CreateTimer(
            context.CurrentUtcDateTime.AddSeconds(30),
            CancellationToken.None);
    }
}
```

## Implementation Approach

### 1. Project Structure

```
Ignixa.Api/
  Features/
    Reindex/
      ReindexEndpoints.cs          # POST /$reindex, GET /$reindex/{jobId}
      ReindexOrchestration.cs      # DurableTask orchestration
      Activities/
        CountResourcesActivity.cs
        ReindexBatchActivity.cs
        EnableSearchParameterActivity.cs
    Export/
      ExportEndpoints.cs           # POST /$export, GET /$export/{jobId}
      ExportOrchestration.cs
      Activities/
        GetResourceIdsActivity.cs
        ExportBatchActivity.cs
        CreateManifestActivity.cs
    Import/
      ImportEndpoints.cs
      ImportOrchestration.cs
      Activities/
        ValidateBatchActivity.cs
        ImportBatchActivity.cs
  Shared/
    Infrastructure/
      DurableTask/
        DurableTaskHostedService.cs   # Background worker
        DurableTaskClientFactory.cs   # Create orchestration clients
```

### 2. Registration and Configuration

```csharp
// Ignixa.Api/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register DurableTask
builder.Services.AddDurableTask(options =>
{
    // Use SQL Server backend (Phase 8+)
    options.UseSqlServer(builder.Configuration.GetConnectionString("FhirDb"));

    // OR use In-Memory emulator (Phase 1-7)
    // options.UseInMemoryEmulator();

    // Register orchestrations
    options.AddOrchestration<ReindexOrchestration>();
    options.AddOrchestration<ExportOrchestration>();
    options.AddOrchestration<ImportOrchestration>();

    // Register activities
    options.AddActivity<CountResourcesActivity>();
    options.AddActivity<ReindexBatchActivity>();
    options.AddActivity<EnableSearchParameterActivity>();
    options.AddActivity<GetResourceIdsActivity>();
    options.AddActivity<ExportBatchActivity>();
    // ... etc
});

// Background worker to process orchestrations
builder.Services.AddHostedService<DurableTaskHostedService>();

var app = builder.Build();
```

### 3. API Endpoints

```csharp
// Ignixa.Api/Features/Reindex/ReindexEndpoints.cs
namespace Ignixa.Features.Reindex;

public static class ReindexEndpoints
{
    public static void MapReindexEndpoints(this WebApplication app)
    {
        // Start reindex job
        app.MapPost("/$reindex", async (
            HttpContext httpContext,
            ReindexRequest request,
            TaskHubClient orchestrationClient) =>
        {
            // Create orchestration instance
            var instanceId = Guid.NewGuid().ToString();

            await orchestrationClient.CreateOrchestrationInstanceAsync(
                typeof(ReindexOrchestration),
                instanceId,
                request);

            // Return 202 Accepted with job location
            httpContext.Response.Headers["Content-Location"] = $"/$reindex/{instanceId}";
            return Results.Accepted($"/$reindex/{instanceId}", new
            {
                jobId = instanceId,
                status = "Accepted"
            });
        });

        // Get reindex job status
        app.MapGet("/$reindex/{jobId}", async (
            string jobId,
            TaskHubClient orchestrationClient) =>
        {
            var state = await orchestrationClient.GetOrchestrationStateAsync(jobId);

            if (state == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                jobId,
                status = state.OrchestrationStatus.ToString(),
                customStatus = state.Status,  // Our progress info
                createdTime = state.CreatedTime,
                lastUpdatedTime = state.LastUpdatedTime,
                output = state.Output  // Final result when complete
            });
        });

        // Cancel reindex job
        app.MapDelete("/$reindex/{jobId}", async (
            string jobId,
            TaskHubClient orchestrationClient) =>
        {
            await orchestrationClient.TerminateInstanceAsync(jobId, "User cancelled");
            return Results.NoContent();
        });
    }
}
```

### 4. Testing with In-Memory Emulator

```csharp
// Ignixa.Api.Tests/Features/Reindex/ReindexOrchestrationTests.cs
public class ReindexOrchestrationTests
{
    [Fact]
    public async Task ReindexOrchestration_ProcessesAllBatches_Completes()
    {
        // Arrange
        var emulator = new LocalOrchestrationService();
        var worker = new TaskHubWorker(emulator);

        // Register orchestration and activities
        worker.AddTaskOrchestrations(typeof(ReindexOrchestration));
        worker.AddTaskActivities(typeof(CountResourcesActivity));
        worker.AddTaskActivities(typeof(ReindexBatchActivity));
        worker.AddTaskActivities(typeof(EnableSearchParameterActivity));

        await worker.StartAsync();

        var client = new TaskHubClient(emulator);

        // Act
        var instance = await client.CreateOrchestrationInstanceAsync(
            typeof(ReindexOrchestration),
            "test-instance",
            new ReindexRequest
            {
                ResourceType = "Patient",
                SearchParameterUrl = "http://example.com/SearchParameter/test"
            });

        // Wait for completion (with timeout)
        var state = await client.WaitForOrchestrationAsync(
            instance,
            TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(OrchestrationStatus.Completed, state.OrchestrationStatus);

        var result = JsonSerializer.Deserialize<ReindexResult>(state.Output);
        Assert.Equal("Completed", result.Status);
        Assert.True(result.TotalProcessed > 0);

        await worker.StopAsync();
    }
}
```

## Benefits Over Custom Implementation

| Feature | Custom Task Framework | DurableTask |
|---------|----------------------|-------------|
| **State Persistence** | Manual SQL table + JSON serialization | ✅ Automatic, versioned |
| **Retry Logic** | Custom implementation (bugs) | ✅ Built-in, configurable |
| **Fault Tolerance** | Manual checkpointing | ✅ Automatic replay from checkpoints |
| **Scalability** | Single-threaded | ✅ Linear (add workers) |
| **Monitoring** | Custom dashboard | ✅ Built-in status API |
| **Testing** | Complex mocking | ✅ In-Memory emulator |
| **Cancellation** | Manual state updates | ✅ Built-in termination |
| **Progress Tracking** | Manual updates | ✅ SetCustomStatus() |
| **Code Complexity** | High (state machine) | ✅ Low (async/await) |
| **Maintenance** | Custom codebase | ✅ Microsoft-supported |

## Performance Considerations

**Orchestration Replay**:
- Orchestration code replays from beginning on every continuation
- Keep orchestration logic simple (no heavy computation)
- All work should be in activities

**Activity Idempotence**:
- Activities may be retried
- Ensure activities are idempotent (safe to run multiple times)

**State Size**:
- Keep orchestration state small (<1MB recommended)
- Don't load large datasets into orchestration memory
- Pass resource IDs to activities, not full resources

## Phase Integration

### Phase 3: Validation (Weeks 8-10) - ADR-2506

**Optional**: Use DurableTask for async validation jobs (if needed)

### Phase 7: Distributed Infrastructure (Weeks 24-29) - ADR-2510

**Deliverable**: Configure DurableTask with distributed storage (SQL Server or Azure Storage)

### Phase 12: Custom Search Parameters (Weeks 56-59) - ADR-2515

**Deliverable**:
- `ReindexOrchestration` for search parameter reindexing
- `POST /$reindex` endpoint with DurableTask

### Phase 13: Bulk Operations (Weeks 60-65) - ADR-2516

**Deliverables**:
- `ExportOrchestration` for $export
- `ImportOrchestration` for $import
- `BulkDeleteOrchestration` for $bulk-delete
- `BulkUpdateOrchestration` for $bulk-update

### Phase 14: Advanced Operations (Weeks 66-71) - ADR-2517

**Deliverables**:
- Long-running custom operations using DurableTask

## Summary

### Key Decisions

1. ✅ **Use DurableTask** for all long-running background operations
2. ✅ **In-Memory Emulator** for Phase 1-7 (F5 developer experience)
3. ✅ **SQL Server Backend** for Phase 8+ (production)
4. ✅ **Azure Storage/Netherite** for cloud deployments
5. ✅ **Orchestration = Workflow**, **Activity = Unit of Work**

### Architectural Impact

**ADR-2500 Update**: Add architectural decision #21:
- **DurableTask Framework**: All background operations use Azure DurableTask for orchestration (not custom task framework)

### Migration from Legacy

**Legacy Task Framework** → **DurableTask**:
1. Convert job classes → Orchestrations
2. Convert step methods → Activities
3. Replace custom state persistence → DurableTask runtime
4. Replace custom retry → DurableTask retry policies
5. Replace custom monitoring → DurableTask status API

**Estimated Effort**: 50% reduction in code compared to custom implementation

## References

- DurableTask Framework: https://github.com/Azure/durabletask
- Azure Durable Functions: https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-overview
- Netherite Backend: https://microsoft.github.io/durabletask-netherite/
- SQL Server Backend: https://github.com/microsoft/durabletask-mssql
