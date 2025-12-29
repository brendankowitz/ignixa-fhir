# Investigation: $includes Operation (Paginated Includes)

**Feature**: search
**Status**: In Progress
**Created**: 2025-12-27

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

## Verdict

*Pending evaluation* - Recommend Option C (Two-Phase Search) for consistency with existing patterns. Need to assess continuation token storage impact for large include sets.
