---
sidebar_position: 2
title: Bulk Operations
description: Async $export and $import operations
---

# Bulk Operations

Ignixa supports FHIR Bulk Data Access specification for high-volume data exchange.

## $export

### System Export

Export all data from the server:

```bash
GET /$export
Accept: application/fhir+json
Prefer: respond-async
```

### Patient Export

Export all Patient compartment data:

```bash
GET /Patient/$export
Accept: application/fhir+json
Prefer: respond-async
```

### Group Export

Export data for a specific group of patients:

```bash
GET /Group/{group-id}/$export
Accept: application/fhir+json
Prefer: respond-async
```

### Export Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `_outputFormat` | Output format | `application/fhir+ndjson` |
| `_since` | Only resources modified since | `2024-01-01T00:00:00Z` |
| `_type` | Resource types to export | `Patient,Observation` |
| `_typeFilter` | Search filters per type | `Patient?active=true` |
| `_elements` | Elements to include | `id,meta,identifier` |

### Example with Parameters

```bash
GET /$export?_type=Patient,Observation&_since=2024-01-01T00:00:00Z&_outputFormat=application/fhir+ndjson
```

### Async Response

```
HTTP/1.1 202 Accepted
Content-Location: /$export-poll-status?_jobId=abc-123
```

### Poll Status

```bash
GET /$export-poll-status?_jobId=abc-123
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
  "request": "/$export?_type=Patient",
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

```bash
DELETE /$export-poll-status?_jobId=abc-123
```

## $import

Import bulk data into the server:

```bash
POST /$import
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

```json
{
  "BulkOperations": {
    "MaxConcurrentExports": 5,
    "MaxExportPageSize": 10000,
    "ExportStorageProvider": "BlobStorage",
    "RetentionDays": 7
  }
}
```

## Storage Integration

Export files are stored based on configuration:

### Azure Blob Storage

```json
{
  "BulkOperations": {
    "ExportStorageProvider": "BlobStorage",
    "BlobStorageConnectionString": "DefaultEndpointsProtocol=https;..."
  }
}
```

### File System

```json
{
  "BulkOperations": {
    "ExportStorageProvider": "FileSystem",
    "ExportPath": "/data/exports"
  }
}
```

## Performance Tips

1. **Use `_type`** - Export only needed resource types
2. **Use `_since`** - Incremental exports for efficiency
3. **Use `_elements`** - Reduce payload size
4. **Monitor progress** - Poll status for large exports

## Related Documentation

- [ADR: Background Jobs](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-background-jobs.md)
- [Azure Deployment](/docs/server/deployment/azure)
