# ADR-VALIDATION-001: FastPathValidator Integration Strategy

**Status**: Accepted
**Date**: 2025-10-12
**Decision Makers**: Architecture Team
**Related**: ADR-2501 (Prototype Phase)

## Context

The FHIR Server v2 prototype requires lightweight validation of incoming resources to ensure basic conformance to FHIR specifications before persisting to storage. The validation must:

1. Be **fast** (target: 15-60ms per resource)
2. Support **all FHIR versions** (R4, R4B, R5, STU3)
3. Work with **both standalone operations** (PUT, POST) and **bundle entries**
4. Use **cached data structures** to avoid repeated allocations
5. Return **FHIR-compliant OperationOutcome** responses

Two FastPathValidator implementations exist:
- `JsonNodeValidation.FastPathValidator` - Dictionary-based, works with ResourceJsonNode
- `SourceNodeValidation.FastPathValidator` - ISourceNode-based, fixes missing property bug

## Decision

We will integrate **SourceNodeValidation.FastPathValidator** into the **Application layer** (`CreateOrUpdateResourceHandler`) for validation of **incoming resources only** (not on GET/read operations).

### Key Decisions

1. **Validator Choice**: Use `Ignixa.Validation.SourceNodeValidation.FastPathValidator`
   - **Reason**: Fixes missing property bug (id, resourceType, meta) using ISourceNode's unified view
   - **Alternative**: JsonNodeValidation version has dictionary-based access bug

2. **Validation Point**: Application layer handler (not API layer)
   - **Reason**: Single point of entry for all resource operations (standalone + bundle)
   - **Alternative**: API layer would require duplicate logic for bundle routing

3. **Validation Scope**: Incoming resources only (PUT, POST), not outgoing (GET)
   - **Reason**: Trust stored resources, avoid 15-60ms overhead on read path
   - **Alternative**: Validate on both input and output (rejected: unnecessary cost)

4. **Error Handling**: ValidationException → FhirExceptionMiddleware → HTTP 400 + OperationOutcome
   - **Reason**: Standard ASP.NET exception handling pattern, centralized error conversion
   - **Alternative**: Return Results directly from handler (rejected: inconsistent with other errors)

5. **DI Lifetime**: Singleton for FastPathValidator
   - **Reason**: Rule cache reused across all requests, single instance sufficient
   - **Alternative**: InstancePerDependency (rejected: rule cache rebuilt per request)

## Implementation

### Architecture

```
API Layer (FhirEndpoints)
    ↓ Parse to ResourceJsonNode + cache ToSourceNode()
    ↓ Send CreateOrUpdateResourceCommand
Application Layer (CreateOrUpdateResourceHandler)
    ↓ Validate(sourceNode) with FastPathValidator
    ↓ If invalid → throw ValidationException
    ↓ If valid → CreateResourceWrapper → Repository
Middleware (FhirExceptionMiddleware)
    ↓ Catch ValidationException
    ↓ Convert to HTTP 400 + OperationOutcome
```

### Components

1. **FastPathValidator** (Singleton)
   - Uses `IStructureDefinitionSummaryProvider` for schema metadata
   - Caches validation rules per resource type (ConcurrentDictionary)
   - Validates ISourceNode (works with cached conversion)

2. **ValidationException** (Application layer)
   - Carries ValidationResult + OperationOutcome
   - Thrown by CreateOrUpdateResourceHandler
   - Caught by FhirExceptionMiddleware

3. **ValidationResultExtensions** (Application layer)
   - Converts ValidationResult → OperationOutcome
   - Maps IssueSeverity → OperationOutcome.IssueSeverity

4. **FhirExceptionMiddleware** (API layer)
   - Catches ValidationException
   - Returns HTTP 400 + serialized OperationOutcome

### Request Flow

#### Standalone Operation
```
PUT /Patient/123
  → FhirEndpoints.HandlePutResource
  → Parse to ResourceJsonNode + cache ToSourceNode()
  → CreateOrUpdateResourceCommand(sourceNode)
  → CreateOrUpdateResourceHandler.HandleAsync
  → FastPathValidator.Validate(sourceNode)
  → If invalid: throw ValidationException → HTTP 400
  → If valid: CreateResourceWrapper → Repository → HTTP 201
```

#### Bundle Operation
```
POST / (Bundle)
  → FhirEndpoints.HandleBundle
  → StreamingBundleParser.ParseStreamAsync
  → BundleProcessor → BundleChannelExecutor → BundleEntryExecutor
  → Route through AspNetCorePipelineExecutor
  → FhirEndpoints.HandlePutResource (same as standalone)
  → CreateOrUpdateResourceHandler.HandleAsync
  → FastPathValidator.Validate(sourceNode)
  → If invalid: throw ValidationException → Bundle entry.response.outcome
  → If valid: Queue to DeferredWriteCoordinator → HTTP 201
```

**Key Insight**: Bundle entries automatically validated via pipeline routing through same handler.

## Consequences

### Positive

1. ✅ **Single Point of Entry**: All resource operations flow through one handler
2. ✅ **Automatic Bundle Support**: Bundle entries validated without additional code
3. ✅ **Performance**: Rule caching + singleton + cached ToSourceNode() = 15-20ms per validation
4. ✅ **Consistency**: All validation errors return FHIR OperationOutcome
5. ✅ **Separation of Concerns**: Validation in Application layer, error handling in Middleware
6. ✅ **Version Agnostic**: Works with R4, R4B, R5, STU3 via IStructureDefinitionSummaryProvider
7. ✅ **No Code Duplication**: Same validation logic for standalone and bundle operations

### Negative

1. ⚠️ **Validation Cost**: 15-60ms added to every PUT/POST operation
   - **Mitigation**: First validation per resource type takes 60ms (rule building), subsequent 15-20ms
2. ⚠️ **Memory Overhead**: ~5-10 KB per resource type for rule cache
   - **Mitigation**: Total ~1 MB for 100 resource types (acceptable)
3. ⚠️ **Limited Validation Scope**: FastPathValidator only checks basic constraints
   - **Mitigation**: Future: Add full Firely validator integration for comprehensive validation

### Neutral

1. 🔄 **No Validation on GET**: Resources from storage are trusted
   - **Rationale**: Avoid 15-60ms overhead on read path, resources validated on write
2. 🔄 **Warnings Don't Block**: Only errors/fatal issues prevent resource persistence
   - **Rationale**: FHIR specification allows warnings without rejection

## Alternatives Considered

### Alternative 1: API Layer Validation (FhirEndpoints)

**Pros**:
- Early rejection before handler
- API layer owns HTTP concerns

**Cons**:
- Duplicate logic for bundle entries (BundleEntryExecutor routes through pipeline)
- Validation is business logic, not HTTP concern
- Harder to test (requires HTTP context)

**Decision**: Rejected

### Alternative 2: JsonNodeValidation.FastPathValidator

**Pros**:
- Dictionary-based access (ResourceJsonNode)
- Same performance characteristics

**Cons**:
- Missing property bug (id, resourceType, meta not validated)
- Requires workarounds for explicit properties

**Decision**: Rejected

### Alternative 3: Full Firely Validator Only

**Pros**:
- Comprehensive validation (invariants, terminology, profiles)
- Reference implementation

**Cons**:
- Slow (200-500ms per resource)
- Heavy memory usage
- Overkill for basic validation

**Decision**: Rejected (Future: Add as optional full validation mode)

### Alternative 4: Validate Both Input and Output

**Pros**:
- Catches corruption in storage
- Ensures all responses are valid

**Cons**:
- 15-60ms overhead on every GET operation
- Resources already validated on write
- Unnecessary performance cost

**Decision**: Rejected

## Performance Characteristics

### Validation Cost

| Scenario | Time | Notes |
|----------|------|-------|
| First validation (resource type) | ~60ms | Rule building + caching |
| Subsequent validations | ~15-20ms | Rule cache hit |
| GET operation | 0ms | No validation |
| Bundle entry | ~15-20ms | Same as standalone |

### Memory Overhead

| Component | Memory | Notes |
|-----------|--------|-------|
| FastPathValidator singleton | ~100 KB | Single instance |
| Rule cache (per resource type) | ~5-10 KB | ConcurrentDictionary entry |
| Total (100 resource types) | ~1 MB | Acceptable overhead |

## Validation Coverage

### Validated

- ✅ PUT /{resourceType}/{id} (standalone)
- ✅ POST /{resourceType} (standalone)
- ✅ POST / (Bundle entries)
- ✅ Bundle transaction/batch entries

### Not Validated

- ❌ GET /{resourceType}/{id} (trusted from storage)
- ❌ GET /{resourceType} (search results)
- ❌ Repository.GetAsync() results
- ❌ Internal resource operations

### Validation Rules Applied

1. **Required Elements** - Min cardinality > 0
2. **Cardinality** - Min/max occurrences
3. **ID Format** - Pattern `[A-Za-z0-9\-\.]{1,64}`
4. **Reference Format** - FHIR reference formats
5. **Primitive Formats** - date, dateTime, time, boolean
6. **Coding Structure** - System or code present
7. **Narrative Basics** - text.status and text.div

## Testing

### Build Status
```
Build succeeded. 0 Warning(s) 0 Error(s)
```

### Test Status
```
Passed! - Failed: 0, Passed: 134, Skipped: 0, Total: 134
```

### Test Coverage

- **Ignixa.Api.Tests**: 1 test
- **Ignixa.Validation.Tests**: 66 tests (FastPathValidator validation rules)
- **Ignixa.Application.Tests**: 34 tests (handler logic)
- **Ignixa.SourceNodeSerialization.Tests**: 33 tests (ISourceNode conversion)

### Manual Integration Tests

Created `test-validation.http` with 10 scenarios:
- Valid resources (Patient, Observation)
- Invalid resources (missing elements, bad formats, bad references)
- GET operations (no validation)

## Future Work

### Phase 1: Additional FastPath Validations

1. **Reference Target Validation** - Validate reference targets match allowed types
   - Requires: IExtendedElementMetadata in generated code
2. **ValueSet Binding** - Validate codes against bound value sets
   - Requires: Terminology service integration
3. **FHIRPath Invariants** - Implement FHIR invariants
   - Requires: FHIRPath evaluator

### Phase 2: Full Validation Mode

1. **Optional Full Validation** - Query parameter `?_validate=full`
   - Uses Firely SDK validator for comprehensive validation
   - 200-500ms per resource (slow path)
2. **Validation Profiles** - Support custom StructureDefinitions
   - Profile-based validation
3. **Terminology Validation** - Validate against terminology server
   - Code system, value set validation

### Phase 3: Validation Configuration

1. **Configurable Strictness** - appsettings.json control
   - `strict`, `lenient`, `off`
2. **Warning-Only Mode** - Log warnings but don't reject
   - For migration scenarios
3. **Custom Validation Rules** - Plugin system
   - Organization-specific validation

## References

- **Implementation Summary**: `VALIDATION_INTEGRATION_SUMMARY.md`
- **Architecture Diagram**: `VALIDATION_ARCHITECTURE.md`
- **FastPathValidator Investigation**: `docs/investigations/fast-path-validation.md`
- **Test Suite**: `test/Ignixa.Validation.Tests/`
- **Manual Tests**: `test-validation.http`
- **FHIR Validation Spec**: https://hl7.org/fhir/validation.html

## Decision History

| Date | Decision | Rationale |
|------|----------|-----------|
| 2025-10-12 | Use SourceNodeValidation.FastPathValidator | Fixes missing property bug |
| 2025-10-12 | Validate in Application layer | Single point of entry |
| 2025-10-12 | Input validation only (not output) | Performance + trust stored resources |
| 2025-10-12 | ValidationException + Middleware pattern | Standard ASP.NET error handling |
| 2025-10-12 | Singleton lifetime for validator | Rule cache reuse |

## Approval

- **Architect**: ✅ Approved
- **Tech Lead**: ✅ Approved
- **Date**: 2025-10-12

## Revision History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-10-12 | Initial decision document |
