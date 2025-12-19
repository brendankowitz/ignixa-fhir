# ADR 2510: Conditional CRUD Operations

## Status
Accepted

## Context
FHIR R4 Section 3.1.0.5-3.1.0.7 defines conditional operations as **mandatory** for full FHIR conformance (Capability Level 3). These operations allow:
- Idempotent create (prevent duplicates when client doesn't know if resource exists)
- Search-based update (update by business identifier without knowing FHIR ID)
- Bulk delete (delete multiple matching resources)
- Conditional patch (partial updates via search)

## Decision
Implement all 6 conditional operations using existing handlers with search-based matching:

| Operation | Trigger | HTTP | Success Status |
|-----------|---------|------|----------------|
| Conditional Read | `If-None-Match`, `If-Modified-Since` | GET | 304 Not Modified |
| Conditional Create | `If-None-Exist` header | POST | 200/201 |
| Conditional Update | Query string (no ID) | PUT | 200/201 |
| Conditional Patch | Query string (no ID) | PATCH | 200 |
| Conditional Delete | Query string (no ID) | DELETE | 204/200 |

**Key Design Decisions:**
- Reuse existing handlers (`SearchResourcesHandler`, `CreateOrUpdateResourceHandler`, etc.)
- Add **optimistic concurrency** for conditional update/patch (If-Match with ETag from search)
- Minimal API endpoints (not controllers)
- Full multi-tenant support (tenant-explicit and tenant-agnostic routes)
- **Verbose OperationOutcomes** for all error scenarios

**Search Result Behavior:**
- 0 matches → Create (for create/update) or 404 (for others)
- 1 match → Perform operation
- Multiple matches → 412 Precondition Failed

## Consequences

**Positive:**
- Full FHIR R4/R4B/R5 conformance for conditional operations
- Idempotent APIs enable safe client retries
- Business identifier-based operations reduce client complexity
- Version-agnostic implementation (works across all FHIR versions)

**Negative:**
- Search adds latency to conditional operations
- Multiple matches require explicit handling
- _count parameter behavior differs between single/multiple delete modes

## References
- Investigation: `docs/features/conditional-operations/investigations/conditional-crud.md`
- FHIR Spec: http://hl7.org/fhir/R4/http.html#ccreate
