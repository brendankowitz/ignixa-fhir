# Investigation: Event-Driven Reindex Triggering

**Feature**: reindex
**Status**: In Progress
**Created**: 2025-12-23

## Approach

Leverage the event-sourced conformance system (`SourceEvents` table) to trigger reindex jobs with surgical precision. When a package is activated, `SearchParameterActivated` events are emitted - each contains the exact `ResourceType` and `Code` affected. The reindex system listens for these events and knows exactly which resources need reindexing.

### How It Works

```
Package Activation Flow:
┌─────────────────────┐
│ Install US Core 6.1 │
└──────────┬──────────┘
           │
           ▼
┌──────────────────────────────────────────┐
│ PackageActivationPipeline.ActivateAsync  │
│                                          │
│  Emits events:                           │
│  - SearchParameterActivated (Patient, race)      [Status: Pending]
│  - SearchParameterActivated (Patient, ethnicity) [Status: Pending]
│  - SearchParameterActivated (Condition, asserted-date) [Status: Pending]
│  - PackageActivated (hl7.fhir.us.core@6.1.0)     │
└──────────┬───────────────────────────────┘
           │
           ▼
┌──────────────────────────────────────────┐
│ ReindexEventHandler (Medino Handler)     │
│                                          │
│  Groups by ResourceType:                 │
│  - Patient: [race, ethnicity]            │
│  - Condition: [asserted-date]            │
│                                          │
│  Creates DurableTask jobs:               │
│  - ReindexOrchestration(Patient, [race, ethnicity])
│  - ReindexOrchestration(Condition, [asserted-date])
└──────────┬───────────────────────────────┘
           │
           ▼
┌──────────────────────────────────────────┐
│ ReindexOrchestration (DurableTask)       │
│                                          │
│  1. Emit SearchParameterReindexStarted   │  [Status: Reindexing]
│  2. Partition by SurrogateId ranges      │
│  3. Fan-out to ReindexWorker activities  │
│  4. For each Patient:                    │
│     - Re-extract race, ethnicity values  │
│     - Update SearchIndexEntry rows       │
│  5. Emit SearchParameterReindexCompleted │  [Status: Enabled]
└──────────────────────────────────────────┘
```

### Key Data Available from Events

The `SearchParameterActivated` event contains everything needed:

```csharp
public record SearchParameterActivated(
    string Canonical,           // URL to identify the SP
    string Code,                // "race", "ethnicity"
    string ResourceType,        // "Patient", "Condition"
    string Expression,          // FHIRPath for extraction
    SearchParamType ParamType,  // token, date, reference, etc.
    string SourcePackage,       // "hl7.fhir.us.core@6.1.0"
    OverrideInfo? Overrides,    // If overriding another SP
    int SearchParamId,          // ID for SearchIndexEntry.SearchParamId
    IReadOnlyList<string>? TargetResourceTypes,  // For reference SPs
    IReadOnlyList<SearchParameterComponentData>? Components,  // For composite SPs
    string? Name,
    string? Description);
```

### Precision Indexing

Unlike "reindex everything", we can:

1. **Know exact SearchParameters** - Only index `race` and `ethnicity`, not all 50+ Patient SPs
2. **Know exact resource types** - Only scan Patient and Condition, not all 149 types
3. **Skip base FHIR** - Base FHIR SPs are pre-indexed at resource creation, status is `Enabled` from the start
4. **Track progress per-SP** - `SearchParameterReindexStarted/Completed/Failed` events
5. **Know exact cutoff time** - Only resources created BEFORE activation need reindexing

### Transaction-Based Precision: Deterministic Cutoff

Time-based cutoffs are unreliable (clock drift between servers, DB time mismatch, in-flight transactions). Instead, capture the **TransactionId** at activation time for a deterministic sequence boundary.

```
Transaction Sequence:
───────────────────────────────────────────────────────────────────────────────►
     │                              │                              │
     │  Patient A created           │  SearchParameterActivated    │  Patient B created
     │  TransactionId: 1000         │  TransactionId: 1050         │  TransactionId: 1051
     │  (no 'race' indexed)         │  (hl7.fhir.us.core:race)     │  (has 'race' indexed)
     │                              │                              │
     ▼                              ▼                              ▼
┌─────────────────────┐      ┌─────────────────────┐      ┌─────────────────────┐
│ Patient A           │      │ SourceEvent:        │      │ Patient B           │
│ - TransactionId 1000│      │ - EventId: 167      │      │ - TransactionId 1051│
│ - NEEDS REINDEX     │      │ - TransactionId:1050│      │ - ALREADY INDEXED   │
└─────────────────────┘      └─────────────────────┘      └─────────────────────┘
```

**Schema change** - Add TransactionId to SourceEvents:

```sql
ALTER TABLE SourceEvents ADD TransactionId BIGINT NOT NULL DEFAULT 0;

-- Captured at event append time from current transaction sequence
CREATE INDEX IX_SourceEvents_TransactionId ON SourceEvents(TransactionId);
```

**At activation time**, the `PackageActivationPipeline` captures the current max TransactionId:

```csharp
public async Task<ActivationResult> ActivateAsync(...)
{
    // Get current transaction high-water mark BEFORE appending events
    var activationTransactionId = await _transactionStore.GetCurrentTransactionIdAsync(ct);

    // ... build events ...

    // Events are stored with this TransactionId
    await _eventStore.AppendAsync(events, activationTransactionId, ct);
}
```

**Reindex query** - Deterministic, no clock issues:

```sql
SELECT * FROM Resource
WHERE ResourceTypeId = @PatientTypeId
  AND TransactionId <= @ActivationTransactionId  -- Deterministic cutoff
ORDER BY SurrogateId
```

**Why TransactionId beats timestamps**:
- **Deterministic** - Same query, same results, regardless of when you run it
- **No clock drift** - TransactionId is database-generated sequence
- **Handles in-flight transactions** - Resources committed after activation have higher TransactionId
- **Already exists** - Resource table uses TransactionId for versioning/change tracking
- **Consistent across servers** - All app servers see same DB sequence

**Benefits**:
- Reindex scope is exactly: resources with `TransactionId <= ActivationTransactionId`
- Fresh server install: `ActivationTransactionId = 0` → scope = 0 resources
- Existing server: scope = all resources created before package activation
- No edge cases from concurrent writes during activation

### Status State Machine

The `ConformanceState` already tracks this via events:

```
SearchParameterActivated (non-base-FHIR) → Status: Pending
    ↓
SearchParameterReindexStarted            → Status: Reindexing
    ↓
SearchParameterReindexCompleted          → Status: Enabled (SEARCHABLE)
    or
SearchParameterReindexFailed             → Status: Pending (retry)
```

**Query behavior**: Only `Enabled` SearchParameters are used in searches. `Pending` and `Reindexing` SPs are invisible to search until indexed.

### Implementation Sketch

```csharp
// Handler triggered by package activation completion
public class ReindexTriggerHandler : INotificationHandler<PackageActivatedNotification>
{
    private readonly ConformanceState _state;
    private readonly TaskHubClient _taskHub;

    public async Task HandleAsync(PackageActivatedNotification notification, CancellationToken ct)
    {
        // Find all Pending SearchParameters from this package
        var pendingSPs = _state.AllSearchParameters.Values
            .Where(sp => sp.SourcePackage == notification.PackageKey
                      && sp.Status == SearchParameterStatus.Pending)
            .GroupBy(sp => sp.ResourceType)
            .ToList();

        if (pendingSPs.Count == 0)
            return; // No reindex needed (base FHIR or all SPs were updates)

        // Create one orchestration per resource type
        foreach (var group in pendingSPs)
        {
            var resourceType = group.Key;
            var searchParams = group.Select(sp => new ReindexTarget(
                sp.Canonical,
                sp.Code,
                sp.SearchParamId,
                sp.Expression,
                sp.ParamType
            )).ToList();

            var instanceId = $"reindex-{resourceType}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var input = new ReindexOrchestrationInput(resourceType, searchParams);

            await _taskHub.ScheduleNewOrchestrationInstanceAsync(
                nameof(ReindexOrchestration),
                input,
                new StartOrchestrationOptions { InstanceId = instanceId },
                ct);
        }
    }
}

// DurableTask Orchestration (same pattern as Export)
[DurableTask(nameof(ReindexOrchestration))]
public class ReindexOrchestration : TaskOrchestrator<ReindexOrchestrationInput, ReindexResult>
{
    public override async Task<ReindexResult> RunAsync(TaskOrchestrationContext ctx, ReindexOrchestrationInput input)
    {
        // 1. Emit SearchParameterReindexStarted for each SP
        await ctx.CallActivityAsync(nameof(EmitReindexStartedActivity),
            new EmitReindexStartedInput(input.ResourceType, input.SearchParams));

        // 2. Partition by SurrogateId ranges (same as Export)
        var partitions = await ctx.CallActivityAsync<List<SurrogateIdRange>>(
            nameof(ComputePartitionsActivity),
            new ComputePartitionsInput(input.ResourceType, batchSize: 1000));

        // 3. Fan-out to workers
        var workerTasks = partitions.Select(p =>
            ctx.CallActivityAsync<int>(nameof(ReindexWorkerActivity),
                new ReindexWorkerInput(input.ResourceType, input.SearchParams, p)));

        var results = await Task.WhenAll(workerTasks);
        var totalIndexed = results.Sum();

        // 4. Emit SearchParameterReindexCompleted
        await ctx.CallActivityAsync(nameof(EmitReindexCompletedActivity),
            new EmitReindexCompletedInput(input.ResourceType, input.SearchParams, totalIndexed));

        return new ReindexResult(totalIndexed);
    }
}

// Worker activity - indexes one partition
[DurableTask(nameof(ReindexWorkerActivity))]
public class ReindexWorkerActivity : TaskActivity<ReindexWorkerInput, int>
{
    private readonly ISearchIndexer _indexer;
    private readonly IResourceRepository _resourceRepo;

    public override async Task<int> RunAsync(TaskActivityContext ctx, ReindexWorkerInput input)
    {
        var count = 0;

        await foreach (var resource in _resourceRepo.GetByTypeAndRangeAsync(
            input.ResourceType,
            input.Range.StartSurrogateId,
            input.Range.EndSurrogateId))
        {
            // Extract only the target SearchParameters
            var entries = _indexer.ExtractSpecificParameters(
                resource,
                input.SearchParams);

            // Upsert into SearchIndexEntry
            await _indexRepo.UpsertAsync(resource.SurrogateId, entries);
            count++;
        }

        return count;
    }
}
```

## Tradeoffs

| Pros | Cons |
|------|------|
| **Surgical precision** - Only reindex affected SPs, not everything | Requires event-sourced conformance (now implemented) |
| **Automatic triggering** - No manual "run reindex" needed | DurableTask dependency (already in use) |
| **Full audit trail** - Events show exactly when each SP was indexed | More events in SourceEvents table |
| **Status visibility** - ConformanceState shows Pending/Reindexing/Enabled | Complexity in tracking multi-SP jobs |
| **Consistent with architecture** - Uses existing event patterns | Need to handle partial failures gracefully |
| **Deterministic state** - Replay events → same reindex status | |
| **No guessing** - Events contain Expression, ParamType, SearchParamId | |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
- [x] F5 Developer Experience (works with minimal setup)
- [x] FHIR spec compliance (if applicable)
- [x] Consistent with existing patterns (DurableTask, Event Sourcing)

## Evidence

### Current Implementation

1. **Events already defined** in `SearchParameterEvents.cs`:
   - `SearchParameterReindexStarted(Canonical, Code, ResourceType, JobId, AffectedResourceTypes)`
   - `SearchParameterReindexCompleted(Canonical, Code, ResourceType, JobId, ResourcesIndexed, Duration)`
   - `SearchParameterReindexFailed(Canonical, Code, ResourceType, JobId, ErrorMessage)`

2. **ConformanceState already handles** these events (lines 170-180, 246-270):
   ```csharp
   case SearchParameterReindexStarted reindex:
       sp.Status = SearchParameterStatus.Reindexing;
       sp.ReindexJobId = reindex.JobId;
       break;

   case SearchParameterReindexCompleted completed:
       sp.Status = SearchParameterStatus.Enabled;
       sp.ReindexJobId = null;
       break;
   ```

3. **PackageActivationPipeline** already returns `PendingReindex` list:
   ```csharp
   var reindexNeeded = DetectReindexRequirements(resources);
   return ActivationResult.Succeeded(reindexNeeded);
   ```

4. **SearchParameterStatus enum** already exists:
   - `Pending` - Activated but not indexed (NOT searchable)
   - `Reindexing` - Job in progress
   - `Enabled` - Fully indexed (SEARCHABLE)
   - `Disabled` - Deactivated

### Prior Art

- **Microsoft FHIR Server** uses background jobs triggered by SearchParameter POST/PUT
- **HAPI FHIR** has `$reindex` operation with optional SearchParameter filtering
- **Firely Server** supports incremental reindex per SearchParameter

### DurableTask Integration

Already proven in Export feature:
- `ExportOrchestration` partitions by SurrogateId
- `ExportWorkerActivity` processes batches
- Built-in pause/resume/cancel
- Fault tolerance via checkpointing

## Alternative Approaches

1. **Polling-based** - Background service polls ConformanceState for Pending SPs
   - Simpler but less responsive, polling overhead

2. **Manual trigger only** - Admin explicitly runs reindex
   - Current default, requires human intervention

3. **Hybrid** - Auto-trigger for small packages, manual for large
   - More complexity in decision logic

## Open Questions

1. **Batch size tuning** - How many resources per worker activity? (Start with 1000, tune based on metrics)

2. **Concurrent reindex limit** - Should we limit parallel orchestrations? (Probably yes, 2-4 concurrent)

3. **Failure handling** - Retry individual partitions or entire job? (Individual partitions preferred)

4. **Progress reporting** - How to expose reindex progress? (Query DurableTask + ConformanceState)

## Future Enhancement: Zero-Downtime Rollforward

The current approach (Pending → not searchable → Enabled) has a gap where the SearchParameter isn't usable during reindex. For production systems requiring 24/7 availability, we can implement **zero-downtime rollforward** using the event stream.

### The Pattern

When a new SearchParameter version overrides an old one (e.g., US Core `race` overriding base FHIR `race`):

1. **Keep using OLD definition for searches** - It's already indexed, works fine
2. **Index NEW resources with NEW definition** - They get the new SP automatically
3. **Background reindex old resources** - Add new index entries alongside old
4. **Atomic switchover** - Once complete, use new definition for all searches
5. **Cleanup** - Remove old index entries if expression differs

```
Timeline:
─────────────────────────────────────────────────────────────────────────────────►
│                                      │                               │
│ Old SP (base FHIR race)              │ New SP activated              │ Reindex complete
│ Status: Enabled                      │ (US Core race)                │
│ SearchParamId: 42                    │                               │
│                                      │                               │
▼                                      ▼                               ▼
┌──────────────────────────────────────┬───────────────────────────────┬─────────┐
│ Searches use OLD SP (id=42)          │ OLD for existing resources    │ NEW SP  │
│ All resources indexed with OLD       │ NEW for new resources         │ (id=42) │
│                                      │ Background: reindex old→NEW   │ Enabled │
└──────────────────────────────────────┴───────────────────────────────┴─────────┘
```

### Query Routing During Transition

Since we have `TransactionId` on both events AND resources, we can route queries:

```sql
-- During transition, search query becomes:
SELECT r.* FROM Resource r
JOIN SearchIndexEntry s ON r.SurrogateId = s.SurrogateId
WHERE s.SearchParamId = CASE
    WHEN r.TransactionId <= @ActivationTransactionId THEN @OldSearchParamId  -- Old resources, old index
    WHEN r.TransactionId > @ActivationTransactionId THEN @NewSearchParamId   -- New resources, new index
    END
  AND s.TokenValue = @SearchValue
```

### Extended State Machine

```
                    ┌─────────────────────────────────────────┐
                    │                                         │
                    ▼                                         │
┌──────────┐   Activated   ┌────────────┐  ReindexStarted  ┌────────────┐
│ (none)   │ ─────────────►│ Shadowing  │ ────────────────►│ Migrating  │
└──────────┘               └────────────┘                  └────────────┘
                                 │                               │
                                 │ (same expression -            │ ReindexCompleted
                                 │  inherit old index)           │
                                 │                               ▼
                                 │                        ┌────────────┐
                                 └───────────────────────►│  Enabled   │
                                                          └────────────┘
                                                                │
                                                     CleanupCompleted
                                                                │
                                                                ▼
                                                          ┌────────────┐
                                                          │  Active    │
                                                          │ (old index │
                                                          │  removed)  │
                                                          └────────────┘
```

**States:**
- `Shadowing` - New SP activated, old SP still used for searches, new resources indexed with new definition
- `Migrating` - Background reindex in progress, query routing active
- `Enabled` - Reindex complete, new SP used for all searches, old index entries still exist
- `Active` - Cleanup complete, old index entries removed

### Expression Compatibility Matrix

| Scenario | Old Expression | New Expression | Action |
|----------|---------------|----------------|--------|
| Identical | `Patient.race` | `Patient.race` | Inherit old index, immediate Enabled |
| Compatible | `Patient.identifier` | `Patient.identifier` | Same values, inherit index |
| Different | `Patient.race` | `Patient.extension.where(url='...').value` | Shadow → Migrate → Cleanup |

### New Events Required

```csharp
public record SearchParameterShadowing(
    string Canonical,
    string Code,
    string ResourceType,
    string ShadowsCanonical,      // Old SP being shadowed
    int ShadowsSearchParamId,     // Old SearchParamId for query routing
    long ActivationTransactionId); // Cutoff for query routing

public record SearchParameterMigrationCompleted(
    string Canonical,
    string Code,
    string ResourceType,
    long ResourcesMigrated);

public record SearchParameterCleanupCompleted(
    string Canonical,
    string Code,
    string ResourceType,
    long IndexEntriesRemoved);
```

### Why This Works

1. **TransactionId on events** - Know exact cutoff for query routing
2. **TransactionId on resources** - Know which resources use which index
3. **Event stream is immutable** - Can always reconstruct state
4. **Rollback = keep using old** - If migration fails, old SP still works

### Tradeoffs

| Pro | Con |
|-----|-----|
| Zero search downtime | Query routing complexity |
| Safer rollout | Dual storage during transition |
| Rollback = just keep using old | More state machine states |
| Matches prod DB patterns (pt-osc, pg CONCURRENTLY) | More events to handle |

### Implementation Priority

**Phase 1 (Current)**: Simple Pending → Reindexing → Enabled
- Acceptable for most deployments
- SP not searchable during reindex window
- Simpler implementation

**Phase 2 (Future)**: Zero-Downtime Rollforward
- Required for 24/7 production systems
- No search gaps during reindex
- Add when there's demand

## Verdict

**Recommended** - This approach aligns perfectly with the existing event-sourced conformance system. The events and state machine already exist. The DurableTask pattern is proven. The only new code is:
1. A Medino handler to listen for package activation and spawn orchestrations
2. The orchestration and worker activities (following Export pattern)
3. Wire up events to ConformanceState (already done)

Estimated effort: ~400-500 LOC, mostly adapting existing Export patterns.
