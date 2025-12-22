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

:::note JSON Only
Ignixa supports JSON format only. XML is not supported.
:::

## HTTP Headers

### Request Headers

#### Prefer (RFC 7240)

Controls response behavior and validation level:

```bash
# Return full resource in response
Prefer: return=representation

# Return minimal response (headers only)
Prefer: return=minimal

# Return OperationOutcome
Prefer: return=OperationOutcome

# Control validation level
Prefer: validation=minimal   # Structure only (fastest)
Prefer: validation=spec      # FHIR specification compliance
Prefer: validation=full      # Full profile validation

# Combine preferences
Prefer: return=representation, validation=spec
```

| Preference | Values | Description |
|------------|--------|-------------|
| `return` | `representation`, `minimal`, `OperationOutcome` | Response body content |
| `validation` | `minimal`, `spec`, `full` | Validation depth |

#### X-Provenance

Submit provenance alongside create/update operations:

```bash
POST /Patient
Content-Type: application/fhir+json
X-Provenance: {"resourceType":"Provenance","recorded":"2024-01-15T10:30:00Z","agent":[{"who":{"reference":"Practitioner/123"}}]}

{ "resourceType": "Patient", ... }
```

The `X-Provenance` header:
- MUST contain a valid Provenance resource
- MUST NOT include `target` (server auto-fills with created resource)
- Maximum size: 16KB

#### Conditional Headers

| Header | Purpose | Example |
|--------|---------|---------|
| `If-Match` | Optimistic concurrency | `If-Match: W/"5"` |
| `If-None-Match` | Conditional read (304) | `If-None-Match: W/"3"` |
| `If-Modified-Since` | Date-based conditional | `If-Modified-Since: Wed, 17 Oct 2025 14:30:00 GMT` |
| `If-None-Exist` | Conditional create | `If-None-Exist: identifier=12345` |

#### Content Negotiation

| Header | Supported Values |
|--------|------------------|
| `Accept` | `application/fhir+json`, `application/json`, `*/*` |
| `Content-Type` | `application/fhir+json`, `application/json` |

### Response Headers

| Header | Description | Example |
|--------|-------------|---------|
| `ETag` | Weak ETag for versioning | `W/"5"` |
| `Last-Modified` | Resource modification time | `Wed, 17 Oct 2025 14:30:00 GMT` |
| `Location` | Created/updated resource URL | `/Patient/123/_history/1` |
| `Preference-Applied` | Preferences honored | `return=representation, validation=spec` |

### Query Parameters

#### _pretty

Pretty-print JSON output for debugging:

```bash
GET /Patient/123?_pretty=true

# Presence implies true
GET /Patient/123?_pretty
```

#### _format

Specify response format (JSON only supported):

```bash
GET /Patient/123?_format=json
GET /Patient/123?_format=application/fhir+json
```

## Related Documentation

- [Supported Resources](/docs/server/fhir/supported-resources)
- [Search Parameters](/docs/server/fhir/search-parameters)
- [Operations](/docs/server/fhir/operations)
