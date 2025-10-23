# ADR-2502: FHIR Compartment Wildcard Search

**Status**: Implemented
**Date**: 2025-10-22
**Phase**: Phase 1.0 Enhancement

## Context

FHIR R4 specification (Section 3.1.0.9.1) defines a special compartment search syntax using `/*` (literal asterisk) to search across ALL resource types within a compartment:

```
GET /Patient/123/*
GET /Encounter/456/*
```

This differs from type-specific compartment searches:
```
GET /Patient/123/Observation    # Only Observations
GET /Patient/123/*              # ALL resources in Patient/123 compartment
```

### Current Status

✅ **Implemented**:
- Compartment search for specific resource types: `GET /Patient/123/Observation`
- `ICompartmentDefinitionManager.TryGetResourceTypes()` method exists
- `SearchCompartmentHandler` uses compartment search rewriter pattern

❌ **Missing**:
- Routes for `/{compartmentType}/{compartmentId}/*` pattern
- Handler logic to expand `*` into multi-resource-type search

## FHIR Specification Summary

From [FHIR R4 Section 3.1.0.9.1](https://www.hl7.org/fhir/http.html#search):

### Syntax
```
GET [base]/[compartmentType]/[compartmentId]/*{?[parameters]}
```

### Behavior
- `/*` is a **literal character** in the URL (not a wildcard pattern)
- Returns all resources in the compartment across ALL included resource types
- Supports standard search parameters: `_count`, `_type`, `_since`, `_elements`, `_summary`
- Returns `200 OK` with empty Bundle if compartment doesn't exist (not 404)
- Returns `400 Bad Request` for invalid compartment types
- Returns `400 Bad Request` if resource-type-specific search parameters are used (e.g., `gender` on `Patient/*`)

### Search Parameter Restrictions
| Parameter Type | Allowed on `/*` | Example |
|----------------|----------------|---------|
| Common parameters | ✅ Yes | `_count`, `_type`, `_since`, `_elements` |
| Resource-type-specific | ❌ No | `gender` (Patient-only), `code` (Observation-only) |
| Compartment inclusion parameters | ✅ Yes | All parameters defined in CompartmentDefinition |

### Example
```http
GET /Patient/123/*?_count=20&_type=Condition,Observation
```

Returns up to 20 resources of type Condition OR Observation from Patient/123's compartment.

## Design Decision

### Approach: Fan-Out Search Across Resource Types

**Selected Strategy**: Execute N separate searches (one per resource type) and merge results via streaming.

**Rationale**:
1. **Works with current architecture** - Search infrastructure is resource-type-specific
2. **Streaming-friendly** - IAsyncEnumerable enables zero-copy merging
3. **No _type parameter dependency** - _type parameter not yet implemented in search pipeline
4. **Compartment-aware** - Each search uses CompartmentSearchRewriter for correct filtering

### Implementation Pattern

```csharp
// 1. Detect wildcard in endpoint
if (resourceType == "*")
{
    // 2. Enumerate all resource types in compartment
    if (!compartmentDefinitionManager.TryGetResourceTypes(compartmentType, out var resourceTypes))
    {
        throw InvalidOperationException("Invalid compartment type");
    }

    // 3. Create async enumerable that merges all resource type searches
    async IAsyncEnumerable<SearchEntryResult> MergeResourceStreams()
    {
        foreach (var resourceType in resourceTypes)
        {
            // Create compartment expression for this resource type
            var compartmentExpression = new CompartmentSearchExpression(...);
            var rewrittenExpression = rewriter.VisitCompartment(compartmentExpression, ...);

            // Execute search for this resource type
            var resourceStream = executionStrategy.SearchStreamAsync(partition, searchOptions, ct);

            // Stream results from this resource type
            await foreach (var resource in resourceStream)
            {
                yield return resource;
            }
        }
    }

    return new SearchResourcesResult(Resources: MergeResourceStreams(), ...);
}
```

### Alternative Approaches Considered

| Approach | Pros | Cons | Decision |
|----------|------|------|----------|
| **Fan-out queries** (chosen) | Works with existing arch, streaming-friendly | N queries (sequential) | ✅ **SELECTED** |
| **_type parameter expansion** | Single query | _type not implemented yet | ❌ Not viable (Phase 1.2 feature) |
| **Special query executor** | Custom optimization | Duplicates search logic | ❌ Rejected |

## Performance Considerations

### Query Performance
- **Sequential fan-out**: N queries executed sequentially (one per resource type)
- **Per-query overhead**: Each resource type incurs separate index lookups
- **Expected latency**: Sum of all resource type search latencies (e.g., Patient compartment ~120 types)
- **Index usage**: Each query uses resource-type-specific indexes
- **Streaming**: Results streamed as they arrive (no buffering)

### Memory Usage
- **Streaming Bundle response**: Same 95% memory reduction as other searches
- **Async enumerable merging**: Zero-copy result aggregation
- **No intermediate buffering**: Results yielded directly from each resource type search

### Expected Resource Type Counts (R4)
| Compartment | Resource Types | Expected Queries |
|-------------|---------------|------------------|
| Patient | ~120 types | Up to 120 sequential searches |
| Practitioner | ~40 types | Up to 40 sequential searches |
| Encounter | ~30 types | Up to 30 sequential searches |
| Device | ~15 types | Up to 15 sequential searches |
| RelatedPerson | ~10 types | Up to 10 sequential searches |

**Note**: Only resource types with compartment data will return results; empty types complete instantly.

## Implementation Files

### Modified Files
1. **CompartmentEndpoints.cs** - Add `/*` routes (tenant-explicit + agnostic)
2. **SearchCompartmentHandler.cs** - Detect `*` and expand to `_type` parameter
3. **SearchCompartmentQuery.cs** - Allow `ResourceType = "*"`
4. **SearchCompartmentHandlerTests.cs** - Unit tests for wildcard behavior

### No Changes Required
- **SearchHandler.cs** - Already supports `_type` parameter
- **ICompartmentDefinitionManager** - `TryGetResourceTypes()` method exists
- **CompartmentSearchRewriter** - Works with `_type` parameter

## Testing Strategy

### Unit Tests (SearchCompartmentHandlerTests.cs)
```csharp
[Fact]
public async Task GivenWildcardSearch_WhenPatientCompartment_ThenReturnsAllResourceTypes()
{
    // Arrange: Patient with Observation, Condition, MedicationRequest
    // Act: GET /Patient/123/*
    // Assert: Bundle contains all 3 resource types
}

[Fact]
public async Task GivenWildcardWithTypeFilter_WhenSearching_ThenReturnsFilteredTypes()
{
    // Arrange: Patient with multiple resource types
    // Act: GET /Patient/123/*?_type=Condition
    // Assert: Only Conditions returned
}

[Fact]
public async Task GivenWildcardWithInvalidCompartment_WhenSearching_ThenReturns400()
{
    // Act: GET /InvalidCompartment/123/*
    // Assert: 400 Bad Request
}

[Fact]
public async Task GivenWildcardForNonexistentCompartment_WhenSearching_ThenReturnsEmptyBundle()
{
    // Act: GET /Patient/nonexistent/*
    // Assert: 200 OK with empty Bundle
}
```

### Integration Tests (SearchExamples.http)
```http
### Compartment Search All Types
GET {{hostname}}/Patient/123/*

### Compartment Search All Types (Tenant-Explicit)
GET {{hostname}}/tenant/1/Patient/123/*

### Compartment Search with Type Filter
GET {{hostname}}/Patient/123/*?_type=Condition,Observation

### Compartment Search with Count
GET {{hostname}}/Patient/123/*?_count=10
```

## Error Handling

| Scenario | HTTP Status | Response |
|----------|-------------|----------|
| Invalid compartment type | 400 Bad Request | OperationOutcome with diagnostic |
| Nonexistent compartment ID | 200 OK | Empty Bundle (FHIR spec requirement) |
| Resource-type-specific param | 400 Bad Request | "Parameter 'gender' not applicable to wildcard search" |
| Valid wildcard search | 200 OK | Bundle with searchset entries |

## Security Considerations

- **Same authorization** as type-specific compartment searches
- **No additional data exposure** - only returns resources already in compartment
- **Multi-tenant isolation** - Respects tenant boundaries

## Migration Path

### Phase 1: Basic Implementation (This ADR)
✅ Add `/*` routes
✅ Expand to `_type` parameter
✅ Basic validation
✅ Unit tests

### Phase 2: Advanced Features (Future)
- Resource-type-specific parameter validation
- Performance optimization (pre-compute resource type lists)
- Enhanced error messages for invalid parameters

## References

- [FHIR R4 Compartment Search](https://www.hl7.org/fhir/http.html#search)
- [FHIR R4 CompartmentDefinition](https://www.hl7.org/fhir/compartmentdefinition.html)
- ADR-2501: Prototype Phase Implementation
- ADR-2523: Multi-Tenancy Architecture

## Decision

**APPROVED**: Implement compartment wildcard search using `_type` parameter expansion strategy.

**Rationale**: Simplest approach that reuses existing infrastructure, provides FHIR-compliant behavior, and maintains streaming performance characteristics.
