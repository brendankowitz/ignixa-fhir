# Investigation: SQL Queue with State Machine

**Feature**: reindex
**Status**: In Progress
**Created**: 2025-12-19

## Approach

Use a **SQL-backed queue** (similar to JobManagement from src-old) with explicit state machine transitions to manage reindex jobs. No DurableTask framework - just SQL tables, stored procedures, and application logic.

### Architecture Overview

**Two-Table Queue Pattern**:

1. **ReindexOrchestrator Table** (parent job)
   ```sql
   CREATE TABLE ReindexOrchestrator (
       JobId BIGINT IDENTITY PRIMARY KEY,
       GroupId BIGINT NOT NULL,  -- Links related jobs
       Status TINYINT NOT NULL,  -- 0=Created, 1=Running, 2=Completed, 3=Failed, 4=Cancelled
       TenantId INT NOT NULL,
       Definition NVARCHAR(MAX) NOT NULL,  -- JSON: search params, resource types
       Result NVARCHAR(MAX),  -- JSON: progress, errors
       HeartbeatDateTime DATETIME2,
       Version BIGINT,  -- For optimistic concurrency
       ROWVERSION
   )
   ```

2. **ReindexWorker Table** (child jobs, one per surrogate ID range)
   ```sql
   CREATE TABLE ReindexWorker (
       JobId BIGINT IDENTITY PRIMARY KEY,
       GroupId BIGINT NOT NULL,  -- References parent orchestrator
       Status TINYINT NOT NULL,
       ResourceType NVARCHAR(50) NOT NULL,
       StartSurrogateId BIGINT NOT NULL,
       EndSurrogateId BIGINT NOT NULL,
       Definition NVARCHAR(MAX),
       Result NVARCHAR(MAX),
       Version BIGINT,
       ROWVERSION
   )
   ```

3. **Processing Flow**:
   - **Step 1**: Create orchestrator job (POST /admin/reindex)
   - **Step 2**: Orchestrator spawns worker jobs (one per resource type * range)
   - **Step 3**: Multiple background workers poll `ReindexWorker` table, dequeue jobs
   - **Step 4**: Workers process their range, update status
   - **Step 5**: Orchestrator polls workers, marks itself complete when all done

### Scalability for 1M-1B Rows

**Work Distribution**:
```csharp
// Orchestrator creates N worker jobs per resource type
foreach (var resourceType in targetResourceTypes)
{
    var ranges = await GetExportRangesAsync(resourceType, numberOfRanges: 16);

    foreach (var (startId, endId) in ranges)
    {
        await EnqueueWorkerJobAsync(new WorkerJobDefinition
        {
            GroupId = orchestratorJobId,
            ResourceType = resourceType,
            StartSurrogateId = startId,
            EndSurrogateId = endId
        });
    }
}

// Multiple worker processes poll and dequeue
var job = await DequeueJobAsync(queueType: "ReindexWorker");
if (job != null)
{
    await ProcessWorkerJobAsync(job);
    await CompleteJobAsync(job.JobId);
}
```

**Stored Procedures** (from src-old pattern):
```sql
-- Dequeue with optimistic concurrency
CREATE PROCEDURE dbo.DequeueReindexJob
    @worker NVARCHAR(100),
    @heartbeatTimeoutSec INT
AS
BEGIN
    UPDATE TOP(1) ReindexWorker WITH (UPDLOCK, READPAST)
    SET Status = 1,  -- Running
        Worker = @worker,
        HeartbeatDateTime = GETUTCDATE(),
        Version = Version + 1
    WHERE Status = 0  -- Created
       OR (Status = 1 AND DATEDIFF(SECOND, HeartbeatDateTime, GETUTCDATE()) > @heartbeatTimeoutSec)
    OUTPUT INSERTED.*;
END
```

**Why this scales**:
- ✅ Horizontal scale-out - Deploy N worker instances, each polls queue independently
- ✅ Built-in retry - Stale heartbeats automatically re-queued
- ✅ Transactional guarantees - SQL ensures exactly-once dequeue via UPDLOCK/READPAST
- ⚠️ **Polling overhead** - Workers must poll SQL continuously (vs. DurableTask's push model)

## Tradeoffs

| Pros | Cons |
|------|------|
| **Horizontal scalability** - Add more workers, they auto-discover work | **Polling overhead** - Each worker queries SQL every 100ms-1s (DB load) |
| **Proven pattern** - Microsoft FHIR Server used this for years | **Manual orchestration** - Must implement parent/child job coordination logic |
| **Transactional safety** - SQL guarantees at-most-once dequeue | **No built-in pause/resume** - Must implement via status flag checks |
| **Simple debugging** - Query SQL to see job state | **State machine complexity** - Must handle all status transitions manually |
| **No external dependencies** - Just SQL Server | **Heartbeat monitoring required** - Background thread to detect stale jobs |
| **Retry built-in** - Heartbeat timeout automatically retries | **No orchestration replay** - Cannot reconstruct what happened (vs. DurableTask history) |
| **Resume from checkpoint** - Each worker job is independently resumable | **SQL table growth** - ReindexWorker can have millions of rows (index pressure) |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
  - API: ReindexEndpoints.cs
  - Application: ReindexOrchestratorService, ReindexWorkerService
  - DataLayer: SqlReindexQueue (stored procedures)

- [x] F5 Developer Experience (works with minimal setup)
  - Requires SQL schema migration (new tables + sprocs)
  - Must run separate worker process or BackgroundService

- [x] FHIR spec compliance (if applicable)
  - N/A

- [ ] Consistent with existing patterns
  - ❌ **Export/Import use DurableTask** - This introduces JobManagement pattern (deprecated in favor of DurableTask)
  - ✅ SQL queue used successfully in src-old

## Evidence

### src-old JobManagement Implementation

From src-old/Microsoft.Health.TaskManagement/IQueueClient.cs:
```csharp
// Queue operations
Task<IReadOnlyList<JobInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, CancellationToken cancellationToken);
Task<JobInfo> DequeueAsync(byte queueType, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null, bool checkTimeoutJobsOnly = false);
Task<bool> PutJobHeartbeatAsync(JobInfo jobInfo, CancellationToken cancellationToken);
Task CompleteJobAsync(JobInfo jobInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken);
Task CancelJobByGroupIdAsync(byte queueType, long groupId, CancellationToken cancellationToken);
```

**Production Evidence**:
- Microsoft FHIR Server used this from 2019-2023
- Handled billions of resources across multi-tenant deployments
- **Why replaced**: DurableTask Framework offers better observability, built-in retries, and orchestration replay

### Pause/Resume Implementation

**Pause** (manual):
```csharp
// Set all jobs in group to status "Paused"
await CancelJobByGroupIdAsync(queueType, groupId, cancellationToken);
// Workers detect Paused status and stop processing
```

**Resume** (manual):
```csharp
// Find incomplete ranges for the job
var incompleteRanges = await GetIncompleteRangesAsync(jobId);
// Re-enqueue worker jobs for incomplete ranges
foreach (var range in incompleteRanges)
{
    await EnqueueWorkerJobAsync(range);
}
```

**Problem**: No atomic "pause in place" - workers may complete current batch before detecting pause.

## Verdict

**Acceptable but not ideal**:

1. **Horizontal scale-out** ✅ - Multiple workers can process job concurrently
2. **Proven at scale** ✅ - Microsoft FHIR Server validated this for billions of resources
3. **Manual orchestration** ❌ - Requires significant custom code vs. DurableTask
4. **Polling overhead** ❌ - Continuous SQL queries add DB load
5. **Architectural mismatch** ❌ - Export/Import already use DurableTask; reindex should too

**Best use case**: If DurableTask Framework were unavailable or unsuitable (e.g., Cosmos DB backend where DTFx SQL backend doesn't work).

**For Ignixa**: DurableTask is already integrated and proven - no reason to reinvent with SQL queue.

**Recommendation**: Only use this if DurableTask has a critical blocker (none identified).
