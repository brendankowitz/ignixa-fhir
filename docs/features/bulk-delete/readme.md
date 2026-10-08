# Feature: `$bulk-delete` Operation

**Status**: Decided (implemented; ADR pending `/accept-adr`, see [ADR 2610](adr-2610-bulk-delete-operation.md))
**Created**: 2026-10-06

## Problem Statement

AHDS-compatible deprovisioning clients deprovision a FHIR service's data by calling fhir-server's
`$bulk-delete` operation: a system-level `DELETE $bulk-delete?_type=…` with
`Prefer: respond-async`, hard-delete and purge flags in a `Parameters` body, then polling
`Content-Location` until a non-202 2xx status. Before this feature, Ignixa did not implement
`$bulk-delete`, so it could not sit behind such a deprovisioning flow. fhir-server itself ignores
the body flags and only honors query-string flags, which means a server that mirrors it exactly
would soft-delete when these clients expect a hard delete with history purge.

## Constraints

- **AHDS-compatible deprovisioning prerequisite.** The operation must satisfy existing
  fhir-server/AHDS callers' call pattern (async kickoff, `Content-Location` polling,
  `Retry-After`) without requiring a client-side change.
- **fhir-server/AHDS compatibility.** The HTTP contract (routes, flags, status shapes, cancel
  semantics) follows fhir-server's `$bulk-delete` so existing FHIR clients and the
  `ms-bulk-delete.json` TestScript behave the same way, except where this ADR records an explicit
  deviation.
- **Multi-tenant isolation.** Every kickoff, status, and cancel request is scoped to the resolved
  tenant; the job definition snapshots the tenant and resource type set at kickoff so replay is
  deterministic.
- **Tenant 0 is never exposed.** `/tenant/0/...` routes are already rejected by
  `TenantResolutionMiddleware`; `$bulk-delete` introduces no exception to that rule.

## Decision

See [ADR 2610: `$bulk-delete` Operation](adr-2610-bulk-delete-operation.md).

## Implementation Notes

Implemented as a DurableTask-backed background job (`BulkDeleteEndpoints`,
`CreateBulkDeleteJobHandler`, `BulkDeleteOrchestration`/`BulkDeleteBatchActivity`), following the
ADR's contract. Changes made during review (round 1), beyond the original design:

- A search parameter with an empty/whitespace value (e.g. `?status=`) is kickoff-rejected (`400`),
  since a dropped filter would widen the delete rather than narrow it.
- `_include`/`_revinclude` are kickoff-rejected (`400`) when the tenant's storage provider does not
  evaluate includes (FileSystem), instead of silently running without the cascade.
- Status/cancel job lookups use the typed `IBackgroundJobRepository<BulkDeleteJobDefinition>.GetAsync`
  overload, which filters by job type in SQL before deserializing, so polling an ID that belongs to
  a different job type (e.g. an export job) returns `404` instead of a `500` deserialization error.
- The orchestration calls `ContinueAsNew` every 100 batches, carrying its traversal position and
  cumulative counts forward, so DurableTask history stays bounded for large deletions.
- Purge-history traversal pages matches with the SQL provider's keyset continuation token and reads
  each page's `_include`/`_revinclude` cascade with a separate chunked `_id={...}` search over that
  page's matches, since the keyset cursor cannot carry includes itself.

See [Bulk Operations: $bulk-delete](../../site/docs/server/features/bulk-operations.md#bulk-delete)
for the user-facing parameter reference, request/response examples, and the full list of known
limitations (remove-references scope, purge double-counting, and undercounting after a retry or
cancel).
