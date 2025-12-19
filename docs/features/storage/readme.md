# Feature: Storage (General)

**Status**: Partial Implementation
**Created**: 2025-10-09

## Problem Statement

FHIR resource storage requires flexible backends supporting transactions, versioning, and efficient querying. This feature covers storage-agnostic patterns and the file-based development backend.

## Constraints

- Must support transaction bundles with atomic commit/rollback
- Must store resource metadata (versionId, lastUpdated) separately
- Must support decoupled indexing for search
- Architecture must be backend-agnostic where possible

## Related Features

- [storage-cosmos](../storage-cosmos/) - Azure Cosmos DB backend
- [storage-sql](../storage-sql/) - SQL Server backend

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [ndjson-storage](investigations/ndjson-storage.md) | Merged | NDJSON file-based storage with date-partitioned directories |
| [file-based-search](investigations/file-based-search.md) | Merged | File-based storage with search indexing |
| [v2-design](investigations/v2-design.md) | Viable | Storage architecture v2 design |
| [decoupled-indexing](investigations/decoupled-indexing.md) | Viable | Decoupled resource and index storage |

## Decision

File-based storage with NDJSON format implemented for development. Production backends (Cosmos, SQL) are separate features.

See [ADR-2510: NDJSON File-Based Storage](../../adr/adr-2510-ndjson-storage.md)
