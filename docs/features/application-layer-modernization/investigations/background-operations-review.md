# Investigation: Background Operations & Sidecar Contracts Review

**Feature**: application-layer-modernization
**Status**: Complete
**Created**: 2026-07-11
**Scope**: Ignixa.Application.BackgroundOperations, Ignixa.Application.Operations, Ignixa.Sidecar.Contracts

## Summary

The DurableTask orchestration/activity decomposition matches ADR 2510's intended shape, and the streaming pipelines (bounded channels, line-by-line NDJSON, no whole-file buffering) are genuinely good. But the failure/observability story is broken end-to-end: orchestrations swallow failures into "Completed" orchestration states, the status handler then reports failed jobs as successful, no retry policy exists anywhere despite being the ADR's core justification, and two orchestrations perform non-deterministic I/O inside orchestration code. Ignixa.Application.Operations carries ~13 files of dead, namespace-only duplicates of the Experimental Transform/Terminology features. Ignixa.Sidecar.Contracts is healthy.

## Strengths

- **Streaming done right**: `StreamingImportFileActivity` reads NDJSON line-by-line from a blob stream through a bounded channel with backpressure (`Import/Activities/StreamingImportFileActivity.cs:122-129, 322-368`); `ExportWorkerActivity` streams DB→writer with a bounded 500-slot channel and periodic flush (`Export/Activities/ExportWorkerActivity.cs:223-292`). No whole-dataset buffering anywhere.
- **Partition-based export design**: surrogate-ID range partitioning (`GetExportRangesActivity`) eliminates continuation-token pagination and parallelizes cleanly — a sound architecture.
- **Eternal orchestration singletons**: `EternalOrchestrationStarter` uses `CreateOrchestrationInstanceAsync` with `dedupeStatuses` for atomic dedup (`Ignixa.Api/BackgroundServices/EternalOrchestrationStarter.cs:63-95`), and `ContinueAsNew` correctly truncates history.
- **TtlCleanup cannot run away**: bounded batch (default 100/tenant/cycle), per-tenant repository scoping, partition 0 explicitly excluded (`TtlCleanup/Orchestrations/TtlCleanupOrchestration.cs:57-58`), per-resource audit logging on success and failure (`TtlCleanup/Activities/TtlCleanupActivity.cs:93-118`). This is exactly the defensive posture a hard-delete job needs.
- **TransactionWatcher matches the app-level transaction model**: commits stalled transactions via `IFhirRepositoryFactory`/`CommitTransactionAsync` (roll-forward visibility, not SQL ACID rollback), per-tenant, skips partition 0, isolates per-transaction failures (`TransactionWatcher/Activities/TransactionWatcherActivity.cs:65-93`).
- **No `Hl7.Fhir.*` anywhere** — both csproj files use only `Ignixa.*`, DurableTask, and Grpc packages. Nullable enabled; repo-wide `TreatWarningsAsErrors`.
- **DurableTaskHostedService init**: thoughtful transient-error classification for SQL Server and RBAC-propagation retry for Azure Storage (`Ignixa.Api/Infrastructure/DurableTaskHostedService.cs:82-210`).
- **Sidecar.Contracts** is clean: four well-documented protos (audit, logging, metrics, rbac), fail-fast semantics documented in the contract itself, dual-targeted net9.0/net10.0, packable. No issues.

## Findings

### P0 — Failed jobs are reported as Completed
**Location**: `src/Application/Ignixa.Application.BackgroundOperations/Jobs/GetJobStatusHandler.cs:204-223`, `Import/Orchestrations/ImportOrchestration.cs:149-160`, `Export/Orchestrations/ExportOrchestration.cs:216-248`
**Issue**: Every orchestration catches all exceptions and returns a failure *payload* (`Status = "Failed"` / `Success = false`) — so the DurableTask orchestration itself ends in `OrchestrationStatus.Completed`. `GetJobStatusHandler.UpdateJobStatusFromOrchestrationAsync` maps `OrchestrationStatus.Completed` unconditionally to `job.Status = "Completed"` and never inspects the output's `Status`/`Success`/`ErrorMessage` fields. Consequences: (a) a failed import is reported to the client as Completed, with counts extracted from the failure output; (b) for export, `CompleteJobActivity` correctly writes `Status = "Failed"` to the job record, and the next status poll *overwrites it back to "Completed"*. `OrchestrationStatus.Failed` can effectively never occur because the orchestrations never rethrow.
**Recommendation**: Pick one failure channel. Either let orchestrations throw (DurableTask marks the instance Failed and the handler mapping works), or make the status handler deserialize the output and honor its success flag. Also stop overwriting `EndDate` with poll time on every read.
**Effort**: M

### P0 — Export job result records file paths that don't exist
**Location**: `src/Application/Ignixa.Application.BackgroundOperations/Export/Orchestrations/ExportOrchestration.cs:85` vs `:190`
**Issue**: Workers write to `partition/{tenantId}/export/{jobId}/...` (line 85) but Phase 4 builds the `exportedFiles` dictionary — persisted into the job result and returned by `$export` status — with `tenant/{tenantId}/export/{jobId}/...` (line 190). `partition/` appears nowhere else in the codebase; every other path (including `ExportJobDefinition.OutputPath` in `CreateExportJobHandler.cs:108` and the documented format in `ExportWorkerInput.cs`) uses `tenant/`. Clients following the reported output URLs will find nothing.
**Recommendation**: Build the path once, pass it through `ExportWorkerOutput`, and use the worker-reported path in the result. Add an E2E test that downloads a reported output file.
**Effort**: S

### P0 — Non-deterministic I/O inside orchestration code
**Location**: `src/Application/Ignixa.Application.BackgroundOperations/TransactionWatcher/Orchestrations/TransactionWatcherOrchestration.cs:31, 53`; `TtlCleanup/Orchestrations/TtlCleanupOrchestration.cs:31, 54`
**Issue**: Both eternal orchestrations constructor-inject `ITenantConfigurationStore` and call `GetAllTenantsAsync` inside `RunTask`. DurableTask orchestrations must be deterministic: on replay (worker failover, mid-cycle recovery while tenant activities are in flight) the live tenant list can differ from the history, producing divergent task scheduling or non-determinism failures. The registration helper's name — `AddTaskOrchestrationsFromInterface` "orchestrations with DI dependencies" (`Ignixa.Api/Infrastructure/DurableTaskConfiguration.cs:58-60`) — institutionalizes the mistake.
**Recommendation**: Move tenant enumeration into a `GetActiveTenantsActivity` and schedule it like any other activity. Orchestrations should have zero I/O dependencies.
**Effort**: M

### P0 — Default export silently exports only 6 hardcoded resource types
**Location**: `src/Application/Ignixa.Application.BackgroundOperations/Export/Orchestrations/ExportOrchestration.cs:251-262`
**Issue**: When `_type` is not specified, `GetDefaultResourceTypes()` returns exactly `{Patient, Observation, Condition, MedicationRequest, Encounter, Procedure}`. The command doc (`CreateExportJobCommand.cs:23-25`) and `ExportCoordinatorInput` both promise "if empty, exports all types," and the FHIR bulk-export spec requires all types by default. A system-level `$export` silently omits every other resource type — undetectable data loss for the consumer.
**Recommendation**: Resolve the full supported resource-type list from the tenant's schema/capability provider (via an activity), or reject type-less exports until that exists. Do not ship a silent subset.
**Effort**: S/M

### P0 — No retry policy anywhere; ADR 2510's fault-tolerance claim is unimplemented
**Location**: all orchestrations — zero usages of `ScheduleWithRetry`/`RetryOptions` across `Ignixa.Application.BackgroundOperations`
**Issue**: ADR 2510 selects DurableTask for "built-in retry policies and fault tolerance," but every activity is scheduled with plain `ScheduleTask`. One transient SQL timeout in one export worker fails the whole job (and per the first P0, may then be reported as Completed). Long-running import files that crash mid-way are never retried.
**Recommendation**: Wrap activity scheduling in `ScheduleWithRetry` with a shared `RetryOptions` policy (short backoff, handle-transient predicate). This also forces the idempotency review below.
**Effort**: M

## P1 — Significant tech debt

### P1 — Fake concurrency limiting in Import and Terminology orchestrations
**Location**: `Import/Orchestrations/ImportOrchestration.cs:54-90`; `Terminology/Orchestrations/TerminologyImportOrchestration.cs:26-47`
**Issue**: `input.InputFiles.Select(f => context.ScheduleTask(...)).ToList()` schedules *every* activity immediately; the subsequent batched `Task.WhenAll` in groups of 2 (or 5) limits nothing — all activities run as concurrently as the worker allows. The 10-line comment explaining the thread-pool math ("2 concurrent files × 9 = 18 threads vs 45") describes behavior the code does not have. Classic generated-code artifact: elaborate justification, no-op mechanism.
**Recommendation**: Schedule lazily — only create the next `ScheduleTask` after a slot frees (sliding-window pattern), or rely on the worker's `MaxConcurrentTaskActivityWorkItems` and delete the fake batching + comment.
**Effort**: S

### P1 — Import job completion never persisted; error log written to local disk
**Location**: `Import/Activities/CompleteJobActivity.cs:27-142`
**Issue**: Import's `CompleteJobActivity` never touches `IBackgroundJobRepository` — the job record is only moved to a terminal state lazily when a client polls (`GetJobStatusHandler`), and per P0 #1 that flattening is wrong. Worse, the "error log upload" writes NDJSON to `Path.Combine("import-errors")` — a CWD-relative path on the local filesystem of whichever worker node ran the activity — and returns a URL (`/import-errors/...`) that nothing serves. The file is not tenant-scoped and contains failure details for PHI-bearing resources. The `Future: Upload to blob storage` comment has shipped.
**Recommendation**: Write the error NDJSON to blob storage under `tenant/{tenantId}/import/{jobId}/errors.ndjson` via the existing `IBlobStorageClient`, and update the job record (status, result, EndDate) here, mirroring export's activity.
**Effort**: M

### P1 — Unbounded error accumulation flows through orchestration state (memory + PHI)
**Location**: `Import/Activities/StreamingImportFileActivity.cs:230-238, 391-442`; `Import/Models/ImportErrorLogEntry.cs`; `Import/Orchestrations/ImportOrchestration.cs:99-101`
**Issue**: Every failed line stores the *full resource JSON* in `ImportErrorLogEntry.ResourceJson`, accumulated in memory per file, serialized into `StreamingImportFileOutput`, through DurableTask history, aggregated across files, and passed into `CompleteJobInput`. A malformed 1M-line file produces a multi-GB activity output — OOM and/or orchestration-message-size failure. It also persists raw PHI into the DurableTask state store.
**Recommendation**: Cap in-memory errors (e.g., first N), stream error entries directly to a blob from within the activity, and pass only counts + the blob path through orchestration state. Drop `ResourceJson` from entries that transit DurableTask.
**Effort**: M

### P1 — Cancellation is not implemented anywhere
**Location**: every activity — `CancellationToken.None` at all I/O sites (e.g., `Export/Activities/ExportWorkerActivity.cs:83-292`, `Import/Activities/StreamingImportFileActivity.cs:91-281`, `TtlCleanup/Activities/TtlCleanupActivity.cs:43-83`)
**Issue**: ADR 2510 lists cancellation support as a requirement. DurableTask.Core activities don't hand you a token, but the code doesn't build one either: there is no `TerminateAsync` path, no job-cancel command/endpoint, and no cooperative check of job status inside long loops. A multi-hour export cannot be stopped except by killing the worker. `GetJobStatusHandler` even maps `Terminated → Cancelled` (`Jobs/GetJobStatusHandler.cs:231-234`) for a transition nothing can trigger.
**Recommendation**: Add a `CancelJobCommand` that calls `TaskHubClient.TerminateAsync`, plus a periodic job-status check (every N batches) inside worker/import loops to stop cooperatively.
**Effort**: M

### P1 — Orphaned producer task on consumer failure in ExportWorkerActivity
**Location**: `Export/Activities/ExportWorkerActivity.cs:229-289`
**Issue**: The producer is a `Task.Run` writing to a bounded channel; the consumer loop runs inline. If the consumer throws (line 255-286 rethrows), the method unwinds without awaiting `producerTask` (line 289 is never reached) and without completing the channel reader. The producer remains blocked on `WriteAsync` against a full channel forever, holding an open DB streaming query — a leaked task, connection, and unobserved exception per failed worker.
**Recommendation**: In a `finally`, drain/complete: `channel.Reader.Complete()` isn't available — instead cancel the producer via a linked CTS and `await producerTask` inside try/catch before rethrowing. Same pattern in `StreamingImportFileActivity` is safer (it awaits producer then consumers, line 277-278) but still deadlock-prone if a consumer dies while the channel is full and other consumers exit.
**Effort**: S

### P1 — Group export is wrong for non-Patient types and unbounded for large groups
**Location**: `Export/Activities/ExportWorkerActivity.cs:163-197`
**Issue**: Group-scoped export (`GroupId`) only constrains the `Patient` type; every other requested type exports the *entire tenant's* resources, not the group compartment — a data-overexposure bug against bulk-export semantics. Additionally, group membership is re-resolved by every worker (once per range — 6+ redundant resolutions), and all member IDs are joined into a single `_id=a,b,c,...` parameter, which breaks for large groups (query/parameter limits).
**Recommendation**: Resolve membership once in the orchestration (activity), constrain non-Patient types by patient compartment, and chunk the ID filter. If compartment-scoped export isn't ready, reject `GroupId` + non-Patient type combinations instead of silently over-exporting.
**Effort**: L

### P1 — Import retry non-idempotency: server-assigned IDs regenerate on re-run
**Location**: `Import/Activities/StreamingImportFileActivity.cs:404-409`
**Issue**: Resources missing an `id` get `Guid.NewGuid()` assigned per execution. When the file activity is re-executed (worker crash today; retry policy once P0 #5 is fixed), previously-written resources with generated IDs are written *again* under new IDs — silent duplication. Resources with client IDs are safe (PUT upsert).
**Recommendation**: Derive deterministic IDs for ID-less rows (e.g., UUIDv5 of jobId + fileUrl + lineNumber), making file replay idempotent.
**Effort**: S

### P1 — Zombie jobs: no reconciliation for crash between job-create and orchestration-start
**Location**: `Export/CreateExportJobHandler.cs:115-137`; `Import/CreateImportJobHandler.cs:85-106`
**Issue**: Job row is created (`Status="Queued"`), then the orchestration is started, then the row is updated with the instance ID. A crash between steps leaves a Queued job with no orchestration, forever. `GetJobStatusHandler` fetches orchestration state by JobId and silently does nothing when it's null (`Jobs/GetJobStatusHandler.cs:189-246`) — the job shows Queued indefinitely. Nothing sweeps for these (TransactionWatcher watches transactions, not jobs; `HeartbeatDate` is written once and never updated by anything).
**Recommendation**: Either start the orchestration first with the job payload (orchestration creates the record), or have the status handler mark instance-less jobs older than a threshold as Failed. Delete `HeartbeatDate` if nothing maintains it.
**Effort**: M

### P1 — Eternal orchestrations can silently never start; background subsystem silently disabled
**Location**: `Ignixa.Api/BackgroundServices/EternalOrchestrationStarter.cs:31, 110-113`; `Ignixa.Api/Infrastructure/DurableTaskHostedService.cs:38-43`
**Issue**: The starter "waits for DurableTask infrastructure" with a blind `Task.Delay(5s)`, then swallows any startup failure with a catch-all log. If SQL schema init is still in its retry loop (it retries up to 10× with growing delays), the create fails and TtlCleanup + TransactionWatcher never run until a process restart — with the only evidence one log line. Similarly, `DurableTaskHostedService` returns on init failure, "disabling" all background jobs while the server reports healthy. TransactionWatcher not running means stalled import transactions stay invisible — a data-visibility outage with no signal.
**Recommendation**: Sequence properly (expose an initialization signal from the hosted service, or start the starter from within it after `StartAsync`), and surface both failure modes in a health check.
**Effort**: M

### P1 — Application layer constructs concrete SQL DataLayer types (Terminology)
**Location**: `Terminology/Activities/ImportTerminologyResourceActivity.cs:9-10, 42-51, 197-237`
**Issue**: The activity service-locates `SqlEntityFrameworkRepositoryFactory` from `IServiceProvider`, `new`s `SqlSystemRepository` and `SqlCodeSystemImporter`, uses `FhirDbContext` and EF Core directly, and hand-builds an `UPDATE dbo.PackageResource` SQL string (parameterized — not injectable, but still raw SQL in Application). This violates the layer rule (Application → Domain interfaces → DataLayer implements) and is untestable without a real SQL provider. It also hard-locks terminology import to the SQL provider even when the tenant runs the FileSystem datalayer.
**Recommendation**: Define `ITerminologyImportStore` (load package resource, update import status) in Domain, implement in the SQL DataLayer, inject it.
**Effort**: M

### P1 — ~13 files of dead duplicated Transform/Terminology code in Ignixa.Application.Operations
**Location**: `Ignixa.Application.Operations/Features/Transform/*` (7 files), `Ignixa.Application.Operations/Features/Terminology/{Expand,Subsumes,Translate}/*` (6 files)
**Issue**: These are byte-for-byte copies of `Ignixa.Application/Features/Experimental/Transform` and `.../Experimental/Terminology` differing only in namespace (verified by diff). Nothing outside the Operations project references the Operations namespaces for these features; DI registration (`ExperimentalAutofacRegistration.cs:121-171`) and endpoints use the Experimental copies. Two diverging copies of a $transform engine is how a security fix lands in only one of them. (Behavioral issues in the *live* Experimental copies — 5s FHIRPath "timeout" that abandons the evaluating thread since the token is never observed by the evaluator, sync-over-async `GetAwaiter().GetResult()` in `ConceptMapResolverService.Translate` which also swallows all exceptions into `null`, and a per-request-scoped `MapRegistryCache` that a package-loaded invalidation handler can never meaningfully invalidate — belong to the Application-core review, but apply verbatim to these copies.)
**Recommendation**: Delete the Operations copies of Transform and Terminology outright. Keep DeIdentify, MemberMatch, PatientEverything, Validate (those are the live, registered ones).
**Effort**: S

### P1 — MemberMatch ignores identifier.system: wrong-patient match risk
**Location**: `Ignixa.Application.Operations/Features/MemberMatch/DefaultMemberMatchStrategy.cs:198-203`
**Issue**: `BuildIdentifierExpression` matches on `FieldName.TokenCode` only — the identifier *system* is extracted (`ExtractIdentifiers`) and then discarded. Two patients with the same identifier value under different systems (MRN "12345" at hospital A vs plan-member "12345") can produce a single confident "match" of the wrong person. `$member-match` returning the wrong patient is a PHI disclosure, and HRex matching is exactly where this matters.
**Recommendation**: Include system in the token expression when present (`system|value` semantics); only fall back to bare-value matching when the input identifier has no system, and consider requiring more than one corroborating identifier for a match.
**Effort**: S

### P1 — Patient $everything silently truncates with no paging
**Location**: `Ignixa.Application.Operations/Features/PatientEverything/PatientEverythingHandler.cs:71, 117-122`
**Issue**: `MaxItemCount = request.Count ?? 50`, `ContinuationToken: null`, `HasMore: false`. A patient with 500 resources returns 50 with no `next` link and no signal that the bundle is incomplete. Consumers of $everything (payer/member access flows) will treat the bundle as the complete record.
**Recommendation**: Implement continuation for the PatientEverything expression, or until then set a high explicit cap and return `HasMore`/an OperationOutcome warning when truncated — silent truncation is the worst option.
**Effort**: M/L

### P1 — ListJobsTool loads all tenants' jobs and filters in memory
**Location**: `JobManagement/ListJobsTool.cs:76-101`
**Issue**: `ListAsync(jobType)` fetches every job across all tenants, then filters `j.Definition.TenantId == resolvedTenantId` in process. Tenant isolation enforced post-query in application code is one refactor away from a cross-tenant leak, and it's O(all-jobs) per call.
**Recommendation**: Add a tenant-scoped `ListAsync(tenantId, jobType, ...)` to `IBackgroundJobRepository` and push the predicate to the store.
**Effort**: S

### P1 — SAS tokens persisted into DurableTask orchestration state
**Location**: `Import/Models/ImportOrchestrationInput.cs` (`StorageDetail` — "SAS tokens, etc."), flows into `CompleteJobInput.StorageDetail`
**Issue**: `StorageDetail` is documented to carry SAS tokens and is serialized as orchestration input — persisted, unredacted, into the DurableTask store (SQL/Azure Storage) and replayed through history. It is currently unused by any activity (`CompleteJobActivity` ignores it), so today it's pure secret-at-rest liability.
**Recommendation**: Remove it from orchestration inputs until needed; when needed, store a reference/secret handle, not the token.
**Effort**: S

## P2 — Polish / style / dead code

### P2 — Counterfeit Microsoft copyright headers
**Location**: ~40 files across all three projects (e.g., `Export/Orchestrations/ExportOrchestration.cs` has none, but `ExportWorkerActivity.cs:1-4`, `ImportOrchestration.cs`, all model files, MCP tools, and even `Ignixa.Api/BackgroundServices/EternalOrchestrationStarter.cs` carry `Copyright (c) Microsoft Corporation`)
**Issue**: This repo is not microsoft/fhir-server; the header misattributes copyright and points at a LICENSE that isn't Microsoft's. Newer files use the correct `Ignixa Contributors` header (e.g., `DeIdentifyHandler.cs:2`), so both exist inconsistently.
**Recommendation**: Repo-wide header sweep to the Ignixa header (or none).
**Effort**: S

### P2 — Dead code inventory
**Location / items**:
- `Export/Activities/SearchAndWriteChunkInput.cs`, `SearchAndWriteChunkOutput.cs` — models for an activity that no longer exists (pre-partitioning pagination design).
- `Import/Activities/ImportBatchActivity.cs` + `ImportBatchInput/Output` — not registered in `DurableTaskConfiguration`, not scheduled by any orchestration; also duplicates `PrepareResource` logic from `StreamingImportFileActivity` (copy-paste divergence risk).
- `Import/Activities/ValidateFileActivity.cs:83-113` — `ExtractBlobName` never called, contains a bare `catch { return string.Empty; }`.
- `Ignixa.Application.Operations/Features/Validate/ValidateResourceHandler.cs:87-129, 224-373` — ~200 lines of commented-out pseudo-code for CREATE/UPDATE/DELETE modes, and `:475-614` a ~140-line dead method tree (`ValidateTerminologyBindingsAsync`, `GetKnownBindings`, `ExtractCodedValue`) "kept for reference" — which also uses `MutableNode` JSON navigation, banned by CLAUDE.md.
**Recommendation**: Delete all of it. Pseudo-code plans belong in docs/investigations, not shipped handlers.
**Effort**: S

### P2 — Job status magic strings
**Location**: throughout — `"Queued"/"Running"/"Completed"/"Failed"/"Cancelled"` string literals in handlers, activities, tools (`CreateExportJobHandler.cs:100`, `GetJobStatusHandler.cs:197-233`, `UpdateProgressActivity.cs:65-67`, etc.), plus mode strings `"InitialLoad"/"IncrementalLoad"` validated by string comparison in three places.
**Recommendation**: A `JobStatus` enum (or constants class) in Domain; same for import mode. Note `Mode` is validated but never actually changes behavior — either implement InitialLoad semantics or collapse the parameter.
**Effort**: S

### P2 — Invalid XML doc placement on positional record parameters
**Location**: all Export models, e.g. `Export/Models/ExportCoordinatorInput.cs:15-22` — `/// <summary>` blocks *inside* the parameter list. These are not valid doc comments (compiler treats them as trivia); TtlCleanup/TransactionWatcher models show the correct `<param>` style.
**Recommendation**: Convert to `<param>` on the record.
**Effort**: S

### P2 — Empty catch blocks in ExportOrchestration
**Location**: `Export/Orchestrations/ExportOrchestration.cs:125-128, 163-166, 235-239`
**Issue**: Three `catch { }` blocks around failure-path `CompleteJobActivity` scheduling. Comments acknowledge it ("just continue"). Project rule: empty catch blocks are bugs. Also `ScheduleTask<bool>` return values (job-not-found → false) are ignored everywhere.
**Recommendation**: At minimum, orchestrations can't log — surface via the output's `FailurePhase`; better, let the orchestration fail and reconcile in the status handler (see P0 #1).
**Effort**: S

### P2 — Export progress never reported
**Location**: `GetJobStatusHandler.cs:137-145` parses `ExportJobProgress`; nothing ever writes export progress (no export UpdateProgressActivity, no `SetCustomStatus` anywhere despite ADR 2510 citing it).
**Recommendation**: Emit worker-completion progress from the orchestration (a small activity after each `WhenAll` slice once real windowing exists).
**Effort**: S

### P2 — Import progress reporting is post-hoc and off-by-one
**Location**: `Import/Orchestrations/ImportOrchestration.cs:92-122`
**Issue**: Because all file tasks complete before the aggregation loop (see fake-batching P1), progress updates fire only after everything is done; `CurrentFile = input.InputFiles[processedFiles].Url` also points at the *next* file, not the one processed. Cosmetic once real windowing lands.
**Effort**: S (fold into the fake-concurrency fix)

### P2 — Optional constructor dependencies
**Location**: `Export/CreateExportJobHandler.cs:32` (`ViewDefinitionLoader? = null`), all four MCP tools (`IMcpAuthorizationService? = null`)
**Issue**: Optional ctor params hide wiring failures — a mis-registered `ViewDefinitionLoader` silently disables ViewDefinition validation instead of failing at startup. For the authz service, "null = authorization service not configured" is a risky default for a tool that starts import jobs.
**Recommendation**: Require them; use a no-op registration where a feature is genuinely optional.
**Effort**: S

### P2 — TtlCleanup throughput ceiling and log volume
**Location**: `TtlCleanup/Activities/TtlCleanupActivity.cs:69-120`; `TtlCleanup/Models/TtlCleanupOrchestrationInput.cs`
**Issue**: 100 resources/tenant/15-min cycle = ~9,600/day/tenant maximum; expiry rates above that accumulate a permanent backlog with no signal. Each deletion is an individual round-trip with two Information-level log lines plus an audit entry (log spam at scale). The bounded batch is the right safety default — but there's no drain-until-empty loop and no backlog metric.
**Recommendation**: Loop batches within the activity up to a time budget; drop per-resource success logs to Debug; emit a "remaining expired" gauge.
**Effort**: S

### P2 — Watcher stall threshold vs long imports (needs verification)
**Location**: `TransactionWatcher/Models/TransactionWatcherOrchestrationInput.cs` (default 5-min stall threshold); `Import/Activities/StreamingImportFileActivity.cs:100-106, 281`
**Issue**: A large import file can hold its transaction open well past 5 minutes. Whether the watcher prematurely commits it depends on `GetStalledTransactionsAsync` heartbeat semantics in the DataLayer — the import activity itself never explicitly heartbeats. If `BatchWriteAsync` does not refresh the transaction heartbeat, a slow import gets its transaction committed mid-flight by the watcher (partial visibility — arguably acceptable roll-forward, per CLAUDE.md's degraded-state philosophy, and the final `CommitTransactionAsync` must then be idempotent). Not verified in this review's scope; flagging for the DataLayer review.
**Effort**: S (verify + document)

### P2 — Multiple types per file in Api DurableTask wiring
**Location**: `Ignixa.Api/Infrastructure/DurableTaskConfiguration.cs` (3 types), `MapRegistryCache.cs` (3 types, in the dead Operations copy and the live Experimental one)
**Recommendation**: Split per repo rule.
**Effort**: S

## Architectural Observations

- **The failure channel is architecturally inverted.** Orchestrations are written to "never fail" (catch-all → failure payload → Completed status), which defeats DurableTask's own failure model, breaks the status handler, makes `OrchestrationStatus.Failed`/`Terminated` unreachable, and forced the empty-catch compensation blocks. One decision — "orchestrations throw; the job record is reconciled from real orchestration status" — collapses P0 #1, the empty catches, and half the status-handler complexity.
- **Idempotency story is thin.** Activities are re-runnable-ish by accident (PUT upserts, re-queried expired lists) rather than by design: generated import IDs break replay, `CompleteJobActivity` timestamps drift on re-execution, and there are no retries to exercise any of it. ADR 2510's "activities are stateless, retriable, idempotent units" is aspiration, not implementation.
- **Observability is logs-only.** No metrics for job success/failure, TTL backlog, watcher commits/failures; eternal-orchestration outputs are computed then discarded on `ContinueAsNew`; failures in TtlCleanup/TransactionWatcher activities are folded into output records that nothing reads. A failed background subsystem (init failure, starter race) is indistinguishable from a healthy idle one.
- **Layering is respected everywhere except Terminology import** (direct EF/SQL types in Application) **and the MCP tools**, which couple BackgroundOperations to `Features.Experimental.Mcp` infrastructure — background-job MCP surface living in the jobs assembly but depending on an experimental feature's authz/DTO types is a dependency direction worth revisiting when MCP graduates.
- **Duplication as a change-risk multiplier**: the Operations project's dead Transform/Terminology twins, plus `PrepareResource` duplicated between the live and dead import activities, are exactly where a future fix lands once and silently misses the copy.
- **Positive**: the transaction model usage is correct per CLAUDE.md — no SQL transactions wrapped around merges, watcher commits (rolls forward) rather than rolls back, extension/index-extraction failures degrade gracefully (import proceeds with a warning when search-index extraction fails — consistent with the PostMergeExtensionUpdater philosophy).

## Recommendations Summary

| Priority | Recommendation | Effort | Files affected |
|----------|---------------|--------|-----------------|
| P0 | Unify failure channel: orchestrations throw; status handler honors real orchestration outcome; stop overwriting Failed→Completed | M | GetJobStatusHandler.cs, 3 orchestrations |
| P0 | Fix `partition/` vs `tenant/` export path mismatch | S | ExportOrchestration.cs |
| P0 | Move tenant enumeration out of orchestrations into an activity | M | TransactionWatcherOrchestration.cs, TtlCleanupOrchestration.cs, DurableTaskConfiguration.cs |
| P0 | Export all resource types by default (or reject type-less export) | S/M | ExportOrchestration.cs |
| P0 | Adopt `ScheduleWithRetry` + shared RetryOptions | M | all orchestrations |
| P1 | Replace fake concurrency batching with real sliding-window scheduling | S | ImportOrchestration.cs, TerminologyImportOrchestration.cs |
| P1 | Import completion: persist job record; error log to blob, tenant-scoped | M | Import CompleteJobActivity.cs |
| P1 | Cap/stream import errors; strip ResourceJson from orchestration state | M | StreamingImportFileActivity.cs, models |
| P1 | Implement job cancellation (TerminateAsync + cooperative checks) | M | new command/handler, activities |
| P1 | Fix orphaned producer task on consumer failure | S | ExportWorkerActivity.cs |
| P1 | Fix group export scope (non-Patient types) + membership resolution | L | ExportWorkerActivity.cs, ExportOrchestration.cs |
| P1 | Deterministic IDs for ID-less import rows | S | StreamingImportFileActivity.cs |
| P1 | Zombie-job reconciliation; remove dead HeartbeatDate | M | Create*JobHandler.cs, GetJobStatusHandler.cs |
| P1 | Startup sequencing + health checks for DurableTask/eternal orchestrations | M | DurableTaskHostedService.cs, EternalOrchestrationStarter.cs |
| P1 | Extract ITerminologyImportStore; remove EF/SQL from Application | M | ImportTerminologyResourceActivity.cs, Domain, DataLayer |
| P1 | Delete dead Operations Transform/Terminology duplicate trees | S | 13 files in Ignixa.Application.Operations |
| P1 | MemberMatch: match on system|value | S | DefaultMemberMatchStrategy.cs |
| P1 | $everything paging or explicit truncation signal | M/L | PatientEverythingHandler.cs |
| P1 | Tenant-scoped job listing in repository | S | ListJobsTool.cs, IBackgroundJobRepository |
| P1 | Remove StorageDetail (SAS) from orchestration state | S | Import models |
| P2 | Header sweep (remove Microsoft copyright) | S | ~40 files |
| P2 | Delete dead code (SearchAndWriteChunk, ImportBatchActivity, pseudo-code blocks, dead validate methods) | S | 6+ files |
| P2 | JobStatus enum; implement or remove import Mode | S | cross-cutting |
| P2 | Fix record doc comments; empty catches; export progress; optional ctor deps; TTL drain loop; per-file type split | S | various |
