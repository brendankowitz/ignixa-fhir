---
sidebar_position: 2
title: Bulk Operations
description: Async $export and $import operations
---

# Bulk Operations

Ignixa supports [FHIR Bulk Data Access](https://hl7.org/fhir/uv/bulkdata/) specification for high-volume data exchange.

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
- `FileSystem` - Development/testing only

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

## Performance Tips

1. **Use `_type`** - Export only needed resource types
2. **Use `_since`** - Incremental exports for efficiency
3. **Monitor progress** - Poll status for large exports

## Related Documentation

- [Operations API Reference](/docs/server/fhir/operations#bulk-data-operations)
- [SQL on FHIR SDK](/docs/core-sdk/sql-on-fhir)
- [ADR: Background Jobs](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-background-jobs.md)
- [Azure Deployment](/docs/server/deployment/azure)
