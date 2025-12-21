# Reindex Implementation: Design Comparison

**Feature**: Reindex
**Date**: 2025-12-19

## Three Competing Designs

### Design 1: DurableTask Orchestration
**Pattern**: Orchestration framework with parallel worker activities
**Complexity**: Medium
**Reference**: [durable-task-orchestration.md](investigations/durable-task-orchestration.md)

### Design 2: Simple Background Service
**Pattern**: Single BackgroundService polling SQL, processing sequentially
**Complexity**: Low
**Reference**: [background-service-simple.md](investigations/background-service-simple.md)

### Design 3: SQL Queue with State Machine
**Pattern**: Custom SQL queue (src-old JobManagement pattern), manual orchestration
**Complexity**: High
**Reference**: [sql-queue-state-machine.md](investigations/sql-queue-state-machine.md)

---

## Comparison Matrix

| Criterion | DurableTask | Background Service | SQL Queue |
|-----------|-------------|-------------------|-----------|
| **Scale (1M rows)** | ⭐⭐⭐ Excellent | ⭐⭐⭐ Good | ⭐⭐⭐ Excellent |
| **Scale (100M rows)** | ⭐⭐⭐ Excellent | ⭐⭐ Acceptable | ⭐⭐⭐ Excellent |
| **Scale (1B rows)** | ⭐⭐⭐ Excellent | ⭐ Poor | ⭐⭐⭐ Excellent |
| **Horizontal Scale-Out** | ✅ Built-in | ❌ Single worker | ✅ Manual (N workers) |
| **Pause/Resume** | ✅ `SuspendAsync`/`ResumeAsync` | ⚠️ Manual status checks | ⚠️ Cancel + Re-enqueue |
| **Cancel** | ✅ `TerminateInstanceAsync` | ⚠️ Set flag, wait for check | ✅ `CancelJobByGroupId` |
| **Fault Tolerance** | ✅ Auto-retry activities | ❌ Manual checkpointing | ✅ Heartbeat timeout retry |
| **Progress Monitoring** | ✅ Query orchestration state | ⚠️ Poll SQL table | ✅ Query job table |
| **Parallelism (same job)** | ✅ 24-48 workers (DurableTask pool) | ❌ Single-threaded | ✅ N workers dequeue chunks |
| **Code Complexity** | ⭐⭐ Medium (orchestration replay semantics) | ⭐ Low (simple loop) | ⭐⭐⭐ High (state machine logic) |
| **Existing Pattern Match** | ✅ Export/Import use DurableTask | ❌ No BackgroundService examples | ⚠️ src-old used this (deprecated) |
| **External Dependencies** | ✅ DurableTask.SqlServer (already installed) | ✅ None (just SQL) | ✅ None (just SQL) |
| **Observability** | ⭐⭐⭐ Rich (orchestration history) | ⭐ Basic (logs only) | ⭐⭐ Good (SQL queries) |
| **Retry Strategy** | ✅ Built-in (activity retries) | ⚠️ Manual Polly policies | ✅ Built-in (heartbeat timeout) |
| **Atomic Operations** | ✅ Activity = transaction boundary | ⚠️ Manual transaction scoping | ✅ SQL sproc guarantees |
| **Resume After Crash** | ✅ Replay from last activity | ❌ Manual checkpoint logic | ✅ Re-dequeue stale jobs |
| **Development Time** | ⭐⭐ 2-3 weeks (orchestration + activities) | ⭐ 1 week (simple loop) | ⭐⭐⭐ 3-4 weeks (SQL sprocs + logic) |

---

## Deep Dive: Key Differentiators

### 1. Scalability (1B Resources)

**DurableTask**:
```
1B Patient resources, 16 ranges/type
→ 16 worker activities process in parallel
→ Each worker handles 62.5M resources
→ Batches of 100 resources/query, 1000/write
→ Estimated time: ~10-12 hours (at 10K resources/sec throughput)
```

**Background Service**:
```
1B Patient resources, single worker
→ Processes sequentially in batches
→ No parallelism within job
→ Estimated time: ~28 hours (at 10K resources/sec, but single-threaded)
BLOCKER: Crash after 5 hours = restart from scratch (unless complex checkpointing)
```

**SQL Queue**:
```
1B Patient resources, deploy 8 worker instances
→ Queue contains ~128 jobs (16 ranges/type × 8 resource types)
→ Each worker dequeues and processes independently
→ Estimated time: ~10-12 hours (similar to DurableTask)
```

**Winner**: **DurableTask** (tied with SQL Queue, but simpler to implement)

### 2. Pause/Resume Semantics

**DurableTask**:
```csharp
// Pause (immediate):
await taskHubClient.SuspendAsync(instanceId);
// Orchestration stops scheduling new activities
// Current activities complete, then suspend

// Resume (immediate):
await taskHubClient.ResumeAsync(instanceId);
// Orchestration continues from suspension point
```

**Background Service**:
```csharp
// Pause (delayed):
await UpdateJobStatusAsync(jobId, "Paused");
// Worker checks status in tight loop (every 100 resources?)
while (await GetResourceBatchAsync())
{
    if (await IsJobPausedAsync(jobId)) break;  // Adds latency
    await ProcessBatch();
}
// Pause not immediate - completes current batch
```

**SQL Queue**:
```csharp
// Pause (cancel workers):
await CancelJobByGroupIdAsync(queueType, groupId);
// Workers detect cancellation, stop processing
// Resume = re-enqueue incomplete ranges (complex query)
```

**Winner**: **DurableTask** (built-in, immediate, no polling)

### 3. Fault Tolerance

**DurableTask**:
- Activity failure → automatic retry (configurable policy)
- Worker crash → another worker picks up from last completed activity
- SQL persistence → orchestration state survives process restarts
- **Example**: Worker crashes after processing 50M resources → Replay from activity 50M, not 0

**Background Service**:
- No automatic retry → must implement Polly manually
- Crash → restart from last checkpoint (if implemented correctly)
- **Risk**: Checkpoint write fails but DB update succeeds → data inconsistency

**SQL Queue**:
- Heartbeat timeout → job automatically re-queued
- Worker crash → another worker dequeues stale job
- **Downside**: Must implement heartbeat background thread

**Winner**: **DurableTask** (least manual code, most resilient)

### 4. Code Maintainability

**DurableTask** (lines of code estimate):
```
ReindexOrchestration.cs: ~200 lines
GetReindexRangesActivity.cs: ~50 lines
ReindexWorkerActivity.cs: ~150 lines
UpdateSearchParameterStatusActivity.cs: ~100 lines
ReindexEndpoints.cs: ~150 lines
---
Total: ~650 lines (similar to ExportOrchestration pattern)
```

**Background Service** (lines of code estimate):
```
ReindexBackgroundService.cs: ~400 lines (polling, processing, checkpointing)
ReindexJobProcessor.cs: ~300 lines (batch logic)
ReindexEndpoints.cs: ~150 lines
---
Total: ~850 lines + complex checkpoint logic
```

**SQL Queue** (lines of code estimate):
```
SQL sprocs (DequeueJob, CompleteJob, CancelJob, etc.): ~400 lines SQL
ReindexOrchestratorService.cs: ~500 lines (parent job coordination)
ReindexWorkerService.cs: ~300 lines (worker logic)
SqlReindexQueue.cs: ~400 lines (ADO.NET boilerplate)
ReindexEndpoints.cs: ~150 lines
---
Total: ~1750 lines (most complex, most SQL)
```

**Winner**: **DurableTask** (least code, leverages framework)

### 5. Consistency with Existing Patterns

**Current Ignixa Background Operations**:
- ✅ **Export** → DurableTask (ExportOrchestration + workers)
- ✅ **Import** → DurableTask (ImportOrchestration + workers)
- ✅ **Terminology Import** → DurableTask (TerminologyImportOrchestration)

**Introducing Reindex**:
- **DurableTask** → ✅ Consistent, same pattern
- **Background Service** → ❌ New pattern, diverges from export/import
- **SQL Queue** → ❌ src-old pattern (replaced by DurableTask in migration)

**Winner**: **DurableTask** (architectural consistency)

---

## Decision Matrix

| Design | 1M Rows | 100M Rows | 1B Rows | Pause/Resume | Fault Tolerance | Code Complexity | Pattern Match | **TOTAL** |
|--------|---------|-----------|---------|--------------|-----------------|-----------------|---------------|-----------|
| **DurableTask** | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | **20/21** |
| **Background Service** | ⭐⭐⭐ | ⭐⭐ | ⭐ | ⭐ | ⭐ | ⭐⭐⭐ | ⭐ | **12/21** |
| **SQL Queue** | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐ | ⭐⭐ | **17/21** |

---

## Winner: DurableTask Orchestration

### Why DurableTask Wins

1. **Proven at scale** - Export already achieves >10K resources/sec using identical pattern
2. **Built-in pause/resume/cancel** - No custom implementation needed
3. **Fault-tolerant by design** - Automatic retries, orchestration replay
4. **Architectural consistency** - Matches Export/Import patterns
5. **Lowest code complexity** - Framework handles orchestration, we write business logic
6. **Best observability** - DurableTask history provides audit trail
7. **Already integrated** - No new dependencies, SQL backend configured

### When to Use Alternatives

**Background Service**:
- Small deployments (<1M resources)
- Simplicity > resilience
- No multi-tenant scale requirements

**SQL Queue**:
- DurableTask unavailable (e.g., Cosmos DB backend, though DTFx supports Azure Storage backend)
- Need complete control over queue mechanics
- Already have custom queue infrastructure

### Recommendation

**Implement DurableTask Orchestration** as designed in [durable-task-orchestration.md](investigations/durable-task-orchestration.md).

**Next Steps**:
1. Create ADR documenting this decision
2. Implement proof-of-concept for single resource type
3. Performance test with 10M Patient resources
4. Full implementation with all 3 FHIR resource types
5. Integration tests for pause/resume/cancel

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| DurableTask SQL backend performance degrades at 1B rows | Low | High | Use same `GetExportRangesAsync` optimization as Export (proven) |
| Orchestration instance state size exceeds limits | Low | Medium | Use continuation pattern if single range too large |
| Team unfamiliar with DurableTask replay semantics | Medium | Low | Training session + code review guidelines |
| Pause doesn't stop immediately (completes current activity) | High | Low | Design activities to be <5 min duration (100-1000 resources) |
| Multiple reindex jobs conflict (same search parameter) | Medium | Medium | Enforce singleton orchestration per tenant (check before start) |

**Overall Risk**: **Low** - DurableTask is proven technology, Export/Import validate the pattern.
