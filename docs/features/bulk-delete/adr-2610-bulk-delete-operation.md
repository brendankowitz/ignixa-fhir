# ADR 2610: `$bulk-delete` Operation

## Status

Proposed (implemented; pending `/accept-adr`)

## Date

2026-10-06

## Context

AHDS-compatible deprovisioning clients deprovision a FHIR service's data through fhir-server's
`$bulk-delete` operation: they call `GET /metadata`, then issue a system-level
`DELETE {base}/$bulk-delete?_type=A,B` with header `Prefer: respond-async` and a `Parameters` body
carrying `hardDelete` and `purgeHistory` boolean flags. They expect `202` with a `Content-Location`,
then poll that URL — `202` means running, any other `2xx` means done, non-`2xx` means failure —
honoring `Retry-After`. They never cancel the job and never parse the response body.

fhir-server's `$bulk-delete` (`BulkDeleteController.cs`, `GetBulkDeleteHandler.cs`,
`DeletionService.cs`) only recognizes `_hardDelete`/`hardDelete`, `_purgeHistory`,
`excludedResourceTypes`, and `_remove-references` as **query-string** flags
(`KnownQueryParameterNames.cs:111-144`); it ignores a request body entirely. Because these
AHDS-compatible clients send their flags in the body, a server that mirrors fhir-server exactly would silently soft-delete
instead of hard-deleting and purging history — the opposite of what deprovisioning needs. Ignixa
already has the building blocks this operation needs: DurableTask-based background jobs
(`$export`/`$import`), a TTL hard-delete code path, and a TestScript (`ms-bulk-delete.json`)
currently marked "not implemented".

## Options Considered

### HTTP contract

1. **Mirror fhir-server exactly** (query-string flags only). Matches the public contract
   byte-for-byte but does not satisfy AHDS-compatible clients' body-flag request, so deprovisioning
   would soft-delete instead of hard-delete-and-purge.
2. **fhir-server superset: honor body flags in addition to query flags, with strict validation**
   (chosen). Keeps fhir-server's routes, flags, and response shapes, and additionally reads
   `hardDelete`/`purgeHistory` from an optional `Parameters` body. A conflicting query/body value
   is rejected rather than silently preferring one, and any unsupported search parameter or
   modifier is rejected rather than being ignored, because bulk delete is destructive and widening
   the match set silently is unacceptable.
3. **A new, FHIR-spec-style async delete operation** (new route, new parameter names). Would be a
   clean design but breaks existing fhir-server/AHDS callers and the existing TestScript, with no
   corresponding benefit over extending the existing contract.

### Physical deletion implementation

- **Reuse the TTL hard-delete path as-is** vs. **add a new repository API.** The TTL cleanup
  activity hard-deletes one resource at a time on a schedule; `$bulk-delete` needs hard delete
  *and* history purge driven by a batch loop with per-batch progress and cancellation. Chosen:
  add `HardDeleteAsync`/`PurgeHistoryAsync` to `IFhirRepository`, with the SQL implementation
  reusing the existing `HardDeleteResourceCoreAsync` core rather than duplicating it, and
  `PurgeHistoryAsync` deleting `IsHistory = 1` versions and their index rows in one transaction
  (inline SQL; no changes to the `MergeResources` stored procedures/TVPs).

### Traversal strategy

- **Restart from the first page every batch** vs. **provider continuation token.** Soft and hard
  delete remove matches from the search result set as they go, so a continuation token would skip
  resources; re-querying the first page each batch is correct on both SQL and FileSystem (whose
  continuation is positional). Purge keeps the resource's current version matching, so its target
  set does not shrink as the batch runs, and it uses the provider's continuation token (SQL
  keyset) for efficiency. In restart mode (soft and hard delete), the orchestration fails the job when
  two consecutive batches of a type start with the same first match while more pages remain: that
  batch made no progress, and the loop would otherwise never end if traversal and deletion ever
  disagree.

## Decision

Implement `$bulk-delete` as a DurableTask-backed background job, following the fhir-server
superset contract above.

### HTTP contract

| Concern | Behavior |
|---|---|
| Routes | `DELETE [/tenant/{id}]/$bulk-delete` (system-level) and `DELETE [/tenant/{id}]/{type}/$bulk-delete`. No compartment route, matching fhir-server. |
| `Prefer` header | `Prefer: respond-async` is required; its absence is `400`. |
| Flags | `_hardDelete`/`hardDelete` and `_purgeHistory` (query), plus `hardDelete`/`purgeHistory` (`valueBoolean`) in an optional `Parameters` body. Hard delete takes precedence over purge. Any other body parameter is `400`. |
| Flag conflicts | If the query and body set the same flag to different values, `400`. |
| `excludedResourceTypes` | CSV query parameter; removes types from both the delete target set and cascaded includes. |
| `_type` | At the system route, restricts the resource type set (intersected with the tenant schema's concrete types); unknown type names are `400`. Disallowed at the type-level route, matching fhir-server. |
| Search validation | Every other query parameter is forwarded to search. Any unsupported parameter or modifier is `400` — including `_count`, `_sort`, `_summary`, `_elements`, `_total`, and `_contained*`. At the system route, a filter invalid for any targeted type is `400` naming that type. An empty/whitespace search value (e.g. `?status=`) is also `400`, since a dropped filter would widen the delete rather than narrow it. |
| Kickoff response | `202` with `Content-Location: {base}[/tenant/{t}]/_operations/bulk-delete/{id}`, no body. Mirrors the kickoff route form, including `PathBase`. |
| Status (running) | `202`, `Parameters` with an `Issues` part (`OperationOutcome`, informational, "Job In Progress"), plus any `ResourceDeletedCount` parts accumulated so far. |
| Status (completed) | `200`, `Parameters` with `ResourceDeletedCount` parts (one per type with a non-zero count, `valueInteger64`). No matches ⇒ an empty `Parameters`. |
| Status (cancelled) | `200`, partial `ResourceDeletedCount` parts plus an `Issues` warning "Job Canceled". |
| Status (failed) | `500`, `Issues` error with the failure message, plus partial counts. |
| Cancel | `DELETE` on the status URL: `202` if accepted, `409` if the job is already terminal, `404` if the job is unknown or belongs to a different tenant. |
| No matches | The job still runs to completion and returns an empty result; it is not a `404` or no-op. |
| `_include`/`_revinclude` cascade | Included resources are deleted alongside matches (includes first, then matches), and excluded types are filtered out of the includes, matching fhir-server. |
| `_remove-references` | Accepted only together with hard delete; otherwise `400`. When active, for each deleted resource the handler finds referrers via `_id=X&_revinclude=*:*`, sets `reference = null` and `display = "Referenced resource deleted"`, and saves a new version. |
| Storage capability | `IFhirRepository.SupportsPhysicalDeletion` gates hard delete/purge kickoff. SQL returns `true`. FileSystem (prototype, append-only) returns `false`, so a hard-delete/purge kickoff against FileSystem is `400` (unsupported), not a silent soft delete. `_include`/`_revinclude` are likewise `400` on FileSystem, which does not evaluate includes. |
| Tenant/job-type isolation | The job definition carries `TenantId`. Status and cancel return `404` unless `job.Definition.TenantId == routeTenant` **and** `JobType == BulkDelete`. Lookup uses the typed `IBackgroundJobRepository<BulkDeleteJobDefinition>.GetAsync(jobId, tenantId, jobType, ct)`, which filters `JobType` in SQL *before* deserializing the definition, so a job ID belonging to another job type (e.g. an export job) is reported `404` rather than throwing a deserialization error. Every activity resolves its tenant-scoped repository and search service from the job's `TenantId`, not the ambient request. |

### Orchestration shape

`BulkDeleteOrchestration` processes the snapshotted resource type set sequentially. For each type
it loops `BulkDeleteBatchActivity` (scheduled with retry) until a batch finds no more matches, then
runs `CompleteBulkDeleteJobActivity`, which marks the job `Completed`/`Failed` and writes the final
per-type counts. Every 100 batches the orchestration calls `ContinueAsNew`, carrying its traversal
position (type index, cursor, previous first match) and cumulative counts forward in the input, so
DurableTask history — and replay cost — stays bounded no matter how many resources a job deletes;
the instance ID is unchanged, so status and cancel address every execution. Each
`BulkDeleteBatchActivity` establishes a background `FhirRequestContext` for the job's tenant,
searches a page (with includes per the cascade rules above), deletes includes then matches using
the mode-appropriate path (soft: `DeleteResourceCommand`; hard: `repo.HardDeleteAsync`; purge:
`repo.PurgeHistoryAsync`), and persists cumulative progress after every batch. Soft and hard delete
re-read the first page every batch, because a deleted match drops out of the result set; purge
keeps current versions in place, so it instead pages forward with the SQL provider's keyset
continuation token and reads each page's `_include`/`_revinclude` cascade with a separate `_id={m1},
{m2},...` search over that page's matches (chunked to stay under SQL's parameter limit), since the
keyset cursor cannot carry includes. Cancellation terminates the orchestration so no new batch is
scheduled; an in-flight batch finishes its current page, and its subsequent progress write hits
`BackgroundJobUpdateConflictException`, which the activity reports as `Superseded` rather than
retrying. Activities also observe `IHostApplicationLifetime.ApplicationStopping` for graceful
shutdown. Batch size is configurable (`BulkDelete:BatchSize`, default 500) and is captured in the
orchestration input at kickoff so a replay is deterministic.

### Capability statement

A `BulkDeleteFeature : IPackageFeature` registers system-level `bulk-delete` and, under the `"*"`
key, the resource-level `bulk-delete` operation on every resource type -- the same pattern
`IncludesOperationFeature` uses -- with fallback canonical
`http://hl7.org/fhir/OperationDefinition/bulk-delete`.

### Authorization

The route-level `FhirAuthorizationFilter` classifies a kickoff as `operation-system`/
`operation-type`. SMART maps `operation-*` to require a `*` scope (so `system/*.s` or `system/*.d`
for a system-level operation, or `*/*` at least). Because SMART maps `operation-*` to no
permission and RBAC checks only the route's type, which is wrong for a destructive operation whose
cascade and reference rewrite reach other types, the kickoff runs a dedicated step,
`BulkDeleteKickoffAuthorizer`, through the same handler pipeline (authentication, tenant
isolation, RBAC or sidecar RBAC, SMART) when `Authorization:Enabled` is set. The SMART
route-level check precedes the authorizer's validation:

- `delete` on the route type (type level), on each `_type` entry (system level), or on `*` (every
  type) for a system-level request without `_type` or any request with `_include`/`_revinclude`.
- additionally `update` on `*` when `_remove-references` is set, because referrers of any type are
  rewritten.
- `403` if any of those grants carries a data restriction (a patient/practitioner compartment or a
  SMART v2 search constraint): the job deletes everything the request matches, so a restricted token
  must never start one.

The type set is derived from the request rather than the job snapshot and errs wide;
`excludedResourceTypes` only narrows a job, so it is ignored. Status (`search-system` on `*`) and
cancel (`delete` on `*`) keep the generic route classification, so a caller granted `delete` on one
type can start a type-level job but needs those broader grants to poll or cancel it.

### Deviations from fhir-server

These are intentional differences from fhir-server's `$bulk-delete`, called out because
AHDS-compatible clients and other callers built against fhir-server should not assume
byte-for-byte parity:

- **Body flags are honored**, not ignored. This is required for AHDS-compatible clients'
  `Parameters{hardDelete, purgeHistory}` body to take effect; fhir-server accepts the body but
  does not read it.
- **Unsupported search parameters and modifiers are always `400`**, where fhir-server silently
  drops or forwards some of them. A destructive bulk operation should fail closed on an
  unrecognized filter rather than risk deleting more than the caller intended.
- **`_remove-references` without hard delete is `400`.** fhir-server silently ignores the flag for
  soft delete and, for purge, strips references to resources that still exist — both are unsafe
  because the reference target is still live; we fail fast instead.
- **`$bulk-delete-soft-deleted` is not implemented.** fhir-server exposes a separate route to purge
  previously soft-deleted tombstones; that is out of scope for this ADR. Ignixa's hard delete only
  covers currently live (searchable) resources, matching fhir-server's own split.
- **No hard-delete-specific RBAC data action.** fhir-server gates hard delete behind a distinct
  data action; Ignixa requires `delete` on every type the job can reach (and `update` on every type
  for `_remove-references`), see [Authorization](#authorization), but no permission specific to
  hard delete.
- **Capability canonical fallback** is `http://hl7.org/fhir/OperationDefinition/bulk-delete` by
  convention (same as `$includes`), since there is no published canonical for this operation.

### Known limitations (as implemented)

- **Remove-references scope is per batch**, matching fhir-server. Each batch rewrites referrers of
  that batch's own deletions that lie outside that batch's delete set. A referrer a later batch
  deletes may first be rewritten (a new version) and then deleted; a referrer created after its
  target's batch already ran keeps a dangling reference.
- **Purge can double-count a resource updated mid-run.** Purge pages by the SQL keyset cursor
  (surrogate ID); an update during the run gives the resource a new surrogate ID past the cursor,
  so a later batch processes — and counts — it again. The extra count is harmless (purging twice is
  a no-op on the second pass); the same applies to its include cascade.
- **Counts can undercount by at most one batch after a retry or a cancel.** A batch that deletes
  and then fails before recording progress is retried without re-seeing the first attempt's
  deletions. A batch in flight when the job is cancelled finishes deleting its page but may lose
  its progress write to the now-`Cancelled` job, so that page's deletions go uncounted.

## Consequences

### Positive

- Satisfies AHDS-compatible deprovisioning clients' contract (body flags, async polling,
  `Retry-After`) without requiring any client-side change.
- Stays close enough to fhir-server's public contract that existing clients and the
  `ms-bulk-delete.json` TestScript work against Ignixa, aside from the documented deviations.
- Reuses the existing DurableTask job infrastructure (orchestration/activity pattern, tenant
  isolation, and the terminal-state authority) already used by `$export`/`$import`.
- Strict validation and the `_remove-references`/hard-delete coupling close two correctness gaps
  that fhir-server itself does not fully guard against.
- The new `HardDeleteAsync`/`PurgeHistoryAsync` repository API reuses the proven SQL hard-delete
  core instead of duplicating it, and makes the storage capability gap (FileSystem) explicit via
  `SupportsPhysicalDeletion` instead of a silent no-op.

### Negative / Risks

- There is no separate "hard delete" data action, unlike fhir-server: a caller with `delete` on every
  type the job reaches can trigger a hard delete and purge, not only a soft delete. Read-only,
  type-limited (for cascades), and compartment- or constraint-limited grants are refused (see
  [Authorization](#authorization)).
- `$bulk-delete` requires a durable job repository (`BackgroundJobs:Repository=SqlServer`) in
  production; with the in-memory default, status polls return `404` after a restart or on another
  scaled-out instance. The FileSystem DurableTask provider is dev/test only and does not complete
  multi-step orchestrations, so bulk-delete jobs hang on it.
- Hard delete only covers live (searchable) resources; resources already soft-deleted before the
  job runs are untouched, since `$bulk-delete-soft-deleted` is out of scope.
- `_revinclude=*:*` fan-out for `_remove-references` is per page, not globally bounded; it is
  bounded only by the configured batch size.
- The capability canonical is a convention, not a published HL7 canonical, since none exists for
  this operation.

## References

- fhir-server (`C:\src\fhir-server`, read-only reference):
  `src/Microsoft.Health.Fhir.Shared.Api/Controllers/BulkDeleteController.cs`,
  `src/Microsoft.Health.Fhir.Core/Features/Operations/BulkDelete/Handlers/GetBulkDeleteHandler.cs:50-180`,
  `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Delete/DeletionService.cs:435-612`,
  `KnownQueryParameterNames.cs:111-144`, `ReferenceRemover.cs`.
- [ADR 2510: Background Jobs with DurableTask Framework](../../adr/adr-2510-background-jobs.md)
- [ADR 2510: Multi-Tenancy](../../adr/adr-2510-multi-tenancy.md)
- [ADR 2510: Conditional CRUD Operations](../../adr/adr-2510-conditional-operations.md)
