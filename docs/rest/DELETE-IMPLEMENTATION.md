# FHIR DELETE Implementation

**Status**: ✅ COMPLETED
**Date**: January 17, 2025
**FHIR Version**: R4 (Specification Section 3.1.0.7.1)

## Overview

Implemented FHIR R4 compliant soft delete functionality across all layers of the architecture.

## FHIR R4 Specification Compliance

Per FHIR R4 Section 3.1.0.7.1, DELETE implements **logical deletion** (soft delete), not physical deletion:

- ✅ Creates new version with `IsDeleted=true` (tombstone)
- ✅ Increments version number on deletion
- ✅ Subsequent GET returns **410 Gone** (not 404)
- ✅ Deletion appears in `_history` endpoint
- ✅ Idempotent (multiple DELETEs return same result)

## HTTP Status Codes

| Scenario | Status Code | Description |
|----------|-------------|-------------|
| Successful deletion | **204 No Content** | Resource deleted, tombstone created |
| Resource never existed | **404 Not Found** | No resource to delete |
| Already deleted | **204 No Content** | Idempotent, returns existing tombstone version |
| GET deleted resource | **410 Gone** | Resource existed but has been deleted |

## Implementation Details

### Domain Layer

**IFhirRepository.cs** - Added DeleteAsync method:
```csharp
ValueTask<ResourceKey?> DeleteAsync(
    ResourceKey key,
    ResourceRequest request,
    TransactionId? transactionId = null,
    CancellationToken ct = default);
```

**Returns**:
- `ResourceKey` with new deleted version if deletion successful
- `null` if resource never existed (404 Not Found)
- Same `ResourceKey` if already deleted (idempotent)

### Data Layer

#### FileBasedFhirRepository (FileBasedFhirRepository.cs:183-283)

**Implementation**:
1. Check if resource exists (search for metadata file)
2. Read current metadata to get version
3. Check if already deleted (idempotency)
4. Create tombstone JSON with minimal content:
   ```json
   {
     "resourceType": "Patient",
     "id": "123",
     "meta": {
       "versionId": "4",
       "lastUpdated": "2025-01-17T..."
     }
   }
   ```
5. Write tombstone to versioned file: `{id}.{version}.json`
6. Write metadata with `IsDeleted=true`
7. Update current symlink

#### SqlEntityFrameworkRepository (SqlEntityFrameworkRepository.cs:147-251)

**Implementation**:
1. Query for current version (IsHistory=false)
2. Check if already deleted (idempotency)
3. Mark current entity as history (IsHistory=true)
4. Create new entity with:
   - Incremented version
   - `IsDeleted=true`
   - `RequestMethod="DELETE"`
   - Compressed tombstone JSON
5. Save to database

**Also Updated GetAsync** (lines 90-93):
- Removed filter that excluded deleted resources
- Now returns deleted resources with `IsDeleted=true`
- Allows API layer to return 410 Gone

### Application Layer

**DeleteResourceHandler.cs** - Full implementation:
- Extracts tenant ID from HttpContext
- Creates PartitionResolutionContext
- Calls DetermineWritePartition with minimal ResourceJsonNode
- Validates single partition for write
- Gets repository from factory
- Calls repository.DeleteAsync()
- Returns bool (true if deleted, false if not found)

### API Layer

**FhirEndpoints.cs** - Updated GET endpoint (lines 211-229):
```csharp
if (result.IsDeleted)
{
    return Results.Problem(
        statusCode: StatusCodes.Status410Gone,
        title: "Resource Deleted",
        detail: $"{resourceType}/{id} has been deleted (last version: {result.VersionId})");
}
```

**DELETE endpoint** - Already existed, no changes needed.

## Testing

### Build Status
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Test Results
```
Passed!  - Failed: 0, Passed: 427, Skipped: 0, Total: 427
```

### Manual Integration Tests

See `docs/rest/delete-integration-test.http` for comprehensive test scenarios:

1. ✅ Create resource → DELETE → verify 410 Gone
2. ✅ DELETE already deleted resource (idempotency)
3. ✅ DELETE non-existent resource → 404 Not Found
4. ✅ Multi-tenant DELETE with explicit tenant route
5. ✅ Verify deletion appears in `_history`

**To run tests**:
1. Start API: `dotnet run --project src/Ignixa.Api`
2. Open `docs/rest/delete-integration-test.http` in VS Code
3. Execute each request sequentially (use REST Client extension)

## Key Files Modified

| File | Changes | Lines |
|------|---------|-------|
| `Ignixa.Domain/Abstractions/IFhirRepository.cs` | Added DeleteAsync method | 139-143 |
| `Ignixa.DataLayer.FileSystem/FileBasedFhirRepository.cs` | Implemented DeleteAsync | 183-283 |
| `Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepository.cs` | Implemented DeleteAsync | 147-251 |
| `Ignixa.Application/Features/Resource/DeleteResourceHandler.cs` | Full implementation | 37-91 |
| `Ignixa.Api/Infrastructure/FhirEndpoints.cs` | Added 410 Gone check | 211-229 |

## Multi-Tenancy Support

DELETE fully supports multi-tenancy:

- ✅ Tenant-explicit routes: `DELETE /tenant/{tenantId}/Patient/{id}`
- ✅ Tenant-agnostic routes (single-tenant): `DELETE /Patient/{id}`
- ✅ Partition strategy determines write partition
- ✅ Repository factory creates tenant-specific repositories

## Future Enhancements

1. **Bundle DELETE Support** - Add DELETE to transaction/batch bundles
2. **Conditional DELETE** - `DELETE /Patient?identifier=...`
3. **Hard Delete** - Admin endpoint for permanent deletion (GDPR compliance)
4. **Restore** - Endpoint to un-delete resources (create new non-deleted version)

## References

- **FHIR R4 Specification**: http://hl7.org/fhir/R4/http.html#delete
- **ADR-2523**: Multi-Tenancy Data Partitioning
- **Integration Tests**: `docs/rest/delete-integration-test.http`
