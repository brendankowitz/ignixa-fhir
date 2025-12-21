# Investigation: First-Class Column Design

**Feature**: ttl
**Status**: Complete
**Created**: 2025-12-20

## Approach

TTL as a **server-managed database column**, not FHIR resource content. Set via HTTP header, queried via built-in search parameter.

### Core Design

```
┌─────────────────────────────────────────────────────────────┐
│                      ResourceEntity                         │
├─────────────────────────────────────────────────────────────┤
│  ResourceSurrogateId  BIGINT        (PK)                    │
│  ResourceTypeId       SMALLINT                              │
│  ResourceId           VARCHAR(64)                           │
│  Version              INT                                   │
│  ...                                                        │
│  ExpiresAt            DATETIMEOFFSET  ← NEW (nullable)      │
│  RawResource          VARBINARY(MAX)                        │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
              ┌───────────────────────────────┐
              │  IX_Resource_ExpiresAt        │
              │  (ExpiresAt)                  │
              │  WHERE ExpiresAt IS NOT NULL  │
              │    AND IsDeleted = 0          │
              │    AND IsHistory = 0          │
              └───────────────────────────────┘
```

### API: X-TTL Header

```http
PUT /Patient/123
X-TTL: 2027-01-01T00:00:00Z
Content-Type: application/fhir+json

{"resourceType": "Patient", "id": "123", ...}
```

| X-TTL Header | ExpiresAt Column | Behavior |
|--------------|------------------|----------|
| Not present | `NULL` | Lives forever |
| `2027-01-01T00:00:00Z` | Value | Expires at specified time |
| `0` or empty | `NULL` | Clear existing TTL |

### Search: `_ttl` Built-In Parameter

Behaves like `_id`, `_lastUpdated` - special-cased in query builder, not a SearchParameter resource.

```http
GET /Patient?_ttl=lt2026-01-01      # Expiring before date
GET /Patient?_ttl=gt2025-01-01      # Expiring after date
GET /Patient?_ttl:missing=true      # No TTL (lives forever)
GET /Patient?_ttl:missing=false     # Has TTL set
```

**SQL translation** (direct column comparison):

```sql
-- GET /Patient?_ttl=lt2026-01-01
SELECT * FROM Resource
WHERE ResourceTypeId = @patientTypeId
  AND ExpiresAt < '2026-01-01'
  AND IsHistory = 0 AND IsDeleted = 0;

-- GET /Patient?_ttl:missing=true
SELECT * FROM Resource
WHERE ResourceTypeId = @patientTypeId
  AND ExpiresAt IS NULL
  AND IsHistory = 0 AND IsDeleted = 0;
```

### Background Cleanup (Hard Purge)

Temp table approach - collect surrogate IDs once, delete from all 14 search param tables:

```sql
-- 1. Collect all surrogate IDs for expired resources (current + all history)
CREATE TABLE #ExpiredSurrogates (ResourceSurrogateId BIGINT PRIMARY KEY);

INSERT INTO #ExpiredSurrogates (ResourceSurrogateId)
SELECT r.ResourceSurrogateId
FROM Resource r
WHERE EXISTS (
    SELECT TOP (@batchSize) 1
    FROM Resource curr
    WHERE curr.ExpiresAt IS NOT NULL
      AND curr.ExpiresAt < GETUTCDATE()
      AND curr.IsHistory = 0
      AND curr.IsDeleted = 0
      AND curr.ResourceTypeId = r.ResourceTypeId
      AND curr.ResourceId = r.ResourceId
    ORDER BY curr.ExpiresAt
);

-- 2. Delete from all search param tables (14 tables)
DELETE FROM StringSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM DateTimeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM TokenSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM ReferenceSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM QuantitySearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM NumberSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM UriSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM TokenTokenCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM TokenStringCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM TokenQuantityCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM TokenNumberNumberCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM TokenDateTimeCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
DELETE FROM ReferenceTokenCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);

-- 3. Delete from Resource table (all versions)
DELETE FROM Resource WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);

-- 4. Cleanup
DROP TABLE #ExpiredSurrogates;
```

## Tradeoffs

| Pros | Cons |
|------|------|
| **Simple** - One column, one header, one search param | Not visible in FHIR resource JSON (by design) |
| **No versioning impact** - TTL changes don't create versions | Clients must use header (can't set via resource body) |
| **Fast queries** - Direct SQL index, no FHIRPath | Non-standard (X-TTL is custom header) |
| **No JSON mutation** - Resource content unchanged | Requires schema migration (add column) |
| **Built-in search** - `_ttl` like `_id`, no SearchParameter resource | |
| **Zero storage overhead** - 8 bytes nullable column vs kilobytes of duplicated JSON | |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
  - Header parsed in API layer
  - Passed through Application layer
  - Stored in Data layer column
- [x] F5 Developer Experience (works with minimal setup)
  - No configuration required
  - No SearchParameter resources to create
  - Just add header to requests
- [x] FHIR spec compliance
  - Uses HTTP headers (allowed by spec)
  - Resource content unchanged
  - Custom `_ttl` parameter follows FHIR conventions
- [x] Consistent with existing patterns
  - Similar to `_id`, `_lastUpdated` built-in parameters
  - Column + index pattern matches existing schema

## Implementation

### 1. Schema Change

```csharp
// ResourceEntity.cs
[Column("ExpiresAt")]
public DateTimeOffset? ExpiresAt { get; set; }
```

```sql
-- Migration
ALTER TABLE dbo.Resource ADD ExpiresAt DATETIMEOFFSET NULL;

CREATE INDEX IX_Resource_ExpiresAt
ON dbo.Resource(ExpiresAt)
WHERE ExpiresAt IS NOT NULL AND IsDeleted = 0 AND IsHistory = 0;
```

### 2. Header Parsing

```csharp
// In endpoint or middleware
DateTimeOffset? ParseTtlHeader(HttpContext context)
{
    if (!context.Request.Headers.TryGetValue("X-TTL", out var header))
        return null;  // Not provided = null (lives forever)

    var value = header.ToString();

    if (string.IsNullOrEmpty(value) || value == "0")
        return null;  // Explicit clear

    if (DateTimeOffset.TryParse(value, out var parsed))
        return parsed;

    throw new BadRequestException($"Invalid X-TTL header: {value}");
}
```

### 3. Repository Integration

```csharp
// IFhirRepository
Task<UpdateResult> CreateOrUpdateAsync(
    ResourceWrapper wrapper,
    DateTimeOffset? expiresAt,  // From X-TTL header
    CancellationToken cancellationToken);

// SqlEntityFrameworkRepository
public async Task<UpdateResult> CreateOrUpdateAsync(
    ResourceWrapper wrapper,
    DateTimeOffset? expiresAt,
    CancellationToken cancellationToken)
{
    // ... existing logic ...

    entity.ExpiresAt = expiresAt;  // Just set it

    await _context.SaveChangesAsync(cancellationToken);
    return result;
}
```

### 4. Search Parameter

```csharp
// In SearchParameterQueryGenerator or SearchOptionsBuilder
case "_ttl":
    var comparator = ParseComparator(paramValue);  // lt, gt, eq, etc.
    var dateValue = ParseDateTime(paramValue);

    query = comparator switch
    {
        "lt" => query.Where(r => r.ExpiresAt < dateValue),
        "le" => query.Where(r => r.ExpiresAt <= dateValue),
        "gt" => query.Where(r => r.ExpiresAt > dateValue),
        "ge" => query.Where(r => r.ExpiresAt >= dateValue),
        "eq" => query.Where(r => r.ExpiresAt == dateValue),
        _ => query
    };
    break;

case "_ttl:missing":
    var isMissing = bool.Parse(paramValue);
    query = isMissing
        ? query.Where(r => r.ExpiresAt == null)
        : query.Where(r => r.ExpiresAt != null);
    break;
```

### 5. Cleanup Service

```csharp
public class TtlCleanupService : BackgroundService
{
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var deleted = await PurgeExpiredResourcesAsync(stoppingToken);

            if (deleted > 0)
            {
                _logger.LogInformation("TTL cleanup: purged {Count} expired resources", deleted);
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task<int> PurgeExpiredResourcesAsync(CancellationToken ct)
    {
        // Single batch operation using temp table
        var deleted = await _context.Database.ExecuteSqlRawAsync("""
            -- Collect surrogate IDs for expired resources (current + history)
            CREATE TABLE #ExpiredSurrogates (ResourceSurrogateId BIGINT PRIMARY KEY);

            INSERT INTO #ExpiredSurrogates (ResourceSurrogateId)
            SELECT r.ResourceSurrogateId
            FROM Resource r
            WHERE EXISTS (
                SELECT TOP (@p0) 1
                FROM Resource curr
                WHERE curr.ExpiresAt IS NOT NULL
                  AND curr.ExpiresAt < GETUTCDATE()
                  AND curr.IsHistory = 0
                  AND curr.IsDeleted = 0
                  AND curr.ResourceTypeId = r.ResourceTypeId
                  AND curr.ResourceId = r.ResourceId
                ORDER BY curr.ExpiresAt
            );

            -- Delete from all search param tables
            DELETE FROM StringSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM DateTimeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM TokenSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM ReferenceSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM QuantitySearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM NumberSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM UriSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM TokenTokenCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM TokenStringCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM TokenQuantityCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM TokenNumberNumberCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM TokenDateTimeCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);
            DELETE FROM ReferenceTokenCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);

            -- Delete resource rows (returns count)
            DELETE FROM Resource WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM #ExpiredSurrogates);

            DROP TABLE #ExpiredSurrogates;
            """, BatchSize, ct);

        return deleted;
    }
}
```

## Evidence

### Industry Comparison

| Server | TTL Approach | Notes |
|--------|--------------|-------|
| **Microsoft FHIR** | None built-in | Uses `$bulk-delete`, `$purge-history` for manual cleanup |
| **HAPI FHIR** | None built-in | Cache TTL only, not resource expiration |
| **AWS HealthLake** | None | Manual delete operations |
| **Firely Server** | None | `$erase` operation for hard delete |
| **Cosmos DB** | Database-level TTL | Per-container or per-item, automatic deletion |

**Conclusion**: No FHIR server implements resource-level TTL. This design fills a gap.

### Existing Patterns in Ignixa

**Built-in search parameters** (special-cased, not SearchParameter resources):
- `_id` → Direct column comparison
- `_lastUpdated` → Direct column comparison (via ResourceSurrogateId range)

`_ttl` follows the same pattern - direct column, no intermediate search index table.

### HTTP Header Precedent

FHIR spec allows custom headers. Existing examples:
- `X-Provenance` - Server processes Provenance from header
- `Prefer` - Controls response format
- `If-Match` - Optimistic locking

`X-TTL` follows this convention.

## Verdict

**Recommended for implementation.**

This is the simplest viable design:
- One column, one index
- One header for input
- One search parameter for queries
- No resource mutation, no versioning impact
- No external dependencies

### Implementation Phases

**Phase 1: Core**
1. Add `ExpiresAt` column to `ResourceEntity`
2. Database migration
3. Parse `X-TTL` header in `CreateOrUpdateResourceHandler`
4. Pass through to repository

**Phase 2: Search**
5. Add `_ttl` handling in `SearchOptionsBuilder`
6. Add `_ttl` handling in `SearchParameterQueryGenerator`

**Phase 3: Cleanup**
7. Implement `TtlCleanupService` background job
8. Configuration for cleanup interval and batch size

**Phase 4: Testing**
9. Unit tests for header parsing
10. Integration tests for search parameter
11. E2E tests for cleanup job
