# Investigation: $includes Operation (Paginated Includes)

**Feature**: search
**Status**: Implemented
**Created**: 2025-12-27
**Implemented**: 2025-12-28

## Problem Statement

When using `_include` or `_revinclude`, large result sets can cause performance issues and response size problems. The `$includes` operation provides independent pagination for included resources, allowing clients to fetch primary search results and their related resources separately.

## Approach

Implement the `$includes` operation as documented in Microsoft FHIR Server, supporting:

1. **`_includesCount` parameter** - Separate limit for included resources (default: unbounded)
2. **`_includesContinuationToken` parameter** - Independent pagination token for includes
3. **"related" link** - Bundle link pointing to next page of included resources
4. **`$includes` operation endpoint** - Direct access to included resources

### Example Usage

```
# Initial search with limited includes
GET /Patient?_include=Patient:organization&_count=10&_includesCount=50

# Response includes:
# - 10 patients (primary matches)
# - Up to 50 organizations (included resources)
# - "related" link if more organizations exist

# Follow "related" link for more includes
GET /Patient/$includes?_includesContinuationToken=xyz123
```

## Implementation Options

### Option A: Extend Existing Include Logic

Modify `IncludeSearchHandler` to:
- Track include counts separately from primary results
- Generate continuation tokens for include pagination
- Add "related" link to bundle when includes are truncated

**Pros:**
- Minimal new infrastructure
- Reuses existing include resolution logic

**Cons:**
- Continuation token management adds complexity
- State management for include pagination across requests

### Option B: Separate Operation Handler

Create dedicated `IncludesOperationHandler` that:
- Accepts continuation token and replays include resolution
- Returns only included resources (not primary matches)
- Manages its own pagination state

**Pros:**
- Clear separation of concerns
- Easier to test independently
- Follows FHIR operation pattern

**Cons:**
- Some code duplication with search handler
- Additional endpoint to maintain

### Option C: Two-Phase Search (Recommended)

Search execution returns:
1. **Phase 1**: Primary matches with includes up to `_includesCount`
2. **Phase 2**: Continuation fetches remaining includes only

Use existing continuation token infrastructure but encode phase information.

**Pros:**
- Builds on proven continuation token pattern
- Single code path for include resolution
- Consistent UX with existing pagination

**Cons:**
- Phase tracking adds token complexity

## Tradeoffs

| Pros | Cons |
|------|------|
| Prevents response size explosion | Requires client to follow links for full data |
| Improves performance for large include sets | Adds API complexity |
| Consistent with Microsoft FHIR Server | Additional state management |
| Enables progressive loading UX | Continuation token storage requirements |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data) - Operation handler in Application layer
- [x] F5 Developer Experience - Works out of box, no special config
- [x] FHIR spec compliance - Extension, not spec-defined but common pattern
- [ ] Consistent with existing patterns - Need to review continuation token handling

## Evidence

### Microsoft FHIR Server Implementation

From `IncludesOperationTests.cs`:
- Tests `_includesCount` parameter limiting included resources
- Tests continuation via "related" link
- Tests multiple resource types in includes

### Existing Codebase Patterns

Current include implementation in:
- `src/Core/Ignixa.Search/InMemory/IncludeResolver.cs` - Resolves include targets
- `src/Ignixa.Application/Features/Search/SearchHandler.cs` - Orchestrates search with includes

Continuation token handling in:
- `src/Ignixa.Application/Features/Search/ContinuationTokenService.cs`

### Related ADRs

- ADR 2509: InMemory Search Architecture - Established include resolution pattern

## Alternative Investigations

1. **streaming-includes** - Server-sent events for progressive include loading
2. **graphql-includes** - GraphQL-style field selection instead of FHIR includes
3. **include-caching** - Cache include results for faster pagination

## Decision

**Implemented Option C (Two-Phase Search)** - The implementation follows the two-phase pattern with encoding/decoding of continuation tokens for include pagination.

## Implementation Details

### Architecture

The implementation consists of three main components:

1. **Query Parameters** (`SearchOptions`):
   - `IncludesMaxItemCount` - Maximum number of included resources to return per page
   - `IncludesContinuationToken` - Base64-encoded token containing pagination state

2. **Handler** (`IncludesResourceHandler`):
   - Decodes continuation token to extract offset and page size
   - Re-executes the original search with include resolution
   - Filters results to only Include entries (skips Match entries)
   - Applies pagination based on offset and page size
   - Returns Bundle with only included resources

3. **Endpoint** (`OperationEndpoints`):
   - `GET /{resourceType}/$includes?_includesContinuationToken=...`
   - Supports both tenant-explicit and tenant-agnostic routes
   - Validates required `_includesContinuationToken` parameter

### Continuation Token Format

The `IncludesContinuationToken` class handles encoding/decoding:

```csharp
// Token encodes:
{
  "IncludesOffset": 50,    // Number of includes already returned
  "PageSize": 50            // Page size for this request
}
```

Encoded as Base64 JSON for transmission in URLs.

### Flow

1. **Initial search with `_includesCount`**:
   ```
   GET /Patient?_include=Patient:organization&_count=10&_includesCount=50
   ```
   - Returns up to 10 patients (primary matches)
   - Returns up to 50 organizations (includes)
   - If more includes exist, adds "related" link with `_includesContinuationToken`

2. **Follow-up request via $includes**:
   ```
   GET /Patient/$includes?_includesContinuationToken=xyz123&_include=Patient:organization
   ```
   - Returns Bundle with only Include entries
   - Uses standard "next" link for additional pages
   - No Match entries in response

### Key Design Decisions

1. **Filter-based approach**: Re-executes search and filters to Include entries only
   - Simpler than maintaining separate state
   - Consistent with existing include resolution logic
   - Trades computation for state management complexity

2. **Streaming serialization**: Uses `StreamingBundleSerializer` for memory efficiency
   - Handles large include sets without loading all into memory
   - Consistent with other search operations

3. **Continuation token validation**: Validates token presence at endpoint
   - Clear error message when missing required parameter
   - Prevents invalid requests early in pipeline

### Testing

The implementation is tested in:
- `IncludesOperationTests.cs` - Integration tests for $includes operation
- `ContinuationTokenTests.cs` - Unit tests for token encoding/decoding

## Verdict

**ACCEPTED** - Successfully implemented. The two-phase search pattern provides:
- Independent pagination for included resources
- Consistency with existing continuation token infrastructure
- Memory-efficient streaming for large result sets
- Clear separation between Match and Include entries in paginated results
