---
sidebar_position: 1
title: Capability Statement
description: Server capabilities and conformance
---

# Capability Statement

The CapabilityStatement describes what the Ignixa FHIR server can do. Access it at:

```
GET /metadata
```

## Server Information

```json
{
  "resourceType": "CapabilityStatement",
  "status": "active",
  "kind": "instance",
  "fhirVersion": "4.0.1",
  "format": ["json"],
  "software": {
    "name": "Ignixa FHIR Server",
    "version": "1.0.0"
  }
}
```

## Supported Interactions

### Instance Level

| Interaction | Support | Notes |
|-------------|---------|-------|
| `read` | ✅ | Retrieve by ID |
| `vread` | ✅ | Retrieve specific version |
| `update` | ✅ | Full resource replacement |
| `patch` | ✅ | FHIRPath Patch, JSON Patch |
| `delete` | ✅ | Soft delete |
| `history-instance` | ✅ | Version history |

### Type Level

| Interaction | Support | Notes |
|-------------|---------|-------|
| `create` | ✅ | Server-assigned ID |
| `search-type` | ✅ | Search with parameters |
| `history-type` | ✅ | Type history |

### System Level

| Interaction | Support | Notes |
|-------------|---------|-------|
| `transaction` | ✅ | ACID bundles |
| `batch` | ✅ | Independent operations |
| `history-system` | ✅ | Full history |
| `search-system` | ✅ | Cross-resource search |

## REST Capabilities

```json
{
  "rest": [{
    "mode": "server",
    "security": {
      "cors": true,
      "service": [{
        "coding": [{
          "system": "http://terminology.hl7.org/CodeSystem/restful-security-service",
          "code": "SMART-on-FHIR"
        }]
      }]
    },
    "resource": [
      // Per-resource capabilities
    ]
  }]
}
```

## Operations

### Validate

```bash
POST /{type}/$validate
```

Validates a resource against FHIR specifications and profiles.

### Export (Bulk Data)

```bash
GET /$export
GET /Group/{id}/$export
```

Async bulk data export following the Bulk Data Access specification.

### Import (Bulk Data)

```bash
POST /$import
```

Async bulk data import with validation.

## Versioning

Ignixa supports resource versioning:

- `versionId` auto-increments on each update
- `lastUpdated` timestamp on every modification
- Full version history accessible via `/_history`

### Version-Aware Updates

Use `If-Match` header for optimistic concurrency:

```bash
PUT /Patient/123
If-Match: W/"5"
Content-Type: application/fhir+json

{ ... }
```

## Conditional Operations

### Conditional Create

```bash
POST /Patient
If-None-Exist: identifier=12345

{ ... }
```

### Conditional Update

```bash
PUT /Patient?identifier=12345

{ ... }
```

### Conditional Delete

```bash
DELETE /Patient?identifier=12345
```

## Formats

| Format | MIME Type | Support |
|--------|-----------|---------|
| JSON | `application/fhir+json` | ✅ Primary |
| NDJSON | `application/fhir+ndjson` | ✅ Bulk operations |

## Related Documentation

- [Supported Resources](/docs/server/fhir/supported-resources)
- [Search Parameters](/docs/server/fhir/search-parameters)
- [Operations](/docs/server/fhir/operations)
