# Investigation: TTL Implementation Approaches

**Feature**: ttl
**Status**: Complete
**Created**: 2025-12-19

## Context

Time-To-Live (TTL) enables automatic or manual deletion of FHIR resources after a defined retention period. This is critical for:

- **Compliance**: GDPR, HIPAA data retention policies
- **Storage optimization**: Removing test data, expired temporary resources
- **Performance**: Preventing unbounded database growth

### Industry Analysis

**No major FHIR server implements automatic TTL at the FHIR resource level:**

1. **Microsoft FHIR Server**: No TTL. Uses `$purge-history` and `$bulk-delete` for manual cleanup. ADR-2510 introduces `_meta-history` query parameter to prevent version creation when only meta fields change (supports TTL bumping without version proliferation).

2. **HAPI FHIR**: No automatic TTL. Provides `$expunge` operation for hard deletion and scheduled jobs for cache cleanup (search results, bulk export files). TTL only applies to internal caches, not FHIR resources.

3. **AWS HealthLake**: No TTL. Supports manual delete and conditional delete. Deleted resources are versioned (soft delete) per FHIR spec.

4. **Firely Server**: No TTL. Offers `$erase` for permanent deletion and soft deletion via standard DELETE. No automatic expiration.

5. **IBM FHIR Server (LinuxForHealth)**: No documented TTL for resources. Internal LRU caches exist for metadata.

**Key Finding**: TTL is not a standard FHIR feature. Implementation requires custom extension + deletion mechanism.

---

## Approach 1: Meta Extension with Background Job (Microsoft-Inspired)

### Description

Store TTL as a custom `meta.extension` on each resource. A background job periodically scans for expired resources and deletes them using `$bulk-delete`.

**Implementation**:
```json
{
  "resourceType": "Patient",
  "id": "example",
  "meta": {
    "versionId": "1",
    "lastUpdated": "2025-12-19T10:00:00Z",
    "extension": [
      {
        "url": "http://ignixa.io/fhir/StructureDefinition/ttl",
        "valueDateTime": "2026-01-19T10:00:00Z"
      }
    ]
  }
}
```

**Background Job**:
- Runs every N minutes (configurable: 5m, 1h, daily)
- Queries: `GET /Patient?_filter=meta.extension.where(url='http://ignixa.io/fhir/StructureDefinition/ttl' and value < now()).exists()`
- Deletes matches via `DELETE /Patient/{id}` or batch bundle
- Optionally hard-deletes with `DELETE /Patient/{id}?hardDelete=true`

**TTL Bumping** (via ADR-2510 pattern):
- `PUT /Patient/example?_meta-history=true` updates `meta.extension[ttl]` without creating a version
- Prevents version proliferation for resources with frequent TTL updates

### Tradeoffs

| Pros | Cons |
|------|------|
| ✅ **FHIR-native**: Uses standard extension mechanism | ❌ **Query complexity**: `_filter` may not support `meta.extension` well in all implementations |
| ✅ **Flexible**: Per-resource TTL control | ❌ **Performance**: Full table scans if not indexed on extension |
| ✅ **Auditable**: TTL visible in resource meta | ❌ **Manual indexing**: Need custom search parameter for `meta.extension[ttl]` |
| ✅ **Bump support**: ADR-2510 `_meta-history` prevents version bloat | ❌ **Job overhead**: Background worker adds complexity |
| ✅ **Gradual**: Deletes in batches, avoids long-running transactions | ❌ **Eventual consistency**: Deletion not immediate |

### Alignment

- [x] Follows layer rules (API → App → Domain → Data)
- [x] F5 Developer Experience (extension visible in JSON)
- [x] FHIR spec compliance (extensions are allowed)
- [x] Consistent with existing patterns (uses `meta`, search, delete)

### Evidence

- **Microsoft FHIR Server ADR-2510**: [Meta History ADR](https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2510-meta-history.md) - Prevents version creation for meta-only updates via `_meta-history` query parameter
- **Ignixa Codebase**:
  - `MetaJsonNode.cs` already supports `VersionId`, `LastUpdated` (can extend with custom properties)
  - `ResourceEntity.cs` has `IsDeleted`, `TransactionId` for soft delete tracking
  - No existing `$bulk-delete` operation (would need implementation)

---

## Approach 2: Database-Level TTL (Cosmos DB / SQL Server Pattern)

### Description

Leverage underlying database TTL features (Cosmos DB TTL, SQL Server temporal tables, or custom timestamp columns).

**Cosmos DB TTL** (if using Azure Cosmos persistence):
- Set container-level default TTL (e.g., 90 days)
- Override per-item with `ttl` property in JSON document
- Cosmos automatically deletes expired items

**SQL Server Custom** (current persistence layer):
- Add `ExpiresAt` column to `ResourceEntity`
- Background job: `DELETE FROM Resource WHERE ExpiresAt < GETUTCDATE() AND IsDeleted = 0`
- Index on `ExpiresAt` for fast scans

**FHIR API Layer**:
- Accept `X-TTL-Seconds` header or `_ttl` query parameter on POST/PUT
- Map to database field during resource creation/update

### Tradeoffs

| Pros | Cons |
|------|------|
| ✅ **Performance**: Database-native indexes for expiration queries | ❌ **Not FHIR-visible**: TTL hidden from resource JSON (breaks transparency) |
| ✅ **Simple queries**: `WHERE ExpiresAt < NOW()` is fast | ❌ **Database coupling**: Ties logic to persistence layer |
| ✅ **Automatic (Cosmos)**: Zero code for deletion with Cosmos DB | ❌ **Migration burden**: Schema change required (add `ExpiresAt` column) |
| ✅ **Scalable**: Database handles millions of rows efficiently | ❌ **Multi-tenant complexity**: Partition-aware deletion logic needed |
| ✅ **No API changes**: TTL set via headers, not resource body | ❌ **FHIR non-compliance**: Clients can't see/update TTL via standard PATCH |

### Alignment

- [ ] Follows layer rules (❌ leaks persistence details to API layer via headers)
- [x] F5 Developer Experience (works, but TTL not visible in FHIR JSON)
- [ ] FHIR spec compliance (❌ non-standard header, TTL not in resource)
- [x] Consistent with existing patterns (uses `ResourceEntity`, transactions)

### Evidence

- **Azure Cosmos DB TTL**: [Cosmos DB Time to Live](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/time-to-live) - Automatic item deletion after TTL expiration
- **Ignixa Codebase**:
  - `ResourceEntity.cs` - Can add `ExpiresAt` column
  - `SqlEntityFrameworkRepository.cs` - Create/Update methods would set `ExpiresAt`
  - No Cosmos DB persistence layer exists (SQL Server only)

---

## Approach 3: Scheduled Job with SearchParameter-Based Query

### Description

Define a **custom search parameter** `_expires` that indexes a standardized extension. Background job queries using FHIR search API.

**SearchParameter Definition** (stored in database or config):
```json
{
  "resourceType": "SearchParameter",
  "id": "resource-expires",
  "url": "http://ignixa.io/fhir/SearchParameter/resource-expires",
  "name": "expires",
  "status": "active",
  "code": "_expires",
  "base": ["Resource"],
  "type": "date",
  "expression": "meta.extension.where(url='http://ignixa.io/fhir/StructureDefinition/ttl').value.as(dateTime)"
}
```

**Indexing**:
- `ElementSearchIndexer` extracts `meta.extension[ttl].valueDateTime` into `DateSearchIndexEntity`
- Database index on `DateSearchIndexEntity` enables fast `_expires` queries

**Background Job**:
- Runs every N minutes
- Queries: `GET /?_expires=lt{now}&_count=500`
- Deletes matches in batches

**API Usage**:
- Client sets TTL: `PUT /Patient/123` with `meta.extension[ttl]`
- Client bumps TTL: `PATCH /Patient/123` updates extension (or use `_meta-history`)

### Tradeoffs

| Pros | Cons |
|------|------|
| ✅ **FHIR-native search**: Uses standard search parameters | ❌ **Custom parameter setup**: Requires SearchParameter resource management |
| ✅ **Indexed queries**: Database index on `DateSearchIndexEntity` | ❌ **Indexing overhead**: Every resource with TTL gets an extra index row |
| ✅ **Discoverable**: Clients can query `GET /SearchParameter/resource-expires` | ❌ **Reindexing**: Must reindex all resources if SearchParameter changes |
| ✅ **Multi-resource support**: Works across all resource types (base: Resource) | ❌ **Job complexity**: Must handle pagination (`_count=500`, iterate) |
| ✅ **Standards-aligned**: Follows FHIR SearchParameter lifecycle | ❌ **Extension + SearchParam**: Two moving parts (extension schema + index definition) |

### Alignment

- [x] Follows layer rules (API → App → Search → Data)
- [x] F5 Developer Experience (TTL visible, searchable)
- [x] FHIR spec compliance (standard SearchParameter + extension)
- [x] Consistent with existing patterns (uses `ElementSearchIndexer`, search parameters)

### Evidence

- **Ignixa Codebase**:
  - `SearchParameterNames.cs` - Defines search parameter constants
  - `ElementSearchIndexer.cs` - Extracts values from FHIRPath expressions
  - `SearchParameterQueryGenerator.cs` - Generates SQL for search queries
  - `docs/features/search/investigations/custom-parameters.md` - Investigation on custom SearchParameter lifecycle
- **FHIR Spec**: [SearchParameter Resource](https://www.hl7.org/fhir/searchparameter.html) - Standard mechanism for defining search behavior

---

## Recommendation: **Approach 3** (SearchParameter-Based with Meta Extension)

### Rationale

| Criterion | Approach 1 | Approach 2 | Approach 3 |
|-----------|-----------|-----------|-----------|
| **FHIR Compliance** | ✅ Extension only | ❌ Hidden header | ✅ Extension + SearchParameter |
| **Performance** | ⚠️ Unindexed `_filter` | ✅ Database column | ✅ Indexed search parameter |
| **Discoverability** | ❌ No search support | ❌ Not visible | ✅ `/SearchParameter/resource-expires` |
| **Layer Separation** | ✅ Clean | ❌ Leaks to API | ✅ Clean |
| **Existing Patterns** | ⚠️ No precedent | ⚠️ New column | ✅ Search system exists |
| **F5 Dev Experience** | ✅ JSON visible | ⚠️ Hidden | ✅ JSON + searchable |

**Why Approach 3 wins**:

1. **Leverages existing infrastructure**: Ignixa already has `ElementSearchIndexer`, `SearchParameterQueryGenerator`, and custom parameter investigation (`docs/features/search/investigations/custom-parameters.md`).

2. **FHIR-native**: Clients can query `GET /?_expires=lt2026-01-01` to find expiring resources. Searchable TTL is more powerful than hidden headers.

3. **Performance**: Database index on `DateSearchIndexEntity` makes expiration queries O(log n) vs full table scans.

4. **Extensible**: If future requirements need TTL per resource type (e.g., Patient 90 days, Observation 30 days), SearchParameter can be refined with `base: ["Patient"]`.

5. **Consistency**: Aligns with `docs/features/search/investigations/custom-parameters.md` (already investigating SearchParameter lifecycle).

### Implementation Plan (High-Level)

1. **Define Extension**:
   - Create `StructureDefinition` for `http://ignixa.io/fhir/StructureDefinition/ttl`
   - Extension type: `valueDateTime`

2. **Create SearchParameter**:
   - Code: `_expires`
   - Expression: `meta.extension.where(url='http://ignixa.io/fhir/StructureDefinition/ttl').value.as(dateTime)`
   - Base: `["Resource"]`

3. **Index Resources**:
   - `ElementSearchIndexer` extracts `meta.extension[ttl]` → `DateSearchIndexEntity`
   - Database migration: ensure index on `DateSearchIndexEntity.DateValue`

4. **Background Job** (Application Layer):
   - `TtlCleanupJob` runs every N minutes (config: `appsettings.json`)
   - Query: `GET /?_expires=lt{now}&_count=500`
   - Batch delete: `DELETE /{resourceType}/{id}` or transaction bundle
   - Log deleted resource IDs for audit

5. **Meta-History Support** (Optional, via ADR-2510):
   - Add `_meta-history=true` query parameter to `PUT` handler
   - Skip version creation if only `meta` fields changed
   - Prevents version bloat for TTL bumping

6. **Testing**:
   - Unit test: `ElementSearchIndexer` extracts TTL extension
   - Integration test: `TtlCleanupJob` deletes expired resources
   - E2E test: Create Patient with TTL, wait, verify deletion

### Alternative Approaches Considered

- **Approach 1**: Viable but less performant (no index on extension without SearchParameter).
- **Approach 2**: Database-efficient but violates FHIR transparency (hidden TTL).

---

## References

### Microsoft FHIR Server
- [ADR-2510: Meta History](https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2510-meta-history.md) - Prevents version creation for meta-only updates
- [Bulk Delete Operation](https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/fhir-bulk-delete) - Asynchronous deletion via `$bulk-delete`
- [Purge History Operation](https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/purge-history) - Hard delete history

### HAPI FHIR
- [Configuration](https://hapifhir.io/hapi-fhir/docs/server_jpa/configuration.html) - Search result cache TTL, scheduled jobs
- [Deleting Data - Smile CDR](https://smilecdr.com/docs/fhir_repository/deleting_data.html) - $expunge, soft delete

### AWS HealthLake
- [Deleting FHIR Resources](https://docs.aws.amazon.com/healthlake/latest/devguide/managing-fhir-resources-delete.html) - Manual delete, conditional delete

### Firely Server
- [Permanently Delete Resources - $erase](https://docs.fire.ly/projects/Firely-Server/en/latest/features_and_tools/custom_operations/erase.html) - Hard delete operation

### FHIR Specification
- [FHIR SearchParameter](https://www.hl7.org/fhir/searchparameter.html) - Defining custom search parameters
- [FHIR Extensibility](https://www.hl7.org/fhir/extensibility.html) - Extension mechanism

### Azure Cosmos DB
- [Time to Live (TTL)](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/time-to-live) - Automatic item deletion
