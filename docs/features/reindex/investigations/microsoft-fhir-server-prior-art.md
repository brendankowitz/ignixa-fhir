# Investigation: microsoft/fhir-server `$reindex` Prior Art

**Feature**: reindex
**Status**: Complete
**Created**: 2026-10-06

## Approach

This investigation studies how [microsoft/fhir-server](https://github.com/microsoft/fhir-server) implements
`$reindex`, so Ignixa can reuse what has proven itself in production and avoid known problems. The source was
`main` at commit `035d5460`, read on 2026-10-06. All paths below are relative to that repository.

### API surface (`Shared.Api/Controllers/ReindexController.cs`)

| Route | Behaviour |
|---|---|
| `POST [base]/$reindex` | Creates a job. Returns **201 Created** with a `Parameters` body, `Content-Location`, ETag, and Last-Modified. |
| `GET [base]/$reindex` | Lists active jobs. |
| `GET [base]/$reindex/{id}` | Returns job status as `Parameters`. |
| `DELETE [base]/$reindex/{id}` | Cancels the whole job group. Returns **202 Accepted**. |
| `GET [type]/[id]/$reindex` | **Dry run.** Extracts the index entries for one resource and returns them. Nothing is persisted. |
| `POST [type]/[id]/$reindex` | Extracts and **persists** the index for one resource with the same `Version`, `RawResource`, and `LastUpdated` (`ReindexSingleResourceRequestHandler.cs:63-117`). |

- **Request parameters:** `maximumNumberOfResourcesPerQuery` and `maximumNumberOfResourcesPerWrite` (each bounded
  to 1..10000; defaults 10000 and 1000). The controller also parses `targetResourceTypes`,
  `targetSearchParameterTypes`, `queryDelayIntervalInMilliseconds`, and `targetDataStoreUsagePercentage`, but
  **`CreateReindexRequestHandler` never forwards them to the job**, so they have no effect
  (`CreateReindexRequestHandler.cs:41-66`, `ReindexJobRecord.cs:26-100`). `OperationDefinition/reindex.json`
  declares no parameters at all.
- **Duplicate POST:** when a job is already active, the server returns the *existing* job with 201 instead of
  409 Conflict (`CreateReindexRequestHandler.cs:46-55`).
- **Status `Parameters` fields:** `id`, `startTime`, `endTime`, `lastModified`, `queuedTime`,
  `totalResourcesToReindex`, `resourcesSuccessfullyReindexed`, `progress` (capped at 99.9 until done),
  `status`, `resources`, `resourceReindexProgressByResource (resource count)`, `searchParams`,
  `failureDetails`, `maximumNumberOfResourcesPerQuery`, and `maximumNumberOfResourcesPerWrite`
  (`ReindexJobRecordExtensions.cs:19-109`).

### Job architecture (`Core/Features/Operations/Reindex/`)

The job runs on the generic SQL `JobQueue` (`QueueType.Reindex`). One `ReindexOrchestratorJob` fans out
`ReindexProcessingJob`s that share a `GroupId`. The legacy `ReindexJobTask` has been removed
(microsoft/fhir-server#5711).

The orchestrator (`ReindexOrchestratorJob.cs`) runs these steps:

1. `DeleteOrphans()` marks status rows whose `SearchParameter` resource no longer exists as `Deleted`.
2. **It waits until every instance's search-parameter cache has converged** (`WaitForAllInstancesCacheSyncAsync`
   compared against an `EventLog` signal on SQL, or a fixed multiplier × refresh interval on Cosmos).
3. It selects parameters whose status is `Supported`, `PendingDelete`, `PendingHardDelete`, or `PendingDisable`.
   It expands their base types to derived types and intersects that set with the resource types that actually
   exist in the store.
4. On SQL it pages through `GetSurrogateIdRanges(...)` 100 ranges at a time and enqueues each batch right away,
   so workers start before range planning finishes. Each job definition carries the resource type, the
   surrogate-id range, the **expected `SearchParameterHash`**, the target parameter URLs, and the batch sizes.
5. It polls child jobs every 30 seconds. When every range for a resource type is done, it removes that type from
   each parameter's pending set. When a parameter's pending set is empty, the parameter moves to its terminal
   status: `Supported` becomes `Enabled`, `PendingDisable` becomes `Disabled`, and `PendingDelete` becomes
   `Deleted`. **A single failed child fails the whole orchestrator.**
6. It waits for cache convergence again before returning.

The processing job (`ReindexProcessingJob.cs`) works as follows:

- It **compares the expected hash with the current hash**. A mismatch means a second parameter change landed
  mid-job, so it raises a soft failure instead of writing stale indexes.
- It splits its range again by `maximumNumberOfResourcesPerWrite`, then reads with `SearchBySurrogateIdRange`.
- It recomputes the indexes for **every resource in range, without filtering by hash**, and writes them with
  `BulkUpdateSearchParameterIndicesAsync`.
- Polly retries SQL timeouts and Cosmos 429 responses. An OOM policy shrinks the batch size and retries.

### Hash and index writes

- On every normal write, `ResourceWrapperFactory` stamps `SearchParamHash` with
  `GetSearchParameterHashForResourceType`.
- `dbo.UpdateResourceSearchParams` updates `SearchParamHash` and diffs every typed index table. It joins on
  `(ResourceTypeId, ResourceSurrogateId)` **with `IsHistory = 0`**. Any resource updated concurrently has already
  become history, so it is skipped and counted in `@FailedResources`. The server logs these conflicts and does not
  retry them (`SqlServerFhirDataStore.cs:971-1013`). Ignixa's `UpdateResourceSearchParams.sql` is a port of this
  procedure.

### Status lifecycle (`Search/Registry/SearchParameterStatus.cs`)

The enum values are `Disabled`, `Supported`, `Enabled`, `Deleted`, `PendingDelete`, `PendingDisable`,
`Unsupported`, `Initialized`, and `PendingHardDelete`.

- Only `Enabled` parameters are searchable.
- `Supported` parameters can be searched only when the request sends the header `x-ms-use-partial-indices: true`.
- Each instance runs `SearchParameterCacheRefreshBackgroundService`, which polls `MAX(LastUpdated)` (default
  every 60 seconds) and runs a full sync only when that value changes.

### Known issues

- microsoft/fhir-server#2200 and microsoft/fhir-server#3684: jobs stuck in `Queued` (on Cosmos), a long-standing
  class of bug. #3684 is still open.
- microsoft/fhir-server#2167: reindex fails on a specific resource type (Immunization).
- microsoft/fhir-server#5711 refactored reindex into the orchestrator/processing design, and
  microsoft/fhir-server#5789 normalized its logging to make stuck or failed runs diagnosable.
- `docs/rest/TokenOverflowSearchExample.http` warns that load-balanced writes during a run can leave resources
  unindexed even after the job reports success.

### Tests

`test/.../Rest/Reindex/ReindexTests.cs` (~1440 lines, run with parallelization disabled) covers:

- scale (500 parameters at once)
- concurrent updates, where reported counts are lower than the original count
- parameters added before or after resources exist
- case variants of parameter URLs
- delete and disable lifecycles
- a large matrix of "SearchParameter create/delete conflicts while a reindex is in flight, then succeeds on the
  next attempt"

## Tradeoffs

| Adopt | Rationale |
|---|---|
| Orchestrator plus surrogate-id range fan-out | Same shape as Ignixa `$export`. Proven at 10^8+ rows. |
| Worker check that the definitions are not older than the target | Adapted from the MS expected-hash check. Ignixa compares definitions positions (conformance `EventId`) instead of hashes. |
| Index-only write via `UpdateResourceSearchParams` with an `IsHistory = 0` guard | Does not create a version. Concurrent writers win without a lock. |
| Cache convergence before completing | Adapted: Ignixa uses polling only as an advisory delay. Correctness comes from a database-enforced barrier that rejects transaction allocations from writers with stale definitions. |
| Wire-compatible API (`Parameters` body, 201 + `Content-Location`, `DELETE` to cancel, per-resource GET dry run / POST persist) | `ms-reindex.json` and existing MS-oriented clients keep working. |
| Partial-index opt-in header | Operators can still query during long runs, and they have to ask for it explicitly. |

| Adapt / Avoid | Rationale |
|---|---|
| **Replace** the per-row `SearchParamHash` with a **per-tenant transaction cutoff fenced by a conformance barrier** | Ignixa transaction ids are time-ordered and anchor surrogate ranges, and visibility is contiguous. Raising a per-database `MinAcceptedDefinitionsEventId` and then reading `MAX(Transactions)` gives an exact clustered-range scope with exact totals. Writers with stale definitions are rejected at allocation. An upgrade alone triggers no reindex. |
| **Avoid** accepting parameters that are not enforced | Silent no-ops are success-shaped fallbacks. Ignixa either implements a parameter or rejects it with 400. |
| **Adapt:** completion is fenced by the barrier plus a drain to B_t | MS can report success while resources remain unindexed. Ignixa's barrier makes it impossible for a stale writer to land outside the cutoff set. |
| **Adapt:** use DurableTask instead of a custom JobQueue | ADR-2510. Gives replay, retries, and `TerminateInstanceAsync` without a heartbeat thread. |
| **Adapt:** fan out per tenant database, with a cutoff per tenant | MS uses one database. Ignixa stores status globally and data per tenant. |
| **Adapt:** *queue* a follow-up job on concurrent parameter changes, instead of returning 409 or superseding | Package activation can run at any time. Without a per-row marker, superseding would throw away completed ranges. |
| **Avoid** the `PendingDisable` and `PendingDelete` states | Deactivation makes a parameter unsearchable immediately, which is safe. Orphan rows are harmless because ids are never reused across canonicals. Only removing an override needs a reindex. |
| **Avoid** letting one failed range fail everything with no diagnostics | Ignixa records per-resource failures and isolates the failure to that tenant. |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data). Endpoints call Medino handlers, orchestration lives in
  `Ignixa.Application.BackgroundOperations`, and SQL stays in the DataLayer.
- [x] F5 developer experience. DurableTask is already wired up, and a fresh database reindexes zero rows.
- [x] FHIR spec compliance. `$reindex` is not an HL7 operation. Ignixa matches MS for interoperability and
  publishes an `OperationDefinition` that matches the parameters it actually accepts.
- [x] Consistent with existing patterns: `$export` orchestration, `BackgroundJob` metadata, and event-sourced
  conformance.

## Evidence

- Source citations are inline above, against microsoft/fhir-server `main` @ `035d5460`.
- Ignixa assets reused or affected:
  - `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/UpdateResourceSearchParams.sql`
  - `StoredProcedures/MergeResourcesBeginTransaction.sql` (time-ordered transaction ids that anchor surrogate
    ranges) and `MergeResourcesAdvanceTransactionVisibility.sql` (contiguous visibility)
  - `Tables/SourceEvents.sql` (`TransactionId`) and `EventStore/SqlServerSourceEventStore.cs`
    (`ReadVisibleTransactionCutoffAsync`)
  - `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs:216` (the transaction id is allocated
    after the indexes are extracted)
  - `src/Application/Ignixa.Application/Features/Conformance/ConformanceState.cs:42-56` (overrides reuse the
    `SearchParamId`) and `:365-389`
  - `src/Application/Ignixa.Conformance.Events/Events/SearchParameterEvents.cs`
  - `src/Application/Ignixa.Application.BackgroundOperations/Export/**`
  - `src/Core/Ignixa.TestScript.Suites/testscripts/Microsoft/ms-reindex.json`

## Verdict

**Viable.** This is the reference model for [spec.md](../spec.md). Ignixa adopts the range fan-out, index-only
write, and API shape. It replaces the MS per-row hash with a barrier-fenced, per-tenant transaction cutoff, and
changes the mechanics to fit its own architecture (DurableTask, per-tenant databases, queued follow-up jobs). It
also drops the MS gaps listed above.
