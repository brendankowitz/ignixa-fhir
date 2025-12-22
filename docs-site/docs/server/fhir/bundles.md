---
sidebar_position: 3
title: Bundles
description: Batch and transaction bundle processing
---

# Bundles

Ignixa supports FHIR Bundle resources for submitting multiple operations in a single request.

## Batch vs Transaction

Choose the right bundle type for your use case:

| | Batch | Transaction |
|---|-------|-------------|
| **Atomicity** | None - each entry independent | All-or-nothing |
| **On failure** | Other entries still succeed | All entries rolled back |
| **Execution** | Parallel (faster) | Sequential by verb order |
| **urn:uuid references** | Not resolved between entries | Resolved across entries |
| **Use when** | Bulk loading independent data | Creating related resources together |

## Batch Bundles

Batch bundles execute each entry independently. Failures in one entry don't affect others.

```json
{
  "resourceType": "Bundle",
  "type": "batch",
  "entry": [
    {
      "request": { "method": "POST", "url": "Patient" },
      "resource": { "resourceType": "Patient", "name": [{"family": "Smith"}] }
    },
    {
      "request": { "method": "POST", "url": "Patient" },
      "resource": { "resourceType": "Patient", "name": [{"family": "Jones"}] }
    },
    {
      "request": { "method": "GET", "url": "Patient/123" }
    }
  ]
}
```

### Batch Response

Each entry gets its own status - some may succeed while others fail:

```json
{
  "resourceType": "Bundle",
  "type": "batch-response",
  "entry": [
    { "response": { "status": "201 Created", "location": "Patient/abc" } },
    { "response": { "status": "201 Created", "location": "Patient/def" } },
    { "response": { "status": "404 Not Found" } }
  ]
}
```

### When to Use Batch

- Loading large datasets where entries are independent
- Fetching multiple resources in one request
- Operations where partial success is acceptable
- Performance-critical bulk operations (parallel execution)

## Transaction Bundles

Transaction bundles are atomic - all entries succeed or all are rolled back. Use when creating related resources that reference each other.

```json
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    {
      "fullUrl": "urn:uuid:patient-1",
      "request": { "method": "POST", "url": "Patient" },
      "resource": {
        "resourceType": "Patient",
        "name": [{"family": "Smith"}]
      }
    },
    {
      "fullUrl": "urn:uuid:encounter-1",
      "request": { "method": "POST", "url": "Encounter" },
      "resource": {
        "resourceType": "Encounter",
        "status": "in-progress",
        "class": { "code": "AMB" },
        "subject": { "reference": "urn:uuid:patient-1" }
      }
    },
    {
      "fullUrl": "urn:uuid:obs-1",
      "request": { "method": "POST", "url": "Observation" },
      "resource": {
        "resourceType": "Observation",
        "status": "final",
        "code": { "coding": [{"system": "http://loinc.org", "code": "8310-5"}] },
        "subject": { "reference": "urn:uuid:patient-1" },
        "encounter": { "reference": "urn:uuid:encounter-1" }
      }
    }
  ]
}
```

### urn:uuid Reference Resolution

The `urn:uuid:patient-1` temporary reference is resolved to the actual Patient ID after creation. The Observation's `subject.reference` becomes `"Patient/abc123"` in the stored resource.

### Transaction Response

On success, all entries return their status:

```json
{
  "resourceType": "Bundle",
  "type": "transaction-response",
  "entry": [
    { "response": { "status": "201 Created", "location": "Patient/abc123" } },
    { "response": { "status": "201 Created", "location": "Encounter/def456" } },
    { "response": { "status": "201 Created", "location": "Observation/ghi789" } }
  ]
}
```

On failure, the entire transaction is rolled back:

```json
{
  "resourceType": "Bundle",
  "type": "transaction-response",
  "entry": [{
    "response": {
      "status": "400",
      "outcome": {
        "resourceType": "OperationOutcome",
        "issue": [{ "severity": "error", "diagnostics": "Validation failed..." }]
      }
    }
  }]
}
```

### Processing Order

Transaction entries are reordered by HTTP verb for consistent outcomes:

| Order | Verb | Why |
|-------|------|-----|
| 1 | DELETE | Remove before recreating |
| 2 | POST | Create new resources |
| 3 | PUT | Update existing |
| 4 | PATCH | Partial updates |
| 5 | GET | Reads last |

### When to Use Transaction

- Creating a Patient with related Observations, Encounters, etc.
- Ensuring referential integrity between resources
- Operations that must all succeed or all fail
- Workflows requiring atomicity

## Conditional Operations

Both bundle types support conditional operations:

```json
{
  "request": {
    "method": "PUT",
    "url": "Patient?identifier=http://example.org|12345"
  },
  "resource": { "resourceType": "Patient", ... }
}
```

This updates the Patient matching the identifier, or creates if not found.

## Streaming Architecture

Ignixa processes bundles using a streaming approach:

**Phase 1 (Streaming)**: Entries without `urn:uuid` references execute immediately in parallel as they're parsed.

**Phase 2 (Buffered)**: When `urn:uuid` or conditional references are detected, remaining entries are buffered for reference resolution before execution.

This provides:
- **Memory efficiency** - entries processed as they arrive
- **Lower latency** - processing starts before full request received
- **Optimal throughput** - parallel execution when possible

### Response Links at Bottom

Bundle responses place `link` elements after `entry` because streaming serialization writes entries as they complete - pagination links can only be determined after all entries are processed.

## Best Practices

| Scenario | Use |
|----------|-----|
| Loading 1000 independent Patient records | Batch (or bulk import) |
| Creating Patient + Observation + Encounter together | Transaction |
| Fetching 10 resources by ID | Batch |
| Updating resources that reference each other | Transaction |

## Limitations

- **Large bundles**: For 1000+ entries, consider [bulk import](/docs/server/features/bulk-operations)
- **urn:uuid in batch**: References between batch entries are not resolved
- **Parallel conflicts**: Conditional creates in parallel may race

## Related Documentation

- [ADR: Bundle Processing](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2509-bundle-processing.md)
- [Bulk Operations](/docs/server/features/bulk-operations)
