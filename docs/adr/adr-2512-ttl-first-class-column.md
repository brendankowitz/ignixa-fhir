# ADR-2512: TTL as First-Class Database Column

**Status**: Proposed
**Date**: 2025-12-20
**Feature**: ttl

## Context

FHIR servers need resource expiration for compliance (GDPR, HIPAA retention policies), storage optimization, and operational cleanup. No major FHIR server (Microsoft, HAPI, AWS HealthLake, Firely) implements automatic TTL at the resource level.

The challenge: FHIR's `meta` element is designed for operational metadata like TTL, but the spec provides no mechanism to update `meta` without triggering resource versioning. Storing TTL in `meta.extension` creates versioning bloat when TTL is bumped frequently, requiring workarounds like Microsoft's `_meta-history` parameter or delta-based history storage.

## Options Considered

1. **Meta extension + SearchParameter** - Store TTL in `meta.extension`, index via custom SearchParameter, add `_meta-history` to suppress versioning *(rejected: high complexity, versioning workarounds needed)*

2. **Meta extension + delta history** - Store TTL in extension, use patch/delta storage for TTL-only version history *(rejected: complex reconstruction, append-only semantics blur)*

3. **Database column + HTTP header** - Store TTL in `ExpiresAt` column, set via `X-TTL` header, query via built-in `_ttl` parameter *(selected)*

## Decision

TTL will be implemented as a **first-class database column** with HTTP header input and built-in search parameter:

- **Storage**: `ExpiresAt DATETIMEOFFSET NULL` column on `ResourceEntity`, indexed for cleanup queries
- **Input**: `X-TTL` header on PUT/POST (absent = null = lives forever)
- **Search**: `_ttl` parameter behaves like `_id` and `_lastUpdated` (built-in, direct SQL translation)
- **Cleanup**: Background service finds expired current records, then hard-deletes all related data (history + search indexes)

This approach treats TTL as **server infrastructure** rather than FHIR content. While `meta.extension` is the FHIR-correct location for operational metadata, the practical benefit of avoiding versioning complexity outweighs spec purity. TTL changes update a column, not the resource - no versions created, no history bloat, no workarounds needed.

The `_ttl` search parameter follows the existing pattern of `_id` and `_lastUpdated`: special-cased in the query builder, direct column comparison, no SearchParameter resource required.

## Consequences

- **Simpler implementation**: One column, one header, one search parameter. No SearchParameter lifecycle, no reindexing, no `_meta-history` complexity.

- **No versioning impact**: TTL bumps update a column, not the resource. Version history reflects clinical changes only.

- **TTL not visible in resource JSON**: Clients cannot see TTL via `GET /Patient/123`. They must query `?_ttl:missing=false` or use server documentation. This is acceptable for operational metadata.

- **Header-only input**: Clients cannot set TTL via resource body or PATCH. This is intentional - TTL is server-managed infrastructure.

- **Future option**: If FHIR visibility is later required, the column value can be projected into `meta.extension` on read (read-only, not stored in JSON).
