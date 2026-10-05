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

SQL Server transactions validate and stage every entry before submitting the complete resource
write set to one core merge. Resource data, ordinary search indexes, and TTL changes commit together.
Post-merge search extension updates remain a separate, monitored phase; they do not roll back
the committed core write.

A SQL transaction supports up to 79,999 resource writes. Larger write sets are rejected before
allocation rather than wrapping the cyclic surrogate-ID allocator.

Transactions must use tenant-relative resource interactions. Nested bundles, cross-tenant URLs,
and `$operation` calls are rejected before execution because their side effects cannot participate
in the resource transaction. A mutating transaction on a storage provider without atomic transaction
support returns HTTP 501 before any entry is executed. Such providers do not advertise the full
`transaction` interaction in their CapabilityStatement. Batch remains supported.

Preflight validates every entry's method and canonical path against known FHIR resource types and
supported route shapes. Administrative/job-control routes, encoded path tricks, dot-segment traversal,
alternate separators and absolute/cross-tenant URLs cannot enter the staged write pipeline.

Transactions containing only supported GET/HEAD resource interactions do not require atomic-write
support. They return a `transaction-response` only when every read succeeds; a failed read returns
one error `OperationOutcome`. These reads do **not** promise snapshot-read isolation.

A transaction containing writes supports only plain point GET/HEAD read entries (`ResourceType/id`,
without query parameters). Search, history, version-specific and parameterized reads in a mixed
transaction return HTTP 501 **before any entry executes**. Use a separate request or a read-only
transaction for these shapes; no early commit or second search engine is used to fabricate a staged
search view.

A conditional delete may select multiple resources under one bundle entry. All selected tombstones
are staged in the same core write, while the response retains one entry with the complete deletion
outcome. Mutating the same resource more than once in one transaction remains unsupported.

The `vread` interaction is advertised only for repositories that honor explicit version reads.
SQL Server supports it. The filesystem prototype supports history lists but not version-specific
point reads; its vread requests return HTTP 501 rather than silently returning the latest version.

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

Explicit-ID PUT entries may also declare UUID `fullUrl` aliases; these resolve to the validated
request destination. Duplicate or contradictory UUID aliases and unresolved UUID references are
rejected before execution. A UUID reference without a corresponding `fullUrl` declaration is not
inferred from a resource's `id`.

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

On validation or core-write failure, the server returns an HTTP error with an `OperationOutcome`,
not a mixed-success `transaction-response`. No transaction resource writes are retained:

```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "invalid",
    "diagnostics": "A transaction entry failed validation. No transaction writes were committed."
  }]
}
```

An entry's `request.ifMatch` carries the required current version (for example, `W/"2"`).
It is checked at the SQL write boundary, not only before the write is queued. A mismatch returns
HTTP 412 for the whole transaction; in a batch, only that entry fails. An unresolved `urn:uuid`
reference also rejects a transaction without writes.

When a conditional create selects an existing resource or assigns a different new ID, UUID references
are resolved to that final identity before commit. Both stored references and their search indexes
use the resolved ID.

Connection loss during allocation or commit can leave an uncertain outcome. Such failures are
logged for transaction reconciliation; allocation is never automatically replayed. Reconcile
resource state before retrying after an ambiguous transport failure.

A definitive SQL deadlock-victim rollback closes its failed allocation using uncancelled cleanup,
while preserving the original write failure. Arbitrary transport failures and timeouts are not
classified as known rollbacks.

Successful SQL commit completion also advances the existing transaction visibility watermark.
Newly committed compartment members are therefore available to `Patient/$everything?_since`
without manually updating transaction rows. An earlier unfinished allocation still holds the
visibility barrier until it is completed or reconciled.

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
