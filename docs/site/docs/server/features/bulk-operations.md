---
sidebar_position: 2
title: Bulk Operations
description: Async $export, $import, and $bulk-delete operations
---

# Bulk Operations

Ignixa supports [FHIR Bulk Data Access](https://hl7.org/fhir/uv/bulkdata/) specification for high-volume data exchange,
plus a fhir-server/Azure Health Data Services-compatible `$bulk-delete` operation for bulk removal and deprovisioning.

See [Operations](/docs/server/fhir/operations#bulk-data-operations) for API reference and spec links.

## $export

### System Export

Export all data from the server (single-tenant mode):

```bash
POST /$export
Accept: application/fhir+json
Prefer: respond-async
```

Or with explicit tenant:

```bash
POST /tenant/{tenantId}/$export
Accept: application/fhir+json
Prefer: respond-async
```

### Group Export

Export data for a specific group of patients:

```bash
POST /Group/{group-id}/$export
Accept: application/fhir+json
Prefer: respond-async
```

The tenant-qualified Group route is supported in both modes and required when
multiple tenants are configured:

```bash
POST /tenant/{tenantId}/Group/{group-id}/$export?_type=Observation
Accept: application/fhir+json
Prefer: respond-async
```

Group exports intersect **every requested resource type** with the members' Patient
compartments, not just the Patient output. The Group must exist and enumerate patients
(`actual: true` in STU3/R4/R4B, `membership: enumerated` in R5). Nested local Groups are
expanded with cycle and duplicate detection; inactive members are excluded. Member
references without a local Patient or Group identity are rejected rather than treated as local patient IDs.
An empty Group produces an empty export. Types outside the Patient compartment produce
no Group output; they are never exported tenant-wide.
Configure `Fhir:BaseUri` for absolute self-references. Background workers establish
their own tenant context, so an absolute member reference such as
`https://fhir.example/tenant/1/Patient/p` is recognized consistently at HTTP kickoff
and during tenant 1's export. External and other-tenant references are rejected;
workers restore any previous context on both success and failure.

### Export Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `_outputFormat` | Output format | `application/fhir+ndjson`, `application/vnd.apache.parquet` |
| `_since` | Resources modified at or after the cutoff (inclusive `_lastUpdated` filtering) | `2024-01-01T00:00:00Z` |
| `_type` | Resource types to export; omitted means all applicable concrete types in the tenant schema | `Patient,Observation` |
| `_typeFilter` | Search filters per type | `Patient?active=true` |
| `_viewDefinition` | SQL on FHIR ViewDefinition ID (required for Parquet) | `patient-demographics` |

New jobs snapshot the type list before orchestration starts, including tenant-defined
resource types. System exports include all concrete schema types; Group exports default
to Patient and its compartment types. An explicit `_type` preserves the requested subset.
Previously persisted jobs with an empty type list retain their original six-type behavior
so DurableTask history can replay unchanged.

`_since`, `_typeFilter`, Group membership, and partition boundaries are intersected.
SQL applies the cutoff using its millisecond-resolution resource timestamps, including
all resources at the cutoff millisecond. Each partition is exhausted in pages of at most
1,000 selected matches plus a non-rendered lookahead. SQL continuation uses the last
selected resource's surrogate boundary, not the number successfully materialized.
If a selected resource disappears before fetching, later unchanged resources are still
visited; a missing lookahead also retains its continuation signal. A partition containing
more than 50,000 resources is not truncated. Bulk output has no ordering guarantee;
workers use the provider's stable traversal order rather than `_sort`.
The file provider also excludes lookahead rows from output. If a selected non-probe
file disappears, it fails the export explicitly because its positional cursor cannot
safely continue after that loss.
Export paging does not provide a database snapshot across concurrent updates or deletes.

### Example with Parameters

```bash
# Standard NDJSON export
POST /$export?_type=Patient,Observation&_since=2024-01-01T00:00:00Z&_outputFormat=application/fhir+ndjson
```

## Parquet Export (SQL on FHIR)

Export FHIR data directly to Apache Parquet format using [SQL on FHIR v2](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/) ViewDefinitions. This enables direct analytics integration with tools like Spark, Databricks, and Snowflake.

### Creating a ViewDefinition

First, create a ViewDefinition resource that defines the tabular projection:

```bash
POST /ViewDefinition
Content-Type: application/fhir+json

{
  "resourceType": "ViewDefinition",
  "id": "patient-demographics",
  "name": "patient_demographics",
  "resource": "Patient",
  "select": [
    { "column": [{ "name": "id", "path": "id" }] },
    { "column": [{ "name": "family_name", "path": "name.first().family" }] },
    { "column": [{ "name": "given_name", "path": "name.first().given.first()" }] },
    { "column": [{ "name": "birth_date", "path": "birthDate" }] },
    { "column": [{ "name": "gender", "path": "gender" }] }
  ]
}
```

### Exporting to Parquet

```bash
POST /$export?_type=Patient&_outputFormat=application/vnd.apache.parquet&_viewDefinition=patient-demographics
Accept: application/fhir+json
Prefer: respond-async
```

### Parquet Export Response

```json
{
  "transactionTime": "2024-01-15T10:30:00Z",
  "request": "/$export?_type=Patient&_outputFormat=application/vnd.apache.parquet&_viewDefinition=patient-demographics",
  "requiresAccessToken": false,
  "output": [
    {
      "type": "Patient",
      "url": "https://storage.example.org/export/patient_demographics.parquet",
      "count": 15420
    }
  ]
}
```

### Benefits of Parquet Export

- **Analytics-Ready**: Direct integration with Spark, Databricks, Snowflake, BigQuery
- **Columnar Storage**: Efficient compression and query performance
- **Schema Preservation**: Strong typing from ViewDefinition
- **Reduced Transform**: No ETL pipeline needed for analytics

See [SQL on FHIR SDK](/docs/core-sdk/sql-on-fhir) for ViewDefinition authoring details.

### Async Response

```
HTTP/1.1 202 Accepted
Content-Location: /tenant/{tenantId}/_export/{jobId}
```

### Poll Status

Check the status of an export job:

```bash
GET /tenant/{tenantId}/_export/{jobId}
```

#### In Progress

```
HTTP/1.1 202 Accepted
X-Progress: Exporting... 45%
Retry-After: 1
```

#### Complete

```json
{
  "transactionTime": "2024-01-15T10:30:00Z",
  "request": "/tenant/{tenantId}/$export?_type=Patient",
  "requiresAccessToken": false,
  "output": [
    {
      "type": "Patient",
      "url": "https://storage.example.org/export/Patient.ndjson",
      "count": 15420
    },
    {
      "type": "Observation",
      "url": "https://storage.example.org/export/Observation.ndjson",
      "count": 892341
    }
  ]
}
```

### Cancel Export

An export can produce multiple files for the same resource type. Download **every** entry in
`output`; each entry identifies one partition and reports its resource count. Older persisted
jobs may omit per-file counts. URLs come from the configured blob provider: local storage
returns `file://` URLs, while Azure storage can return signed URLs.
Partitions confirmed to contain zero resources are omitted because the NDJSON writer does
not create an empty blob. A successful export filtered down to zero resources returns an
empty `output` array; a missing populated file is still a failure.
For older jobs affected by the `tenant/` versus `partition/` path-prefix defect, polling
uses the matching worker file only after confirming it exists in the configured provider.
Legacy path recovery uses the stored job owner, including when another permitted shard
polls the job in Distributed mode.

Polling returns an `OperationOutcome` with HTTP 500 for failed jobs or malformed persisted
results, and HTTP 410 for cancelled jobs. These states are not successful empty exports.

Cancel a running export job:

```bash
DELETE /tenant/{tenantId}/_export/{jobId}
```

The first `Completed`, `Failed`, or `Cancelled` outcome is authoritative. Repeating
cancellation for an already cancelled job returns 204. If completion or failure wins
before cancellation is saved, cancellation returns 409 with an OperationOutcome and
does not relabel the job. A retry or requeue starts a new job ID.
If the job has been removed, including before an authoritative reload after a cancellation
race, cancellation returns 404.

## $import

Import bulk data into the server. Imports use the DurableTask framework for reliability and progress tracking.

```bash
POST /tenant/{tenantId}/$import
Content-Type: application/fhir+json
Prefer: respond-async

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "inputFormat",
      "valueCode": "application/fhir+ndjson"
    },
    {
      "name": "input",
      "part": [
        { "name": "type", "valueCode": "Patient" },
        { "name": "url", "valueUri": "import/Patient.ndjson" }
      ]
    },
    {
      "name": "input",
      "part": [
        { "name": "type", "valueCode": "Observation" },
        { "name": "url", "valueUri": "import/Observation.ndjson" }
      ]
    }
  ]
}
```

### Import Response

Input files are read through the configured blob provider. The paths above refer to files
under that provider's root/container, not arbitrary external HTTP downloads. Each nonblank
NDJSON line must contain a resource of the declared `input.type`.

```
HTTP/1.1 202 Accepted
Content-Location: /tenant/{tenantId}/_import/{jobId}
```

### Poll Import Status

```bash
GET /tenant/{tenantId}/_import/{jobId}
```

While queued or running, polling returns HTTP 202 with `X-Progress` and `Retry-After`.
New jobs use `Import:MaxConcurrentFiles`; the value must be positive and is validated
before a job is queued. Completed-file progress is saved before the next group starts.
A rejected checkpoint stops further scheduling. Older persisted jobs retain their recorded
activity ordering, including the original pair-wise waits and checkpoint order.
Completion returns HTTP 200 with the successfully imported resource count and, when records
were rejected, an `error` entry containing the rejected count and a URL for an NDJSON
OperationOutcome log in the configured blob provider. Partial imports retain successful
resources and report rejected records rather than silently dropping them.
Writes commit in batches of at most 1,000 resources; input line numbers do not become
database offsets. Index-extraction failures are reported as rejected records rather than
writing resources without searchable indexes.
Allocation, write, and commit failures are fatal processing failures, not rejected resource
rows. A failed commit may have persisted data: the job reports an indeterminate outcome
instead of completing with those resources in its rejection count. Fatal result metadata
marks counts as incomplete/lower bounds and records unknown write outcomes explicitly.

Fatal import failures return HTTP 500 with an OperationOutcome; cancellation returns HTTP
410. Completion and failure metadata are persisted independently of status polling.
Late polling, progress, or completion updates reload the authoritative terminal record
rather than replacing it. Superseded work is logged separately from storage failures.
Error artifacts use attempt-specific paths, so a losing completion cannot replace the
error file referenced by the winning result.
If error-artifact storage is unavailable, healthy job storage still records Failed status
and the original/upload diagnostics, without a nonexistent artifact URL. Finalization is
not recursively retried through the same failing upload path.

### Cancel Import

```bash
DELETE /tenant/{tenantId}/_import/{jobId}
```

As with export, repeating a completed cancellation returns 204; cancellation that loses
to completion or failure returns 409 without changing the stored outcome.
Missing jobs return 404, including removal before the authoritative conflict reload.

### Import Options

| Parameter | Description |
|-----------|-------------|
| `inputFormat` | Format of input files |
| `input` | Individual file specifications |
| `mode` | `IncrementalLoad` (default) or `InitialLoad`; both use the current batch-upsert path |

## $bulk-delete

`$bulk-delete` is a fhir-server/Azure Health Data Services-compatible, DurableTask-backed background
job that deletes matching resources in batches. It is **not an HL7 FHIR specification operation**;
Ignixa implements it to satisfy AHDS-compatible clients (such as deprovisioning flows) built against
fhir-server's `$bulk-delete` contract.

For runnable requests, open
[`docs/rest/operation-bulk-delete.http`](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/rest/operation-bulk-delete.http).
It seeds tagged sample resources and covers soft delete, hard delete, history purge,
system-level Parameters-body requests, exclusions, polling, cancellation, and no-match results.
Run one scenario at a time in a disposable tenant; every delete is restricted to the sample IDs
and tag.

### Kickoff Routes

```bash
# System-level (every concrete resource type in the tenant schema)
DELETE /$bulk-delete
DELETE /tenant/{tenantId}/$bulk-delete

# Type-level (a single resource type)
DELETE /{resourceType}/$bulk-delete
DELETE /tenant/{tenantId}/{resourceType}/$bulk-delete
```

There is no compartment-level route (matching fhir-server). Every request, including the
tenant-explicit form, requires the `Prefer: respond-async` header:

```bash
DELETE /$bulk-delete?_type=Patient,Observation
Prefer: respond-async
```

A missing `Prefer: respond-async` token returns `400`. The header may carry other tokens too
(for example `Prefer: respond-async, handling=strict`); only the `respond-async` token is required.

### Parameters

| Parameter | Location | Description |
|-----------|----------|-------------|
| `_hardDelete` / `hardDelete` | query or body (`valueBoolean`) | Physically remove each matched resource: every version, its search indexes, and its TTL entry, instead of soft-deleting it. Requires a storage provider with `SupportsPhysicalDeletion`. |
| `_purgeHistory` | query or body (`valueBoolean`) | Physically remove the resource's historical versions, keeping the current version. Ignored when `_hardDelete`/`hardDelete` is also set. |
| `excludedResourceTypes` | query (comma-separated, repeatable) | Resource types never deleted, including from the `_include`/`_revinclude` cascade. Unknown type names return `400`. |
| `_remove-references` / `removeReferences` | query only (`true`/`false`) | For each hard-deleted resource, rewrite referrers found via `_id={target}&_revinclude=*:*`: remove the `reference` element and set `display` to `"Referenced resource deleted"`, saving a new version. A reference is recognized in every form the search index resolves to the target — relative (`Patient/p1`), versioned (`Patient/p1/_history/2`), and absolute under one of this server's base URIs (see the note on `Fhir:BaseUri` under [Known Limitations](#known-limitations)). A reference under another server's base names a different resource and is left alone. **Requires hard delete**; with soft delete or purge it returns `400`. Not accepted in the request body (a body parameter by this name returns `400`). |
| `_type` | query, system-level route only | Comma-separated resource types to restrict the delete to, intersected with the tenant schema's concrete types. Not allowed on the type-level route (`400`). |
| `_include` / `_revinclude` | query | Cascade matches to their included/reverse-included resources, which are deleted alongside the matches (includes first, then matches). Excluded types are filtered out of the includes. Only supported by storage providers that evaluate includes (SQL); on providers that do not (FileSystem), these return `400`. |
| any other search parameter | query | Forwarded to search to filter which resources are deleted (for example `identifier`, `status`, `_lastUpdated`). |

Every query parameter not listed above is treated as a search filter and validated strictly: an
empty value (`?status=`), an unsupported parameter, an unsupported modifier, or (at the system
route) a parameter invalid for any one of the targeted resource types all return `400`, naming the
offending parameter and, at the system level, the resource type. Because bulk delete is
destructive, Ignixa never silently drops or ignores an unrecognized filter the way a regular
search would — doing so would widen what gets deleted. Result-shaping parameters are rejected
outright: `_count`, `_sort`, `_summary`, `_elements`, `_total`, `_contained`, and `_containedType`.

### Optional Parameters Body

In addition to the query-string flags above (the only flags fhir-server itself recognizes),
Ignixa also reads `hardDelete`/`purgeHistory` from an optional FHIR `Parameters` request body, to
satisfy AHDS-compatible clients that send the flags in the body rather than the query string:

```bash
DELETE /$bulk-delete?_type=Patient,Observation
Prefer: respond-async
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "hardDelete", "valueBoolean": true },
    { "name": "purgeHistory", "valueBoolean": true }
  ]
}
```

The body is optional; when a body is sent, it must be a `Parameters` resource whose only
parameters are `hardDelete`/`_hardDelete` and `purgeHistory`/`_purgeHistory` as `valueBoolean`.
Any other parameter name, a non-boolean value, malformed JSON, or a non-`Parameters` resource
returns `400`. `application/fhir+json` and `application/json` are both accepted.

**Conflict rule:** if a flag is set in both the query string and the body with different values
(for example `?_hardDelete=false` with `{"name":"hardDelete","valueBoolean":true}` in the body), the
request returns `400` rather than silently preferring one source over the other.

### Mode Precedence

The effective deletion mode, from either source, is: **hard delete > purge history > soft delete**.
`_hardDelete`/`hardDelete=true` always wins; `_purgeHistory`/`purgeHistory=true` only takes effect
when hard delete is not also set; with neither flag set, matched resources are soft-deleted (the
same as a regular `DELETE`).

### Strict Validation (400 responses)

Because an ignored or widened filter would delete more than intended, kickoff validation is
intentionally stricter than ordinary search and returns `400 Bad Request` with an
`OperationOutcome` for:

- A missing `Prefer: respond-async` header.
- An invalid boolean value for `_hardDelete`/`hardDelete`, `_purgeHistory`, or `_remove-references`
  (anything other than `true`/`false`, case-insensitively).
- The same control flag repeated with conflicting values in the query string.
- A flag set to conflicting values across the query string and the body.
- `_remove-references` without hard delete.
- `excludedResourceTypes` naming an unsupported/unknown resource type.
- `_type` naming an unsupported/unknown resource type, or an empty `_type` value.
- `_type` supplied on the type-level route (`/{resourceType}/$bulk-delete`).
- `excludedResourceTypes` excluding the type-level route's target resource type.
- No resource types remaining after applying `_type` and `excludedResourceTypes`.
- A search parameter with an empty or whitespace-only value (for example `?status=`) — a dropped
  filter would widen the delete rather than narrow it.
- A search parameter or modifier the server does not support (`_count`, `_sort`, `_summary`,
  `_elements`, `_total`, `_contained`, `_containedType`, or any parameter invalid for one of the
  targeted resource types).
- `_hardDelete`/`_purgeHistory` on a storage provider that does not support physical deletion
  (FileSystem).
- `_include`/`_revinclude` on a storage provider that does not evaluate includes (FileSystem).
- A non-`Parameters` or malformed JSON request body, or a body parameter other than
  `hardDelete`/`purgeHistory`.

### Kickoff Response

```
HTTP/1.1 202 Accepted
Content-Location: https://fhir.example.com/tenant/{tenantId}/_operations/bulk-delete/{jobId}
```

The response body is empty, matching fhir-server. `Content-Location` is an absolute URL built from
the request's scheme, host, and path base, and mirrors the kickoff route form: the agnostic routes
return an agnostic status URL (`https://fhir.example.com/_operations/bulk-delete/{jobId}`), and
tenant-explicit routes return a `/tenant/{tenantId}/...` status URL.

### Authorization

When authorization is enabled (`Authorization:Enabled`, the default), a kickoff must pass the
route-level check every operation gets (`operation-system`/`operation-type`) **and** a
bulk-delete-specific check of the interactions the job will actually perform. The second check runs
through the same RBAC/SMART pipeline as any other request:

| Request | Required grant |
|---------|----------------|
| Type-level (`/{resourceType}/$bulk-delete`) | `delete` on `{resourceType}` |
| System-level with `_type=A,B` | Route-level: `system/*.s` (status polling) or `system/*.d` (delete/cancel). Then `delete` on each of `A` and `B` for the bulk-delete authorizer. |
| System-level without `_type` | `delete` on `*` (every resource type) |
| Any `_include` / `_revinclude` (including `:iterate`) | `delete` on `*`: the cascade reaches types the request does not name |
| `_remove-references=true` | additionally `update` on `*`: referrers of any type are rewritten |

`excludedResourceTypes` only narrows a job, so it does not reduce the required grants. In SMART
terms, `system/Patient.d` (or `.cud`) permits `DELETE /Patient/$bulk-delete`, but a system-level
delete or one with `_revinclude` needs `system/*.d`. A system-level delete with `_type=A,B` also
needs `system/*.s` for the route-level check before the bulk-delete authorizer validates the
specific types. A read-only scope such as `patient/Patient.r` permits no bulk delete at all.

A grant that carries a data restriction is refused even if it includes delete: patient- or
practitioner-context SMART scopes (for example `patient/*.cruds`) and scopes with SMART v2 search
constraints (for example `system/Patient.d?identifier=...`). The job deletes everything the request
matches, not only what the caller is allowed to see, so a compartment- or constraint-limited token
can never start one.

Denials return `403 Forbidden` with an `OperationOutcome` (`code: forbidden`) whose diagnostics name
the missing grant. With local RBAC, the route-level check still applies, so a role also needs the
`operation-type`/`operation-system` interaction (or `*`) in addition to `delete`.

Status and cancel use the generic route classification: polling
`GET .../_operations/bulk-delete/{jobId}` requires `search-system` on `*` (SMART `*.s`), and
cancelling requires `delete` on `*` (SMART `*.d`). A caller granted only `delete` on specific types
can start a type-level job, but it needs those broader grants to poll or cancel it.

### Poll Status

```bash
GET /tenant/{tenantId}/_operations/bulk-delete/{jobId}
```

The response is always a FHIR `Parameters` resource (`Content-Type: application/fhir+json`). An
`Issues` parameter (an `OperationOutcome`) is present for in-progress, cancelled, and failed jobs; a
`ResourceDeletedCount` parameter lists only resource types with a count greater than zero. When
there is nothing to report, `parameter` is omitted entirely (FHIR JSON forbids an empty array).
Counts use `valueInteger64` (a JSON string) on R4/R4B/R5/R6; STU3 has no `integer64` type, so it
uses `valueDecimal` (a JSON number) instead.

#### In Progress (`Queued` or `Running`)

```
HTTP/1.1 202 Accepted
Progress: In Progress
Retry-After: 1
```

```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "Issues",
      "resource": {
        "resourceType": "OperationOutcome",
        "issue": [
          { "severity": "information", "code": "informational", "diagnostics": "Job In Progress" }
        ]
      }
    },
    {
      "name": "ResourceDeletedCount",
      "part": [
        { "name": "Patient", "valueInteger64": "1204" }
      ]
    }
  ]
}
```

`ResourceDeletedCount` is present only once at least one batch has recorded progress.

#### Completed

```
HTTP/1.1 200 OK
```

```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "ResourceDeletedCount",
      "part": [
        { "name": "Patient", "valueInteger64": "2" },
        { "name": "Observation", "valueInteger64": "14" }
      ]
    }
  ]
}
```

A completed job with nothing to delete (no matches) returns `Parameters` with `parameter` omitted.

#### Cancelled

```
HTTP/1.1 200 OK
```

```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "Issues",
      "resource": {
        "resourceType": "OperationOutcome",
        "issue": [
          { "severity": "warning", "code": "informational", "diagnostics": "Job Canceled" }
        ]
      }
    },
    {
      "name": "ResourceDeletedCount",
      "part": [
        { "name": "Patient", "valueInteger64": "512" }
      ]
    }
  ]
}
```

#### Failed

```
HTTP/1.1 500 Internal Server Error
```

```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "Issues",
      "resource": {
        "resourceType": "OperationOutcome",
        "issue": [
          { "severity": "error", "code": "exception", "diagnostics": "Bulk delete failed while processing 'Patient': ..." }
        ]
      }
    },
    {
      "name": "ResourceDeletedCount",
      "part": [
        { "name": "Patient", "valueInteger64": "87" }
      ]
    }
  ]
}
```

A job ID that does not exist for the tenant, including one belonging to a different job type
(for example an export job ID), returns `404` with an `OperationOutcome`. A job with a corrupted
persisted result or progress record returns `500` with an `OperationOutcome`.

### Cancel

```bash
DELETE /tenant/{tenantId}/_operations/bulk-delete/{jobId}
```

- **`202 Accepted`** (empty body) — the job was queued or running and is now cancelled. Deletions
  already performed by completed batches are kept and reflected in the final counts; a batch that
  was in flight when cancellation landed finishes deleting its current page, but may not get to
  record that page's counts (see [Known limitations](#known-limitations)).
- **`409 Conflict`** — the job already reached `Completed`, `Failed`, or `Cancelled`; the
  `OperationOutcome` names the terminal status. Cancellation never un-terminates a job or changes
  its recorded outcome.
- **`404 Not Found`** — no bulk-delete job with this ID exists for the tenant (including a job
  belonging to a different tenant or a different job type).

### No-Match Behavior

A bulk-delete job with no matching resources still runs to completion; it is not a `404` or a
no-op. It completes normally with `parameter` omitted from the status response (no counts to
report).

### Tenant Isolation

Every bulk-delete job snapshots its owning `TenantId` at kickoff. Status and cancel requests return
`404` unless the job belongs to the requesting tenant **and** is a bulk-delete job — the background
job repository does not filter by job type on its own, and it validates the tenant only in Isolated
deployment mode, so the handlers check both explicitly. Each batch activity resolves its own
tenant-scoped repository and search service from the job's stored `TenantId`, not from the ambient
request, so a job's execution is unaffected by whichever request happens to poll it.
`/tenant/0/...` remains rejected by tenant-resolution middleware, as with every other route.

### FileSystem Storage Limits

The bundled FileSystem storage provider is an append-only prototype
(`IFhirRepository.SupportsPhysicalDeletion` is `false`) and does not evaluate `_include`/
`_revinclude`. Kickoff rejects the following with `400` on FileSystem rather than silently
performing a different delete than requested:

- `_hardDelete`/`hardDelete=true`
- `_purgeHistory=true`
- `_include` or `_revinclude`

Soft delete (the default, with none of the above) is supported on every storage provider. SQL
Server supports hard delete, purge history, and the include cascade.

### Batch Size

Batch size — how many matches (and their includes) are searched and deleted per batch activity —
is configurable via `BulkDelete:BatchSize`. It must be between 1 and 10,000 and defaults to 500.
See [Bulk Delete Configuration](#bulk-delete-configuration) below for a sample `appsettings.json`
snippet. The configured value is snapshotted into each job's orchestration input at kickoff, so
changing it only affects jobs started afterwards.

### Known Limitations

- **Remove-references scope is per batch** (matching fhir-server). Each batch rewrites referrers of
  that batch's deletions that are outside that batch's own delete set. A referrer that a later
  batch deletes may first be rewritten (a new version) and then deleted; a referrer created after
  its target's batch already ran keeps a dangling reference.
- **Purge can double-count a resource updated mid-run.** Purge pages matches using the storage
  provider's keyset cursor (surrogate ID). An update during the run gives the resource a new
  surrogate ID past the cursor, so a later batch processes (and counts) it again. Purging it twice
  is harmless — the reported count is simply one higher — and the same applies to its include
  cascade.
- **Counts can undercount by at most one batch after a retry or a cancel.** A batch that deletes
  resources and then fails before recording progress is retried; the retry does not see the first
  attempt's deletions, so they go uncounted. A batch in flight when the job is cancelled finishes
  deleting its page but may lose its progress write to the now-`Cancelled` job, so that page's
  deletions are not reflected in the final counts either.
- `$bulk-delete-soft-deleted` (purging previously soft-deleted tombstones, a separate fhir-server
  route) is not implemented. Hard delete and purge only act on currently live (searchable)
  resources.
- There is no separate "hard delete" authorization/data-action distinct from ordinary delete
  access: hard delete and purge require the same `delete` grants as soft delete (see
  [Authorization](#authorization)).
- `_revinclude=*:*` fan-out for `_remove-references` is bounded only by the configured batch size,
  not globally.
- **`_remove-references` needs `Fhir:BaseUri` configured to recognize self-absolute references.**
  A batch runs outside the HTTP pipeline, so its request context carries no service base URIs and
  `Fhir:BaseUri` (plus tenant addressing) is what identifies "this server" — not the host of the
  request that stored the reference, and not the host of the request that started the job. With no
  configured base, a reference written as `https://this-server/Patient/p1` is treated as pointing
  at another server and is left in place, even though the search index collapsed it onto the
  target and so reported the referrer. The same mismatch already applies to indexing itself
  (background-indexed rows store self-references as external while request-indexed rows collapse
  them), which is why `Fhir:BaseUri` is not optional in practice. A referrer skipped this way is
  logged at warning naming the referrer and the target, so it is visible rather than silent.
- **A page whose matches cannot be deleted fails the job.** Soft and hard delete re-read the first
  page every batch, because deleted matches drop out of it. If a page's matches survive the batch
  and more matches remain, the next batch reads the same page, and the job fails with
  `A batch made no progress: '{Type}/{id}' still leads the matches after it was processed.` rather
  than looping forever. In practice this means a search index that still matches a resource the
  store cannot delete — the counts already recorded stay, and the job is safe to re-run once the
  underlying inconsistency is resolved. A page like that as the *last* page of a type is not
  detected: there is no next batch to compare against, so the job completes with those matches
  undeleted. The per-batch log line records matches found against resources deleted, which is
  where that shows up.

### Compatibility with microsoft/fhir-server and Azure Health Data Services

Ignixa's `$bulk-delete` follows fhir-server's public HTTP contract (routes, query flags, response
shapes, and cancel semantics) closely enough that fhir-server/AHDS clients and the
`ms-bulk-delete.json` TestScript work unmodified, with these intentional differences:

- **The optional `Parameters` body is honored, not ignored.** fhir-server accepts a request body
  but does not read it — only query-string flags take effect. Ignixa additionally reads
  `hardDelete`/`purgeHistory` from the body, because AHDS-compatible clients send their flags that
  way; a query/body conflict is `400` rather than one source silently winning.
- **Unsupported search parameters and modifiers always return `400`.** fhir-server silently drops
  or forwards some of them; Ignixa fails closed instead, because an ignored filter widens a
  destructive delete.
- **`_remove-references` without hard delete is `400`.** fhir-server silently ignores the flag for
  soft delete, and for purge it strips references to resources that are still live — both are
  unsafe, so Ignixa rejects the combination instead.
- **No compartment-level route**, matching fhir-server (there is no `Patient/{id}/$bulk-delete`).
- **No separate hard-delete data action.** fhir-server gates hard delete behind a distinct
  authorization data action; Ignixa requires `delete` on every resource type the job can reach
  (and `update` on every type for `_remove-references`), but no grant specific to hard delete.
- **The capability-statement canonical is a convention**
  (`http://hl7.org/fhir/OperationDefinition/bulk-delete`, the same pattern `$includes` uses), since
  there is no published HL7 canonical for this non-spec operation.

## DurableTask Framework

Bulk operations use the DurableTask framework for reliability:

```
┌─────────────────┐
│  Export Request │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Orchestrator   │ Coordinates export
└────────┬────────┘
         │
    ┌────┴────┐
    ▼         ▼
┌───────┐ ┌───────┐
│Task 1 │ │Task 2 │ Export by type
└───────┘ └───────┘
         │
         ▼
┌─────────────────┐
│   Completion    │ Status update
└─────────────────┘
```

### Benefits

- **Durability** - Survives process restarts
- **Parallelism** - Concurrent type processing
- **Progress** - Real-time status updates
- **Checkpointing** - Resume from failures

## Configuration

Bulk operations use DurableTask framework for orchestration and BlobStorage for file storage. Configure in appsettings.json:

Persist both orchestration state and job metadata for restart-safe production polling.
SQL-backed job metadata retains progress, output paths, counts, and terminal diagnostics
even after orchestration history has been removed. Tenant-scoped polling does not expose
another tenant's jobs.

### DurableTask Configuration

```json
{
  "DurableTask": {
    "Provider": "SqlServer",
    "SqlServer": {
      "TaskHubName": "ignixa"
    }
  }
}
```

Providers:
- `SqlServer` - Uses SQL Server (default, integrated with FHIR database)
- `AzureStorage` - Uses Azure Storage for distributed scenarios
- `FileSystem` - Development/testing only. It does not complete multi-step orchestrations:
  `$bulk-delete` jobs stay `Running` after their first batch, so use `SqlServer` or `AzureStorage`.
  Tracked as [#483](https://github.com/brendankowitz/ignixa-fhir/issues/483).

### Background Job Repository

```json
{
  "BackgroundJobs": {
    "Repository": "SqlServer"
  }
}
```

`$export`, `$import`, and `$bulk-delete` track their status in job rows that the status endpoints
read. The default `InMemory` repository keeps those rows in one process, so after a restart, or on
another scaled-out instance, a status poll returns `404` for a job that is still running or already
finished. Production deployments need the durable `SqlServer` repository, which stores the rows in
tenant 1's database. The Azure deployment templates set `BackgroundJobs__Repository=SqlServer`.

### Blob Storage Configuration

```json
{
  "BlobStorage": {
    "Provider": "Local",
    "RootDirectory": "fhir-exports",
    "ContainerName": "fhirstorage"
  }
}
```

Or for Azure:

```json
{
  "BlobStorage": {
    "Provider": "Azure",
    "ContainerName": "fhirstorage",
    "StorageAccountUri": "https://yourAccount.blob.core.windows.net",
    "UseManagedIdentity": true
  }
}
```

### Import Configuration

```json
{
  "Import": {
    "MaxConcurrentFiles": 1,
    "ConsumerCount": 1,
    "BatchSize": 100,
    "ChannelCapacity": 1000
  }
}
```

### Bulk Delete Configuration

```json
{
  "BulkDelete": {
    "BatchSize": 500
  }
}
```

`BatchSize` must be between 1 and 10,000; it defaults to 500 when the section is omitted.

## Performance Tips

1. **Use `_type`** - Export only needed resource types
2. **Use `_since`** - Incremental exports for efficiency
3. **Monitor progress** - Poll status for large exports

## Related Documentation

- [Operations API Reference](/docs/server/fhir/operations#bulk-data-operations)
- [SQL on FHIR SDK](/docs/core-sdk/sql-on-fhir)
- [ADR: Background Jobs](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-background-jobs.md)
- [Azure Deployment](/docs/server/deployment/azure)
