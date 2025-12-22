---
sidebar_position: 5
title: Resource TTL
description: Automatic resource expiration with Time-To-Live
---

# Resource TTL (Time-To-Live)

Ignixa supports automatic resource expiration through a first-class TTL mechanism. Resources can be assigned an expiration timestamp via HTTP header, enabling compliance with retention policies (GDPR, HIPAA), storage optimization, and automatic cleanup of temporary data.

## Quick Start

Set a 30-day TTL when creating or updating a resource:

```http
PUT /Patient/temp-patient
Content-Type: application/fhir+json
X-TTL: P30D

{"resourceType": "Patient", "id": "temp-patient", "active": true}
```

The resource will be automatically deleted after 30 days.

:::info Eventual Expiration
Resources are removed by a background cleanup process that runs periodically (default: every 15 minutes). Exact expiry times are not guaranteed—resources may persist briefly past their TTL until the next cleanup cycle processes them.
:::

## X-TTL Header Format

The `X-TTL` header uses [ISO 8601 duration format](https://en.wikipedia.org/wiki/ISO_8601#Durations):

| Duration | Format | Description |
|----------|--------|-------------|
| 1 hour | `PT1H` | Short-lived data |
| 1 day | `P1D` | Daily expiration |
| 30 days | `P30D` | Monthly retention |
| 1 year | `P1Y` | Annual retention |
| 2 years | `P2Y` | Long-term retention |

### Header Behavior

| Header Value | Result |
|--------------|--------|
| Not present | Resource lives forever |
| `P30D` | Expires 30 days from now |
| Empty or `0` | Clears existing TTL |

## Searching by TTL

Query resources by their expiration status using the built-in `_ttl` search parameter:

```http
# Find resources with TTL set (temporary resources)
GET /Patient?_ttl:missing=false

# Find resources without TTL (permanent resources)
GET /Patient?_ttl:missing=true

# Find resources expiring before a specific date
GET /Patient?_ttl=lt2026-01-01

# Find resources expiring after a date
GET /Patient?_ttl=gt2025-06-01
```

### Comparison Operators

| Operator | Example | Description |
|----------|---------|-------------|
| `lt` | `_ttl=lt2026-01-01` | Expires before date |
| `le` | `_ttl=le2026-01-01` | Expires on or before date |
| `gt` | `_ttl=gt2025-01-01` | Expires after date |
| `ge` | `_ttl=ge2025-01-01` | Expires on or after date |
| `eq` | `_ttl=eq2025-12-31` | Expires on exact date |

## Examples

### Create temporary observation (1 hour TTL)

```http
POST /Observation
Content-Type: application/fhir+json
X-TTL: PT1H

{
  "resourceType": "Observation",
  "status": "preliminary",
  "code": {
    "coding": [{"system": "http://loinc.org", "code": "8867-4", "display": "Heart rate"}]
  },
  "valueQuantity": {"value": 72, "unit": "beats/minute"}
}
```

### Extend TTL on update

```http
PUT /Patient/temp-patient
Content-Type: application/fhir+json
X-TTL: P90D

{"resourceType": "Patient", "id": "temp-patient", "active": true}
```

### Clear TTL (make permanent)

```http
PUT /Patient/temp-patient
Content-Type: application/fhir+json
X-TTL: 0

{"resourceType": "Patient", "id": "temp-patient", "active": true}
```

### Update without changing TTL

Simply omit the `X-TTL` header - the existing TTL is preserved:

```http
PUT /Patient/temp-patient
Content-Type: application/fhir+json

{"resourceType": "Patient", "id": "temp-patient", "active": false}
```

## How It Works

### Architecture

TTL is implemented as a server-managed database column, not FHIR resource content:

- **Storage**: `ExpiresAt` column on resource table (nullable `DATETIMEOFFSET`)
- **Input**: `X-TTL` HTTP header on PUT/POST requests
- **Search**: `_ttl` built-in parameter (like `_id` and `_lastUpdated`)
- **Cleanup**: Background DurableTask orchestration hard-deletes expired resources

### Key Design Decisions

1. **No versioning impact** - TTL changes update a column, not the resource. No new versions are created for TTL-only changes.

2. **TTL not visible in resource JSON** - By design, TTL is operational metadata separate from clinical content. Use `_ttl:missing=false` to find resources with TTL.

3. **Hard delete on expiration** - Expired resources are permanently removed, including all history versions and search indexes.

4. **Header-only input** - TTL cannot be set via the resource body or PATCH. This is intentional - TTL is infrastructure, not clinical data.

## Configuration

TTL cleanup runs automatically as a background service. Configuration options:

```json
{
  "TtlCleanup": {
    "Enabled": true,
    "CheckIntervalMinutes": 15,
    "BatchSize": 500,
    "MaxConcurrentBatches": 4
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable/disable TTL cleanup |
| `CheckIntervalMinutes` | `15` | How often to check for expired resources |
| `BatchSize` | `500` | Resources to delete per batch |
| `MaxConcurrentBatches` | `4` | Parallel deletion batches |

## Use Cases

### Compliance Retention

Set TTL based on data retention policies:

```http
# GDPR: 2-year retention for patient records
PUT /Patient/gdpr-patient
X-TTL: P2Y
```

### Temporary Test Data

Short-lived resources for testing:

```http
# Expires in 1 hour
POST /Patient
X-TTL: PT1H
```

### Cache-like Behavior

Resources that should auto-expire:

```http
# Session data, temporary calculations
POST /Basic
X-TTL: P1D
```

## Limitations

- TTL is not visible when reading resources (by design)
- Cannot set TTL via resource body or PATCH
- Expired resources are hard-deleted (no soft delete)
- TTL applies to the logical resource (all versions deleted together)

## Related Documentation

- [Search Parameters](/docs/server/fhir/search-parameters) - Query syntax including date comparators
- [Bulk Operations](/docs/server/features/bulk-operations) - Large-scale resource management
