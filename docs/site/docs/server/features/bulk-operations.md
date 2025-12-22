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

### Patient Export

Export all Patient compartment data:

```bash
POST /tenant/{tenantId}/Patient/$export
Accept: application/fhir+json
Prefer: respond-async
```

### Group Export

Export data for a specific group of patients:

```bash
POST /tenant/{tenantId}/Group/{group-id}/$export
Accept: application/fhir+json
Prefer: respond-async
```

### Export Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `_outputFormat` | Output format | `application/fhir+ndjson`, `application/vnd.apache.parquet` |
| `_since` | Only resources modified since | `2024-01-01T00:00:00Z` |
| `_type` | Resource types to export | `Patient,Observation` |
| `_typeFilter` | Search filters per type | `Patient?active=true` |
| `_elements` | Elements to include | `id,meta,identifier` |
| `_viewDefinition` | SQL on FHIR ViewDefinition ID (required for Parquet) | `patient-demographics` |

### Example with Parameters

```bash
# Standard NDJSON export
GET /$export?_type=Patient,Observation&_since=2024-01-01T00:00:00Z&_outputFormat=application/fhir+ndjson
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
GET /$export?_type=Patient&_outputFormat=application/vnd.apache.parquet&_viewDefinition=patient-demographics
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

Cancel a running export job:

```bash
DELETE /tenant/{tenantId}/_export/{jobId}
```

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
      "name": "inputSource",
      "valueUri": "https://storage.example.org/import/"
    },
    {
      "name": "input",
      "part": [
        { "name": "type", "valueCode": "Patient" },
        { "name": "url", "valueUri": "https://storage.example.org/import/Patient.ndjson" }
      ]
    },
    {
      "name": "input",
      "part": [
        { "name": "type", "valueCode": "Observation" },
        { "name": "url", "valueUri": "https://storage.example.org/import/Observation.ndjson" }
      ]
    }
  ]
}
```

### Import Response

```
HTTP/1.1 202 Accepted
Content-Location: /tenant/{tenantId}/_import/{jobId}
```

### Poll Import Status

```bash
GET /tenant/{tenantId}/_import/{jobId}
```

### Cancel Import

```bash
DELETE /tenant/{tenantId}/_import/{jobId}
```

### Import Options

| Parameter | Description |
|-----------|-------------|
| `inputFormat` | Format of input files |
| `inputSource` | Base URL for input files |
| `input` | Individual file specifications |
| `storageDetail` | Storage configuration |

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
3. **Use `_elements`** - Reduce payload size
4. **Monitor progress** - Poll status for large exports

## Related Documentation

- [Operations API Reference](/docs/server/fhir/operations#bulk-data-operations)
- [SQL on FHIR SDK](/docs/core-sdk/sql-on-fhir)
- [ADR: Background Jobs](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-background-jobs.md)
- [Azure Deployment](/docs/server/deployment/azure)
