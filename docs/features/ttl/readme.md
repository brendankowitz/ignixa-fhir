# Feature: TTL (Time-To-Live)

**Status**: Decided
**Created**: 2025-12-19

## Problem Statement

Time-To-Live (TTL) enables automatic or policy-driven deletion of FHIR resources after a defined retention period. This addresses:

- **Compliance**: GDPR, HIPAA, and organizational data retention policies requiring deletion after N days
- **Storage optimization**: Removing test data, temporary resources, expired clinical data
- **Performance**: Preventing unbounded database growth and maintaining query performance
- **Operational control**: Scheduled cleanup vs. manual deletion workflows

## Constraints

- **FHIR spec does not define TTL**: No standard `meta.expiresAt` or `_ttl` parameter exists
- **Versioning requirement**: Deleted resources must maintain version history per FHIR spec (soft delete)
- **Multi-tenant isolation**: TTL cleanup must respect partition boundaries
- **Performance at scale**: Expiration queries must use indexes (no full table scans)
- **Auditability**: Deletion events must be traceable for compliance

## Investigations
| Investigation | Status | Summary |
|--------------|--------|---------|
| [ttl-implementation-approaches](investigations/ttl-implementation-approaches.md) | Superseded | Evaluated 3 extension-based approaches. Superseded by simpler column-based design. |
| [first-class-column-design](investigations/first-class-column-design.md) | Merged | `ExpiresAt` column + `X-TTL` header + `_ttl` search parameter. Minimal complexity, no versioning impact, no JSON mutation. |

## Decision

See [ADR-2512: TTL as First-Class Database Column](../../adr/adr-2512-ttl-first-class-column.md)

- `ExpiresAt` column on `ResourceEntity` (nullable, indexed)
- `X-TTL` header sets expiration (absent = lives forever)
- `_ttl` built-in search parameter (like `_id`, `_lastUpdated`)
- Background cleanup job deletes expired resources
- No resource versioning impact for TTL changes
