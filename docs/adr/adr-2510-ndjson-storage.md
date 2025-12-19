# ADR 2510: NDJSON File-Based Storage

## Status
Accepted

## Context
For the F5 developer experience, the FHIR server needs a storage backend that:
- Works without external dependencies (no database setup)
- Supports rapid iteration during development
- Provides human-readable data for debugging
- Enables easy test data management

Production storage (Cosmos DB, SQL Server) is separate and opt-in.

## Decision
Implement **NDJSON file-based storage** with date-partitioned directories:

**Directory Structure:**
```
{baseDir}/{resourceType}/{YYYY}/{MM}/{DD}/
├── tx-{transactionId}.ndjson           # Resource data
└── tx-{transactionId}.metadata.ndjson  # Metadata sidecar
```

**NDJSON File Format:**
- Line 1: Transaction Bundle (request metadata)
- Line 2+: Resource data (one resource per line)

**Metadata Sidecar:**
```json
{
  "transactionId": "1728468123456",
  "resourceType": "Patient",
  "resourceId": "example-123",
  "version": "1",
  "lastModified": "2025-10-09T10:00:00.000Z",
  "isDeleted": false,
  "searchIndexes": []
}
```

**Key Implementation Details:**
- `RecyclableMemoryStreamManager` for all file I/O (reduces GC pressure)
- Transaction ID from Unix timestamp (natural ordering, debugging context)
- Scan date directories to find latest version by `LastModified`
- Search indexes stored in metadata for in-memory search

## Consequences

**Positive:**
- Zero infrastructure requirements (F5 works immediately)
- Human-readable files for debugging
- Git-friendly for test fixtures
- Natural time-based partitioning
- Easy backup/restore (just copy files)

**Negative:**
- Not suitable for production workloads
- Scan-based reads (no index)
- File system limitations (max files per directory)
- No concurrent write safety (development only)

## References
- Investigation: `docs/features/storage/investigations/ndjson-storage.md`
- Investigation: `docs/features/storage/investigations/file-based-search.md`
