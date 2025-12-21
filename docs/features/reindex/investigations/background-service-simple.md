# Investigation: Simple Background Service

**Feature**: reindex
**Status**: In Progress
**Created**: 2025-12-19

## Approach

Implement reindex as a **BackgroundService** (IHostedService) that continuously polls a SQL table for pending reindex jobs and processes them in a simple loop. No orchestration framework, no DurableTask - just a straightforward worker pattern.

### Architecture Overview

**Single-Process Model**:

1. **ReindexBackgroundService** (IHostedService)
   - Runs continuously in the background
   - Polls `ReindexJobs` table every 5 seconds for jobs with status "Pending" or "Running"
   - Processes one job at a time (or N jobs in parallel using SemaphoreSlim)
   - Updates job status directly in SQL

2. **ReindexJob Table** (SQL)
   ```sql
   CREATE TABLE ReindexJobs (
       JobId UNIQUEIDENTIFIER PRIMARY KEY,
       TenantId INT NOT NULL,
       Status NVARCHAR(20) NOT NULL,  -- Pending, Running, Paused, Completed, Failed
       TargetResourceTypes NVARCHAR(MAX),  -- JSON array
       SearchParameterUrls NVARCHAR(MAX),  -- JSON array
       Progress BIGINT DEFAULT 0,
       TotalResources BIGINT,
       ErrorMessage NVARCHAR(MAX),
       CreatedDate DATETIME2 NOT NULL,
       LastHeartbeat DATETIME2,
       ROWVERSION
   )
   ```

3. **Processing Logic** (in-memory state machine)
   - Fetch job from SQL
   - For each resource type:
     - Query resources in batches (e.g., 100 at a time) using surrogate ID WHERE clause
     - Update search indices in batches of 1000
     - Update Progress counter after each batch
   - Mark job as Completed or Failed

### Scalability for 1M-1B Rows

**Batching Strategy**:
```csharp
// Fetch min/max surrogate IDs for resource type
var (minId, maxId, count) = await GetResourceTypeStatsAsync(resourceType);
long currentId = minId;

while (currentId <= maxId)
{
    // Fetch next batch
    var resources = await GetResourcesByIdRangeAsync(resourceType, currentId, currentId + 100);

    // Update indices
    await BulkUpdateSearchParameterIndicesAsync(resources);

    // Update progress in SQL (every 1000 resources to reduce DB writes)
    if (++processedCount % 1000 == 0)
    {
        await UpdateJobProgressAsync(jobId, processedCount);
    }

    currentId += 100;
}
```

**Parallelism**:
- Single background service processes 1 job at a time
- Within a job, can use `Parallel.ForEachAsync` for multiple resource types simultaneously
- No built-in support for multiple workers on same job (would require distributed locking)

**Why this scales (or doesn't)**:
- ✅ Simple batching handles large datasets
- ❌ No horizontal scale-out (single worker per deployment)
- ❌ If process crashes, must restart from last saved progress checkpoint

## Tradeoffs

| Pros | Cons |
|------|------|
| **Simplicity** - No orchestration framework, just C# loops | **Single point of failure** - If background service dies, job stalls |
| **Low overhead** - No DurableTask state persistence | **No built-in pause/resume** - Must implement manually with status checks |
| **Easy to debug** - Standard breakpoints, no replay semantics | **No parallelism across workers** - Can't split job across multiple instances |
| **No external dependencies** - Just SQL and existing ISearchService | **Manual progress tracking** - Must persist checkpoints to survive restarts |
| **Familiar pattern** - Standard .NET BackgroundService | **No automatic retry** - Must implement Polly policies manually |
| **Fast for small datasets** (<1M resources) - No orchestration overhead | **Restart from scratch on crash** - Unless checkpoint logic very robust |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
  - API: ReindexEndpoints.cs (POST /admin/reindex, GET /admin/reindex/{id}, DELETE /admin/reindex/{id})
  - Application: ReindexBackgroundService, ReindexJobProcessor
  - DataLayer: BulkUpdateSearchParameterIndicesAsync

- [x] F5 Developer Experience (works with minimal setup)
  - Background service starts automatically with API
  - No additional configuration required

- [x] FHIR spec compliance (if applicable)
  - N/A

- [x] Consistent with existing patterns
  - ⚠️ **No existing background service pattern in codebase** - Export/Import use DurableTask
  - Introduces new architectural pattern

## Evidence

### Similar Patterns in .NET Ecosystem

**Hangfire** (job queue library):
- Uses SQL Server for job storage
- Background server polls for jobs
- Supports retries, continuations, batches
- **Downside**: External dependency, heavier than needed for single-job reindex

**Quartz.NET** (scheduler):
- Persistent job store in SQL
- Distributed execution with clustering
- **Downside**: Overkill for ad-hoc reindex operations

**Plain BackgroundService** (used in many .NET projects):
- Standard IHostedService implementation
- Simple while() loop with CancellationToken
- **Reference**: ASP.NET Core documentation recommends for long-running tasks

### Checkpoint/Resume Implementation

```csharp
public class ReindexCheckpoint
{
    public string CurrentResourceType { get; set; }
    public long LastProcessedSurrogateId { get; set; }
    public long ProcessedCount { get; set; }
}

// On crash/restart:
var checkpoint = await LoadCheckpointAsync(jobId);
if (checkpoint != null)
{
    // Resume from last checkpoint
    currentId = checkpoint.LastProcessedSurrogateId + 1;
    processedCount = checkpoint.ProcessedCount;
}
```

**Problem**: What if SQL update succeeds but checkpoint write fails? Risk of processing resources twice (idempotent via SearchParameterHash, but wasteful).

## Verdict

**Not recommended** for Ignixa's scale requirements:

1. **No horizontal scale-out** - Cannot split 1B Patient reindex across multiple workers without complex distributed locking
2. **Poor fault tolerance** - Crash recovery requires manual checkpoint logic (error-prone)
3. **Inconsistent with existing patterns** - Export/Import use DurableTask; introducing BackgroundService creates architectural divergence
4. **Limited pause/resume** - Must poll status flag in tight loop (inefficient)

**Best use case**: Small deployments (<10M resources) where simplicity > resilience.

**For 1B+ resources**: DurableTask's built-in partitioning, fault tolerance, and pause/resume make it superior.
