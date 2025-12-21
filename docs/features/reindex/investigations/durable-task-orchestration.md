# Investigation: Durable Task Orchestration

**Feature**: reindex
**Status**: In Progress
**Created**: 2025-12-19

## Approach

Use **DurableTask Framework** (already integrated in Ignixa) to implement a multi-level orchestration pattern for reindexing FHIR resources. The Microsoft FHIR Server used a custom JobManagement framework with an Orchestrator/Processing Job pattern - we can adapt this proven architecture to use DurableTask primitives instead.

### Scalability Design: Surrogate ID Range Partitioning

**Handles 1M to 1B+ rows** using the same proven chunking strategy as Export/Import operations.

Instead of processing all resources sequentially, the orchestrator:
1. **Partitions by resource type** - Each type (Patient, Observation, etc.) processed independently
2. **Subdivides into surrogate ID ranges** - Each type split into N chunks based on surrogate IDs
3. **Parallel processing** - Multiple workers process different ranges simultaneously

**Example** (1B Patient resources):
```
Total Patients: 1,000,000,000
NumberOfRangesPerType: 16 (configurable 1-16)

GetReindexRangesActivity returns:
- Range 1: [surrogate_id 1 .. 62,500,000]       → Worker 1
- Range 2: [surrogate_id 62,500,001 .. 125,000,000] → Worker 2
- ...
- Range 16: [surrogate_id 937,500,001 .. 1,000,000,000] → Worker 16

Each worker:
- Fetches 100 resources at a time (MaximumNumberOfResourcesPerQuery)
- Updates indices in batches of 1000 (MaximumNumberOfResourcesPerWrite)
- Reports progress independently
```

**SQL Query Behind the Scenes** (from src/DataLayer/Ignixa.DataLayer.SqlEntityFramework):
```sql
-- GetExportRangesAsync logic (same used for reindex):
SELECT MIN(ResourceSurrogateId), MAX(ResourceSurrogateId), COUNT(*)
FROM Resources
WHERE ResourceTypeId = @resourceTypeId
  AND IsHistory = 0
  AND IsDeleted = 0

-- Then divide (MaxId - MinId) / NumberOfRanges into equal-sized chunks
```

**Why this scales**:
- No continuation tokens needed (ranges are pre-calculated)
- Each worker processes independently (no coordination overhead)
- Parallelism limited only by DurableTask worker pool (24-48 concurrent workers)
- No memory pressure (streaming batches of 100 resources at a time)

### Architecture Overview

**Three-Layer Job Model** (adapted from src-old implementation):

1. **Reindex Orchestrator** (ReindexOrchestrationJob)
   - Single long-running orchestration instance per reindex request
   - Queries resource counts per resource type
   - **Calls GetReindexRangesActivity** to partition each resource type into surrogate ID ranges
   - Spawns parallel worker activities (one per surrogate ID range)
   - Waits for all workers using `Task.WhenAll()` (no polling loop)
   - Updates search parameter status on completion
   - Handles cancellation by terminating child workers

2. **Worker Activities** (ReindexWorkerActivity)
   - One instance per surrogate ID range (e.g., Patient [1..62,500,000])
   - Queries resources in batches (e.g., 100 resources at a time) using surrogate ID predicates
   - Invokes `BulkUpdateSearchParameterIndicesAsync` to write new index entries
   - Reports progress back via activity result
   - No continuation tokens needed (range boundary defines scope)

3. **Supporting Activities**
   - **GetReindexRangesActivity**: Partition resource type into surrogate ID ranges (calls `ISearchService.GetExportRangesAsync`)
   - **UpdateSearchParameterStatusActivity**: Mark parameters as Enabled/Disabled/Deleted after all workers complete

### Key Implementation Details

**Orchestration Input/Output**:
```csharp
// Input (from HTTP POST /admin/reindex):
public record ReindexOrchestrationInput(
    string JobId,
    int TenantId,
    List<string> TargetResourceTypes,  // Empty = all types
    List<string> TargetSearchParameterUrls,
    int NumberOfRangesPerType,  // 1-16, controls parallelism
    uint MaximumNumberOfResourcesPerQuery,  // Batch size (default 100)
    uint MaximumNumberOfResourcesPerWrite);  // Write batch (default 1000)

// Output (stored in orchestration result):
public record ReindexOrchestrationOutput(
    bool Success,
    long TotalResourcesReindexed,
    Dictionary<string, long> ResourceCountsByType,
    List<string> UpdatedSearchParameters,
    string? ErrorMessage);
```

**Durable Task Features Used**:
- `TaskHubClient.CreateOrchestrationInstanceAsync()` - Start orchestration
- `TaskHubClient.TerminateInstanceAsync()` - Cancel operation
- `context.ScheduleTask<T>()` - Fan-out to parallel worker activities (24-48 concurrent)
- `Task.WhenAll(workerTasks)` - Wait for all workers without polling
- `TaskHubClient.GetOrchestrationStateAsync()` - Query progress
- `TaskHubClient.SuspendAsync()/ResumeAsync()` - Pause/resume support (pauses between activities)

**Consistency Guarantees**:
1. Each processing job updates resources in batches with retry logic (Polly policy for SQL timeouts)
2. ResourceWrapper.SearchParameterHash tracks which index version each resource has
3. On failure, orchestration can be restarted - already-processed resources are skipped via hash check
4. Search parameters only marked Enabled after ALL resource types complete successfully

## Tradeoffs

| Pros | Cons |
|------|------|
| **Built-in pause/resume/cancel** - DurableTask provides `TerminateInstanceAsync`, external events for pause/resume | **Learning curve** - Team needs to understand orchestration replay semantics |
| **Exactly-once execution** - Orchestration replay ensures deterministic progress tracking | **SQL Server backend dependency** - Requires DurableTask.SqlServer configured (already done in DurableTaskConfiguration.cs) |
| **Progress monitoring** - Can query orchestration status via `GetOrchestrationStateAsync` | **More complex than simple queue** - Overkill for small datasets (<10k resources) |
| **Proven pattern** - Microsoft FHIR Server used similar Orchestrator/Processing split successfully | **Instance state size limits** - Very large resource lists may hit storage limits (workaround: use continuation tokens) |
| **Fault tolerance** - Orchestration automatically retries failed activities | **Concurrency control needed** - Must prevent multiple reindex orchestrations from conflicting |
| **Tenant isolation** - Each tenant's reindex runs in separate orchestration instance | **No built-in progress bar** - UI needs to poll orchestration state for percentage complete |
| **Multi-tenancy support** - Can run one orchestration per tenant concurrently without conflicts | |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
  - API: ReindexEndpoints.cs (POST /admin/reindex, GET /admin/reindex/{id}, DELETE /admin/reindex/{id})
  - Application: ReindexOrchestrationJob, ReindexProcessingJob, Activities
  - DataLayer: BulkUpdateSearchParameterIndicesAsync (already exists in IFhirDataStore)

- [x] F5 Developer Experience (works with minimal setup)
  - FileBasedOrchestrationService fallback for local dev (no SQL Server required)
  - SqlOrchestrationService for integration testing (uses existing tenant SQL connection)

- [x] FHIR spec compliance (if applicable)
  - Reindex is not part of FHIR spec, but follows common implementation patterns (see HAPI FHIR, Firely, Smile CDR)

- [x] Consistent with existing patterns
  - Already using DurableTask for Export/Import operations (ExportOrchestration, ImportOrchestration)
  - Reuses ResourceWrapper.SearchParameterHash mechanism from src-old

## Evidence

### src-old Implementation Analysis

**Orchestrator Job** (src-old/Microsoft.Health.Fhir.Core/Features/Operations/Reindex/ReindexOrchestratorJob.cs:316):
```csharp
// Uses GetSurrogateIdRanges to partition work (SQL Server path):
var ranges = await searchService.Value.GetSurrogateIdRanges(
    resourceType,
    resourceCount.StartResourceSurrogateId,  // Min surrogate ID
    resourceCount.EndResourceSurrogateId,    // Max surrogate ID
    resourcesPerJob,  // e.g., 10000 resources per range
    (int)Math.Ceiling(resourceCount.Count / (double)resourcesPerJob),  // Number of ranges
    true,  // Direction (ascending)
    cancellationToken);

// Returns: [(1, 10000), (10001, 20000), ...] for parallel processing
```

- Uses custom `IQueueClient` with `EnqueueAsync`, `GetJobByGroupIdAsync`, `CancelJobByIdAsync`
- Polls child jobs in a `while (activeJobs.Any())` loop with adaptive polling intervals (100ms-5000ms)
- Supports dynamic search parameter updates during execution (CheckForSearchParameterUpdates every 5 minutes)
- **Surrogate ID ranging for SQL Server** (proven scalable) vs traditional chunking for Cosmos DB
- Marks search parameters Enabled/Disabled/Deleted only after all resource types complete

**Processing Job** (src-old/Microsoft.Health.Fhir.Core/Features/Operations/Reindex/ReindexProcessingJob.cs):
- Fetches resources via `SearchForReindexAsync(queryParams, searchParamHash, countOnly: false)`
- Uses `ResourceWrapperFactory.Update(resource)` to regenerate search indices
- Writes in batches via `BulkUpdateSearchParameterIndicesAsync(batch, cancellationToken)`
- Creates child jobs if more resources remain (continuation pattern - used when range too large)
- Retries SQL timeouts up to 3 times with exponential backoff

**Scalability Validation** (from src-old tests and production usage):
- Microsoft FHIR Server uses this for **Azure Health Data Services** (multi-tenant, billions of resources)
- Export operation uses identical `GetSurrogateIdRanges` pattern (proven in Ignixa for millions of resources)
- Each worker processes independently - no shared state beyond orchestration coordination

**Data Model** (src-old/Microsoft.Health.Fhir.Core/Features/Operations/Reindex/Models/ReindexJobRecord.cs):
```csharp
public class ReindexJobRecord {
    public long Count { get; set; } // Total resources to reindex
    public long Progress { get; set; } // Completed so far
    public ConcurrentDictionary<string, SearchResultReindex> ResourceCounts { get; }
    public ICollection<string> SearchParams { get; } // Search parameter URLs
    public uint MaximumNumberOfResourcesPerQuery { get; } // Batch size (default 100)
    public uint MaximumNumberOfResourcesPerWrite { get; } // Write batch (default 1000)
}
```

**Consistency Mechanism**:
- SQL stored procedure `UpdateReindexJob` uses optimistic concurrency (rowversion check)
- Search parameter hash stored in `ResourceWrapper.SearchParameterHash`
- SQL query filters by hash mismatch: `WHERE SearchParameterHash != @expectedHash`

### DurableTask Framework Capabilities

From [GitHub - Azure/durabletask](https://github.com/Azure/durabletask) and [Durable Task Framework Internals - Part 4](https://abhikmitra.github.io/blog/durable-task-4/):

**Pause/Resume**:
- `TaskHubClient.SuspendAsync(instanceId)` - Pause orchestration
- `TaskHubClient.ResumeAsync(instanceId)` - Resume orchestration
- Paused orchestrations don't process new events until resumed

**Cancel/Terminate**:
- `TaskHubClient.TerminateInstanceAsync(instanceId, reason)` - Immediate termination
- Termination is asynchronous - ongoing activities complete, but no new activities start
- No built-in facility to cancel in-progress activities (must use CancellationToken pattern)

**Concurrency**:
- Can run multiple orchestrations concurrently (limited by worker thread pool)
- Workers process multiple work items subject to configured concurrency limits
- Distributed locking via storage backend (SQL Server row locks in our case)

**State Persistence**:
- Orchestration state persisted to SQL via `SqlOrchestrationService`
- Schema auto-created via `CreateIfNotExistsAsync` (see DurableTaskHostedService.cs)
- Can query state via `GetOrchestrationStateAsync(instanceId)`

### Current Ignixa DurableTask Configuration

From src/Application/Ignixa.Api/Infrastructure/DurableTaskConfiguration.cs:

```csharp
// Registered orchestrations:
- ExportOrchestration (proven working implementation with range partitioning)
- ImportOrchestration (proven working implementation)
- TerminologyImportOrchestration (proven working implementation)

// SQL Server backend configuration:
- Connection string from Tenant 0 (system partition)
- TaskHubName: "ignixa"
- Schema: DurableTask.SqlServer auto-creates tables
```

**Implication**: We already have working DurableTask infrastructure with SQL Server backend. Reindex orchestration follows the **exact same pattern as ExportOrchestration**.

**Proven Scalability Evidence** (from src/Application/Ignixa.Application.BackgroundOperations/Export):

ExportOrchestration.cs (lines 12-22):
```csharp
/// Implements high-performance partition-based export:
/// 1. Determines which resource types to export
/// 2. For EACH type: calls GetExportRangesActivity to partition into surrogate ID ranges
/// 3. For EACH partition: queues ExportWorkerActivity to stream range to file
/// 4. Waits for all workers to complete in parallel
/// 5. Returns aggregated results
///
/// This design achieves >10K resources/sec by:
/// - Eliminating pagination (no continuation tokens)
/// - Streaming directly from DB to file (no intermediate buffering)
/// - Parallel execution of 24-48 worker activities (6 types × 4-8 ranges each)
```

**Reindex will use the same proven architecture** - substitute "export to file" with "update search indices", everything else identical.

## Verdict

**Recommended with conditions**:

1. **Use DurableTask Framework** - The pause/resume/cancel capabilities, built-in retry logic, and existing infrastructure make this the most robust approach.

2. **Adapt src-old pattern** - The Orchestrator/Processing split is proven and maps cleanly to DurableTask primitives:
   - ReindexOrchestratorJob → `OrchestrationContext.CreateSubOrchestrationInstance()`
   - ReindexProcessingJob → Sub-orchestration (one per resource type chunk)
   - IQueueClient polling → DurableTask automatic child orchestration tracking

3. **Key differences from src-old**:
   - **No custom IQueueClient** - Use DurableTask's built-in orchestration management
   - **No polling loop** - Parent orchestration uses `context.WaitAll(childTasks)` instead of manual polling
   - **Explicit pause/resume API** - Add `TaskHubClient.SuspendAsync/ResumeAsync` endpoints
   - **Progress tracking** - Store in orchestration state, query via `GetOrchestrationStateAsync`

4. **Concurrency control**:
   - Use `forceOneActiveJobGroup` pattern (check for existing reindex orchestration before starting)
   - Alternatively: Use Durable Task's singleton orchestrations (single instance ID per tenant)

5. **Consistency guarantees**:
   - Reuse `ResourceWrapper.SearchParameterHash` mechanism
   - Update search parameter status atomically after all processing jobs complete
   - Support "resume from failure" by checking hash on each resource

6. **Open questions** (for further investigation):
   - **Can multiple reindex orchestrations run simultaneously?**
     - Yes, if targeting different search parameters or resource types
     - No, if reindexing same resources (risk of conflicting hash updates)
     - **Recommendation**: Enforce single active reindex per tenant via singleton pattern

   - **How to ensure all items in consistent state?**
     - Use `SearchParameterHash` as version marker
     - Only mark search parameter Enabled after ALL resources updated
     - On restart, query resources with stale hash and continue processing
     - **Edge case**: What if resource updated between reindex start and completion? (Hash would be overwritten - acceptable, new index is correct)

   - **Pause semantics - does it stop mid-batch?**
     - DurableTask pauses BETWEEN activities, not during
     - Currently executing `BulkUpdateSearchIndicesActivity` completes before pause takes effect
     - **Recommendation**: Design activities to be small (~1000 resources) for responsive pause

## Alternative Approaches Worth Investigating

1. **Simple background service with stored state** - Simpler, but no built-in pause/resume/cancel
2. **Hangfire recurring job** - Easy integration, but less control over orchestration
3. **MassTransit saga pattern** - More messaging-oriented, less suited to long-running SQL queries

---

**Next Steps**:
1. Create ADR if this approach is accepted
2. Define orchestration API contract (POST /admin/reindex, GET status, DELETE cancel)
3. Implement proof-of-concept with single resource type
4. Test pause/resume/cancel with integration tests
5. Performance test with 1M+ resources
