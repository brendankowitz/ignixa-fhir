# ADR 2520: Phase 17 - Patch Operations

## Metadata

- **ADR Number**: 2520
- **Title**: Phase 17 - Patch Operations
- **Status**: In Progress - FHIRPath Integration Phase
- **Date**: 2025-10-18 (Updated)
- **Phase**: 17 (Weeks 91-94)
- **Implementation Status**: 70% Complete (Custom path navigation) → Target: 100% (FHIRPath integrated)
- **Related Documents**:
  - [ADR-2500: Master Implementation Roadmap](adr-2500-master-implementation-roadmap.md)
  - [ADR-2502: Multi-Tenancy Architecture](adr-2502-multi-tenancy-architecture.md)
  - [Bundle Non-CRUD Operations Investigation](../investigations/bundle-non-crud-operations.md)

## Context

### Phase 17 Positioning

Phase 17 implements FHIR PATCH operations as defined in weeks 91-94 of the master roadmap. This phase follows:
- **Phase 16**: Compartment Search (Weeks 87-90) - Advanced search patterns
- **Phase 22**: FHIR _history Operations (COMPLETED 2025-10-17) - Resource history

And precedes:
- **Phase 18**: Batch Bundles (Weeks 95-98) - Non-transactional bundles

### Current State

As of 2025-10-18, the Ignixa FHIR Server v2 has completed:
- ✅ Phase 22: FHIR _history operations (instance/type/system level)
- ✅ Multi-tenant data partitioning (Isolation mode)
- ✅ Minimal API pattern for all CRUD endpoints
- ✅ Streaming Bundle responses (95% memory reduction)
- ✅ Transaction bundle support with automatic recovery
- ✅ Background services (IndexLoaderService, TransactionWatcherService)

**PATCH Operations Status**: 70% Complete (Partial Implementation)

**✅ Completed Components**:
1. **FhirPatchEngine.cs** - Core patch engine with in-place JsonObject mutation (409 lines)
2. **FhirPatchOperation.cs** - Operation model with 5 types (Add, Insert, Delete, Replace, Move)
3. **FhirPatchParametersParser.cs** - Parses Parameters resource → FhirPatchOperation[]
4. **PatchResourceHandler.cs** - Medino handler orchestrating patch workflow
5. **PatchEndpoints.cs** - Minimal API endpoints (tenant-explicit and tenant-agnostic)
6. **Multi-tenant routing** - Works with TenantResolutionMiddleware
7. **Immutable property protection** - Basic string-based validation
8. **All 5 operation types** - Add, Insert, Delete, Replace, Move implemented

**❌ Critical Gap**: **Custom Path Navigation (NOT FHIRPath)**

Current implementation uses simple string parsing:
- ✅ Supports: `Patient.name[0].family` (dot notation + array indexing)
- ❌ Missing: `Patient.name.where(use='official').family` (FHIRPath functions)
- ❌ Missing: `Patient.telecom.where(system='phone').first()`
- ❌ Missing: Complex expressions with `where()`, `first()`, `exists()`

**FHIR Spec Requirement**: FHIRPath evaluation is MANDATORY (per FHIR R4 Section 3.1.0.7.1), not optional.

**Remaining Work**:
1. ❌ **FHIRPath Integration** - Replace custom parsing with `Ignixa.FhirPath.Evaluation.FhirPathEvaluator`
2. ❌ **Strategy Pattern Refactor** - IOperationExecutor interface with 5 executors
3. ❌ **Validation Framework** - FhirPatchValidator, ImmutablePropertyValidator (proper implementation)
4. ❌ **Tests** - Zero tests currently (need 100+ tests)
5. ❌ **Documentation** - CLAUDE.md updates, capability statement

### FHIR Specification Background

FHIR R4 defines two types of PATCH operations in Section 3.1.0.7:

1. **FHIRPath Patch** (Section 3.1.0.7.1) - FHIR-native patch using Parameters resource
2. **JSON Patch** (Section 3.1.0.7.2) - Generic JSON patching per RFC 6902

**Key Differences**:

| Aspect | **FHIRPath Patch** | **JSON Patch (RFC 6902)** |
|--------|-------------------|---------------------------|
| **Specification** | FHIR R4 Section 3.1.0.7.1 | RFC 6902 (generic JSON) |
| **Format** | Parameters resource | JSON Patch document |
| **Path Syntax** | FHIRPath expressions | JSON Pointer |
| **FHIR Awareness** | Yes (types, cardinality, validation) | No (generic JSON) |
| **Content-Type** | `application/fhir+json` | `application/json-patch+json` |
| **Operations** | add, insert, delete, replace, move | add, remove, replace, move, copy, test |
| **Example Path** | `Patient.name[0].family` | `/name/0/family` |
| **Type Safety** | FHIR type validation | JSON structure only |
| **Validation** | Full FHIR schema validation | JSON structure validation |

### Use Cases

PATCH operations enable:

1. **Partial Updates**: Modify specific fields without sending entire resource (90% bandwidth reduction)
2. **Mobile Offline Sync**: Small delta updates for syncing offline changes
3. **Conflict Resolution**: Granular merge strategies for concurrent edits
4. **Audit Trails**: Track field-level changes (when combined with _history)
5. **Performance**: Reduce payload size for large resources (e.g., Bundle, Composition)

**Example Scenario**:
```
Patient resource: 150 KB (full resource)
PATCH request: 800 bytes (update phone number only)
Bandwidth savings: 99.5%
```

### Why FHIRPath Patch Over JSON Patch?

**Recommendation**: Implement **FHIRPath Patch first** (Phase 17 primary), JSON Patch as optional enhancement.

**Rationale**:
1. **FHIR-Native**: Uses FHIR Parameters resource (no new content-type negotiation)
2. **Type-Safe**: Validates against FHIR schema (prevents type mismatches)
3. **FHIRPath Expressions**: Reuses existing FHIRPath infrastructure (`Ignixa.FhirPath`)
4. **Immutable Properties**: Automatically protects id, meta.versionId, meta.lastUpdated
5. **Cardinality-Aware**: Understands 0..1 vs 0..* element constraints
6. **Interoperability**: Preferred by FHIR community (HAPI FHIR, SMART Health Cards)

**JSON Patch Limitations**:
- Requires JSON Pointer syntax (not FHIR-idiomatic)
- No FHIR type awareness (can set wrong types)
- Requires separate content-type header (`application/json-patch+json`)
- No built-in immutable property protection
- Generic tooling (not FHIR-specific validation)

## FHIRPath Integration Strategy

### Research Findings (2025-10-18)

**Critical Discovery**: FHIR R4 specification **REQUIRES** FHIRPath evaluation for PATCH operations, not simple path navigation.

**Evidence**:
1. **FHIR R4 Spec** (Section 3.1.0.7.1): "The `path` parameter SHALL contain a FHIRPath expression"
2. **Reference Implementations**: All major FHIR servers (HAPI, Firely, Microsoft) use full FHIRPath evaluation
3. **Legacy Tests**: Microsoft's `FhirPathPatchTests.cs` demonstrates `where()` function usage:
   ```csharp
   // Example from legacy tests (line 171-182)
   var patchRequest = new Parameters()
       .AddPatchParameter("replace",
           "Patient.address.where(use = 'home').city",
           value: new FhirString("Portland"));
   ```

### Current Implementation Gap Analysis

| Aspect | Current Implementation | FHIR Spec Requirement | Status |
|--------|----------------------|----------------------|--------|
| **Simple Paths** | ✅ `Patient.name` | ✅ REQUIRED | Complete |
| **Array Indexing** | ✅ `Patient.name[0]` | ✅ REQUIRED | Complete |
| **where() Function** | ❌ Not supported | ✅ SHOULD support | **Gap** |
| **first() Function** | ❌ Not supported | ✅ SHOULD support | **Gap** |
| **exists() Function** | ❌ Not supported | ⚠️ MAY support | Gap |
| **Comparison Operators** | ❌ Not supported | ✅ SHOULD support | **Gap** |
| **Path Resolution** | Custom string parsing (Navigate method) | FHIRPath evaluator | **Gap** |

**Current Navigation Logic** (FhirPatchEngine.cs:321-357):
```csharp
private JsonNode? Navigate(JsonNode? current, string path)
{
    var parts = path.Split('.'); // Simple string split - NOT FHIRPath
    foreach (var part in parts)
    {
        if (part.Contains('['))
        {
            // Basic array indexing only
        }
        else
        {
            current = current?[part];
        }
    }
    return current;
}
```

**Problem**: This approach only handles:
- Simple dot notation: `Patient.name`
- Basic array indexing: `Patient.name[0]`
- Does NOT handle: `Patient.name.where(use='official').family`

### FHIRPath Integration Architecture

**Existing Infrastructure** (Already Available):
- ✅ `Ignixa.FhirPath.Evaluation.FhirPathEvaluator` - Full FHIRPath evaluator (1000+ lines)
- ✅ `Ignixa.FhirPath.Parsing.FhirPathParser` - Expression parser
- ✅ `Ignixa.SourceNodeSerialization.ElementModel.ITypedElement` - FHIR-aware navigation
- ✅ `JsonNodeSourceNode` - Implements ISourceNode for JsonNode resources

**Integration Points**:

1. **Replace Custom Navigation** with FHIRPath Evaluation:
   ```csharp
   // BEFORE (Custom - FhirPatchEngine.cs:321)
   private JsonNode? Navigate(JsonNode? current, string path)
   {
       var parts = path.Split('.'); // String parsing
       // ...
   }

   // AFTER (FHIRPath-aware)
   private async Task<IEnumerable<ITypedElement>> EvaluatePath(
       ResourceJsonNode resource, string fhirPathExpression)
   {
       var parser = new FhirPathParser();
       var expression = parser.Parse(fhirPathExpression);

       var typedElement = resource.ToTypedElement(); // Convert to ITypedElement
       return _fhirPathEvaluator.Evaluate(typedElement, expression);
   }
   ```

2. **Strategy Pattern for Operations**:
   ```csharp
   // Create IOperationExecutor interface
   public interface IOperationExecutor
   {
       FhirPatchOperationType OperationType { get; }
       Task<ResourceJsonNode> ExecuteAsync(
           ResourceJsonNode resource,
           FhirPatchOperation operation,
           CancellationToken cancellationToken);
   }

   // Implement 5 executors: Add, Insert, Delete, Replace, Move
   public class ReplaceOperationExecutor : IOperationExecutor
   {
       private readonly FhirPathEvaluator _evaluator;

       public async Task<ResourceJsonNode> ExecuteAsync(...)
       {
           // 1. Evaluate FHIRPath expression
           var matches = await _evaluator.EvaluateAsync(resource, operation.Path);

           // 2. Validate single match (for replace)
           if (matches.Count() != 1)
               throw new FhirPatchException("Replace requires exactly one match");

           // 3. Replace value using JsonNode manipulation
           var target = matches.Single();
           target.Replace(operation.Value); // In-place mutation

           return resource;
       }
   }
   ```

3. **Converter: ResourceJsonNode ↔ ITypedElement**:
   ```csharp
   // New helper class needed
   public static class ResourceJsonNodeExtensions
   {
       public static ITypedElement ToTypedElement(this ResourceJsonNode resource)
       {
           var sourceNode = JsonNodeSourceNode.Create(resource.MutableNode);
           return sourceNode.ToTypedElement(provider);
       }

       public static ResourceJsonNode FromTypedElement(ITypedElement element)
       {
           // Convert back after FHIRPath evaluation
       }
   }
   ```

### Implementation Phases (Revised)

**Phase 1: Strategy Pattern Refactor** (16 hours)
1. Create `IOperationExecutor` interface
2. Extract operation logic into 5 executors:
   - `AddOperationExecutor`
   - `InsertOperationExecutor`
   - `DeleteOperationExecutor`
   - `ReplaceOperationExecutor`
   - `MoveOperationExecutor`
3. Refactor `FhirPatchEngine` to delegate to executors
4. Keep custom navigation temporarily (no behavior change)

**Phase 2: FHIRPath Integration** (16 hours)
1. Add FhirPathEvaluator injection to executors
2. Replace `Navigate()` calls with `_evaluator.Evaluate()`
3. Create `ResourceJsonNode.ToTypedElement()` converter
4. Update executors to use FHIRPath evaluation
5. Handle multi-match scenarios (delete: allow, replace: error)
6. Add expression validation (syntax errors)

**Phase 3: Validation & Testing** (16 hours)
1. Implement `FhirPatchValidator` (Parameters validation)
2. Implement `ImmutablePropertyValidator` (FHIRPath-aware)
3. Add OperationOutcome generation
4. Port 30+ tests from legacy `FhirPathPatchTests.cs`
5. Add unit tests for executors (50+ tests)
6. Add integration tests (20+ tests)

**Total Estimated Effort**: 48 hours (1 week)

### Key Design Decisions

**Decision 1: Keep Mutable JsonObject Architecture**
- ✅ Existing `ResourceJsonNode.MutableNode` provides in-place mutation
- ✅ FHIRPath evaluation returns `ITypedElement`, we navigate to JsonNode for mutation
- ✅ Avoids serialization roundtrips

**Decision 2: Strategy Pattern Over Monolithic Engine**
- ✅ Each executor handles one operation type (Single Responsibility)
- ✅ Easier to test (5 focused executors vs 1 large engine)
- ✅ Easier to extend (add new operation types)
- ✅ Matches ADR-2520 original design (lines 453-459)

**Decision 3: FHIRPath Evaluator Integration (NOT Custom Parsing)**
- ✅ Meets FHIR spec requirement (FHIRPath is MANDATORY)
- ✅ Reuses existing `Ignixa.FhirPath` infrastructure (no new dependencies)
- ✅ Supports complex expressions (`where()`, `first()`, `exists()`)
- ✅ All reference implementations use FHIRPath (HAPI, Firely, Microsoft)

**Decision 4: Multi-Match Behavior**
- ✅ **delete**: Allow multiple matches (delete all matched elements)
- ✅ **replace**: Require single match (error if multiple)
- ✅ **add/insert**: Path must resolve to collection
- ✅ **move**: Source and destination must each match exactly one element

### Migration Path

**No Breaking Changes**:
- Existing Parameters parsing remains compatible
- Existing simple paths (`Patient.name[0]`) continue to work
- New FHIRPath expressions work seamlessly (`Patient.name.where(use='official')`)
- Backward compatible with any partial PATCH implementation in use

**Validation Strategy**:
1. Port all 30+ legacy tests from `FhirPathPatchTests.cs`
2. Add new tests for `where()`, `first()`, `exists()` functions
3. Verify simple paths still work (regression testing)
4. Performance benchmark (<100ms overhead for FHIRPath evaluation)

## Decision

### Primary Decision: Implement FHIRPath Patch

**Implementation Approach**:
1. **HTTP Method**: `PATCH` (per FHIR R4 spec)
2. **Content-Type**: `application/fhir+json` (Parameters resource)
3. **Routing**: Minimal API pattern with multi-tenant support
4. **Architecture**: Medino command/handler pattern
5. **Operations**: Add, Insert, Delete, Replace, Move (5 operations)
6. **Validation**: Structural validation (always) + Profile validation (opt-in)
7. **Atomicity**: All-or-nothing (transaction support)
8. **Version Control**: ETag support with If-Match header

### Secondary Decision: JSON Patch as Optional Enhancement

**Defer to Future Phase** (not Week 91-94):
- JSON Patch support can be added later if customers request it
- Requires minimal changes (add `application/json-patch+json` content-type handler)
- Lower priority than FHIRPath Patch

### Architecture Decisions

**Minimal API Pattern** (NOT Controllers):
```csharp
// PATCH routes in PatchEndpoints.cs
endpoints.MapMethods("/tenant/{tenantId:int}/{resourceType}/{id}",
    new[] { "PATCH" }, HandlePatchResource);
endpoints.MapMethods("/{resourceType}/{id}",
    new[] { "PATCH" }, HandlePatchResourceAgnostic);
```

**Medino Handler Pattern**:
```csharp
public record PatchResourceCommand(
    int TenantId,
    string ResourceType,
    string ResourceId,
    string PatchDocument) : IRequest<ResourceWrapper?>;

public class PatchResourceHandler : IRequestHandler<PatchResourceCommand, ResourceWrapper?>
{
    public async Task<ResourceWrapper?> HandleAsync(
        PatchResourceCommand request,
        CancellationToken cancellationToken) { /* ... */ }
}
```

**Multi-Tenant Routing**:
- Tenant-explicit: `PATCH /tenant/1/Patient/123` (always available)
- Tenant-agnostic: `PATCH /Patient/123` (single-tenant auto-detect only)
- Multi-tenant mode: Agnostic routes return 400 Bad Request

**Layer Separation**:
- ✅ Domain: ResourceKey, ResourceWrapper, IFhirRepository
- ✅ Application: PatchResourceCommand, PatchResourceHandler, FhirPatchOperationExecutor
- ✅ DataLayer: IFhirRepositoryFactory (existing)
- ✅ API: PatchEndpoints.cs (Minimal API)
- ❌ NO Hl7.Fhir.* packages in Application/DataLayer (use Ignixa.*)

## FHIR Patch Specification Summary

### HTTP Method and Headers

**Request**:
```http
PATCH /Patient/123 HTTP/1.1
Host: fhir.example.com
Content-Type: application/fhir+json
If-Match: W/"2"
Accept: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [ /* operations */ ]
}
```

**Response (Success)**:
```http
HTTP/1.1 200 OK
Content-Type: application/fhir+json
ETag: W/"3"
Last-Modified: Thu, 17 Oct 2025 14:30:00 GMT

{
  "resourceType": "Patient",
  "id": "123",
  "meta": {
    "versionId": "3",
    "lastUpdated": "2025-10-17T14:30:00Z"
  },
  /* ... updated resource ... */
}
```

**Response (Error)**:
```http
HTTP/1.1 400 Bad Request
Content-Type: application/fhir+json

{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "invalid",
    "diagnostics": "FHIRPath expression 'Patient.name[99].family' did not match any elements"
  }]
}
```

### Parameters Resource Format

**Structure**:
```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "operation",
      "part": [
        { "name": "type", "valueCode": "replace" },
        { "name": "path", "valueString": "Patient.name[0].family" },
        { "name": "value", "valueString": "NewLastName" }
      ]
    }
  ]
}
```

**Parameter Parts**:
- `type` (required): Operation type - `add`, `insert`, `delete`, `replace`, `move`
- `path` (required): FHIRPath expression to target element
- `value` (conditional): New value (required for add/insert/replace, omit for delete)
- `index` (conditional): Position for insert operation (0-based)
- `source` (conditional): Source path for move operation
- `destination` (conditional): Destination path for move operation

### Operation Types

#### 1. Add Operation

**Purpose**: Add a new element to a collection (0..* cardinality).

**Parameters**:
- `type`: `"add"`
- `path`: FHIRPath expression to collection (e.g., `Patient.name`)
- `value`: Element to add

**Example**: Add a new phone number to Patient
```json
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "add" },
      { "name": "path", "valueString": "Patient.telecom" },
      {
        "name": "value",
        "valueContactPoint": {
          "system": "phone",
          "value": "+1-555-0199",
          "use": "mobile"
        }
      }
    ]
  }]
}
```

**Constraints**:
- Target path MUST point to a collection (0..* or 1..*)
- Cannot add to 0..1 elements (use `replace` instead)
- Value type MUST match element type

#### 2. Insert Operation

**Purpose**: Insert an element at a specific position in a collection.

**Parameters**:
- `type`: `"insert"`
- `path`: FHIRPath expression to collection
- `value`: Element to insert
- `index`: 0-based position (0 = prepend, length = append)

**Example**: Insert a name at the beginning
```json
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "insert" },
      { "name": "path", "valueString": "Patient.name" },
      { "name": "index", "valueInteger": 0 },
      {
        "name": "value",
        "valueHumanName": {
          "use": "official",
          "family": "Smith",
          "given": ["John"]
        }
      }
    ]
  }]
}
```

**Constraints**:
- Target path MUST point to a collection
- Index MUST be >= 0 and <= collection.length
- Value type MUST match element type

#### 3. Delete Operation

**Purpose**: Remove an element from the resource.

**Parameters**:
- `type`: `"delete"`
- `path`: FHIRPath expression to element(s) to remove

**Example**: Delete the second phone number
```json
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "delete" },
      { "name": "path", "valueString": "Patient.telecom[1]" }
    ]
  }]
}
```

**Constraints**:
- Path MUST match at least one element
- Cannot delete immutable properties (id, meta.versionId, meta.lastUpdated)
- Deleting required elements (1..1, 1..*) will fail validation

#### 4. Replace Operation

**Purpose**: Replace the value of an existing element.

**Parameters**:
- `type`: `"replace"`
- `path`: FHIRPath expression to element to replace
- `value`: New value

**Example**: Update family name
```json
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "replace" },
      { "name": "path", "valueString": "Patient.name[0].family" },
      { "name": "value", "valueString": "Johnson" }
    ]
  }]
}
```

**Constraints**:
- Path MUST match exactly one element
- Cannot replace immutable properties
- Value type MUST match element type
- Path cannot be resource root (use PUT instead)

#### 5. Move Operation

**Purpose**: Move an element from one position to another within a collection.

**Parameters**:
- `type`: `"move"`
- `source`: FHIRPath expression to element to move
- `destination`: FHIRPath expression to target position

**Example**: Move second name to first position
```json
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "move" },
      { "name": "source", "valueString": "Patient.name[1]" },
      { "name": "destination", "valueString": "Patient.name[0]" }
    ]
  }]
}
```

**Constraints**:
- Source MUST match exactly one element
- Source and destination MUST be same element type
- Cannot move across different collections

### Immutable Properties

**Protected Fields** (cannot be modified via PATCH):
- `Resource.id` - Logical ID (use PUT to change)
- `Resource.meta.versionId` - Version (server-managed)
- `Resource.meta.lastUpdated` - Timestamp (server-managed)

**Validation**:
- Pre-patch state capture
- Post-patch state comparison
- Return 400 Bad Request if immutables changed

### Multi-Operation Atomicity

**Behavior**: All operations in a single PATCH request are atomic.

**Example**: Update name AND phone in one request
```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "operation",
      "part": [
        { "name": "type", "valueCode": "replace" },
        { "name": "path", "valueString": "Patient.name[0].family" },
        { "name": "value", "valueString": "Johnson" }
      ]
    },
    {
      "name": "operation",
      "part": [
        { "name": "type", "valueCode": "replace" },
        { "name": "path", "valueString": "Patient.telecom[0].value" },
        { "name": "value", "valueString": "+1-555-9999" }
      ]
    }
  ]
}
```

**Atomicity Guarantee**:
- If ANY operation fails, NONE are applied
- Resource returned to pre-patch state
- OperationOutcome indicates which operation failed

## Architecture Design

### File Structure

**Application Layer** (`src/Ignixa.Application/Features/Patch/`):
```
Patch/
├── PatchResourceCommand.cs          # Medino command (IRequest<ResourceWrapper?>)
├── PatchResourceHandler.cs          # Medino handler (orchestrates patching)
├── FhirPatchOperation.cs            # Model for patch operation
├── FhirPatchParametersParser.cs     # Parse Parameters → FhirPatchOperation[]
├── FhirPatchOperationExecutor.cs    # Executes operations on ITypedElement
├── Executors/
│   ├── IOperationExecutor.cs        # Interface for operation executors
│   ├── AddOperationExecutor.cs      # Add operation logic
│   ├── InsertOperationExecutor.cs   # Insert operation logic
│   ├── DeleteOperationExecutor.cs   # Delete operation logic
│   ├── ReplaceOperationExecutor.cs  # Replace operation logic
│   └── MoveOperationExecutor.cs     # Move operation logic
└── Validation/
    ├── FhirPatchValidator.cs        # Validate Parameters resource
    └── ImmutablePropertyValidator.cs # Protect id, meta.versionId, etc.
```

**API Layer** (`src/Ignixa.Api/Infrastructure/`):
```
Infrastructure/
└── PatchEndpoints.cs                # Minimal API PATCH routes
```

**Tests** (`tests/Ignixa.Application.Tests/Features/Patch/`):
```
Patch/
├── FhirPatchOperationTests.cs       # Unit tests for parser
├── PatchOperationExecutorTests.cs   # Unit tests for executors
└── PatchResourceHandlerTests.cs     # Unit tests for handler

tests/Ignixa.Api.Tests/Infrastructure/
└── PatchEndpointsTests.cs           # Integration tests for endpoints

tests/Ignixa.IntegrationTests/
└── PatchMultiTenantTests.cs         # E2E multi-tenant tests
```

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    HTTP PATCH Request                            │
│  PATCH /Patient/123 (Parameters resource in body)               │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│               PatchEndpoints.cs (Minimal API)                    │
│  - Extract tenantId from route or auto-detect                   │
│  - Read Parameters resource from body                           │
│  - Create PatchResourceCommand                                  │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                   IMediator (Medino)                             │
│  SendAsync(PatchResourceCommand, ct)                            │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│             PatchResourceHandler (Application)                   │
│  1. Get repository via IFhirRepositoryFactory                   │
│  2. Fetch existing resource (404 if not found)                  │
│  3. Parse Parameters → FhirPatchOperation[]                     │
│  4. Execute operations via FhirPatchOperationExecutor           │
│  5. Validate immutable properties unchanged                     │
│  6. Update resource via repository                              │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│          FhirPatchOperationExecutor (Application)                │
│  For each operation:                                            │
│  1. Delegate to specific executor (Add/Insert/Delete/...)      │
│  2. Validate FHIRPath expression                               │
│  3. Execute on ITypedElement (Ignixa.SourceNodeSerialization)  │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│            Operation Executors (Strategy Pattern)                │
│  AddOperationExecutor      - Add to collection                  │
│  InsertOperationExecutor   - Insert at index                    │
│  DeleteOperationExecutor   - Remove element                     │
│  ReplaceOperationExecutor  - Replace value                      │
│  MoveOperationExecutor     - Move within collection             │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│     IFhirRepository.CreateOrUpdateAsync() (DataLayer)            │
│  - Write patched resource to storage                            │
│  - Increment versionId, update lastUpdated                      │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                  HTTP 200 OK (Patched Resource)                  │
│  ETag: W/"3", Content-Type: application/fhir+json               │
└─────────────────────────────────────────────────────────────────┘
```

### Multi-Tenant Routing Strategy

**Tenant-Explicit Routes** (Always Available):
```csharp
// src/Ignixa.Api/Infrastructure/PatchEndpoints.cs
endpoints.MapMethods("/tenant/{tenantId:int}/{resourceType}/{id}",
    new[] { "PATCH" }, HandlePatchResource)
    .WithName("PatchResource")
    .Accepts<object>("application/fhir+json")
    .Produces<object>(StatusCodes.Status200OK, "application/fhir+json");
```

**Tenant-Agnostic Routes** (Single-Tenant Auto-Detect):
```csharp
endpoints.MapMethods("/{resourceType}/{id}",
    new[] { "PATCH" },
    (HttpContext ctx, string resourceType, string id, IMediator mediator, CancellationToken ct) =>
        HandlePatchResource(ctx, ExtractTenantId(ctx), resourceType, id, mediator, ct));
```

**Behavior**:

| Tenant Count | Route | Behavior |
|--------------|-------|----------|
| 1 active | `PATCH /Patient/123` | ✅ Auto-detects tenant 1 |
| 1 active | `PATCH /tenant/1/Patient/123` | ✅ Explicit tenant |
| 2+ active | `PATCH /Patient/123` | ❌ 400 Bad Request (ambiguous) |
| 2+ active | `PATCH /tenant/1/Patient/123` | ✅ Explicit tenant required |

**Middleware**: `TenantResolutionMiddleware` extracts tenantId or auto-detects (same as existing CRUD endpoints).

### Integration with IFhirRepositoryFactory

**Pattern** (existing pattern, reused):
```csharp
public class PatchResourceHandler : IRequestHandler<PatchResourceCommand, ResourceWrapper?>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;

    public PatchResourceHandler(IFhirRepositoryFactory repositoryFactory)
    {
        _repositoryFactory = repositoryFactory;
    }

    public async Task<ResourceWrapper?> HandleAsync(
        PatchResourceCommand request,
        CancellationToken cancellationToken)
    {
        // Get tenant-specific repository
        var repository = _repositoryFactory.GetRepository(request.TenantId);

        // Fetch existing resource
        var key = new ResourceKey(request.ResourceType, request.ResourceId, null);
        var existing = await repository.GetAsync(key, cancellationToken);

        if (existing == null)
            return null;

        // Apply patch operations
        var patched = await _patchExecutor.ExecuteAsync(
            existing, request.Operations, cancellationToken);

        // Update resource (increments versionId, updates lastUpdated)
        return await repository.CreateOrUpdateAsync(patched, cancellationToken);
    }
}
```

### FhirPatchOperation Model

**Model** (`src/Ignixa.Application/Features/Patch/FhirPatchOperation.cs`):
```csharp
namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Represents a single FHIR Patch operation from a Parameters resource.
/// </summary>
public record FhirPatchOperation
{
    /// <summary>
    /// Operation type: add, insert, delete, replace, move
    /// </summary>
    public required FhirPatchOperationType Type { get; init; }

    /// <summary>
    /// FHIRPath expression to target element (required for all operations)
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Value to set (required for add, insert, replace; omit for delete, move)
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Index for insert operation (0-based)
    /// </summary>
    public int? Index { get; init; }

    /// <summary>
    /// Source path for move operation
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Destination path for move operation
    /// </summary>
    public string? Destination { get; init; }
}

public enum FhirPatchOperationType
{
    Add,
    Insert,
    Delete,
    Replace,
    Move
}
```

### Operation Executor Strategy Pattern

**Interface** (`src/Ignixa.Application/Features/Patch/Executors/IOperationExecutor.cs`):
```csharp
namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes a specific patch operation type on a FHIR resource.
/// </summary>
public interface IOperationExecutor
{
    /// <summary>
    /// Operation type this executor handles
    /// </summary>
    FhirPatchOperationType OperationType { get; }

    /// <summary>
    /// Execute the operation on the resource
    /// </summary>
    Task<ITypedElement> ExecuteAsync(
        ITypedElement resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken);
}
```

**Executor Factory**:
```csharp
public class FhirPatchOperationExecutor
{
    private readonly Dictionary<FhirPatchOperationType, IOperationExecutor> _executors;

    public FhirPatchOperationExecutor(IEnumerable<IOperationExecutor> executors)
    {
        _executors = executors.ToDictionary(e => e.OperationType);
    }

    public async Task<ITypedElement> ExecuteAsync(
        ITypedElement resource,
        FhirPatchOperation[] operations,
        CancellationToken cancellationToken)
    {
        var current = resource;
        foreach (var operation in operations)
        {
            if (!_executors.TryGetValue(operation.Type, out var executor))
                throw new InvalidOperationException($"Unknown operation type: {operation.Type}");

            current = await executor.ExecuteAsync(current, operation, cancellationToken);
        }
        return current;
    }
}
```

## Implementation Phases

### Week 91: Core Parser & Models (24 hours)

**Goal**: Parse Parameters resource into FhirPatchOperation objects.

**Tasks**:
1. **FhirPatchOperation.cs** (2 hours)
   - Create record with Type, Path, Value, Index, Source, Destination
   - FhirPatchOperationType enum (Add, Insert, Delete, Replace, Move)

2. **FhirPatchParametersParser.cs** (6 hours)
   - Parse Parameters resource
   - Extract operation parameters
   - Validate required fields (type, path)
   - Map valueCode → FhirPatchOperationType
   - Extract value (valueString, valueInteger, valueHumanName, etc.)
   - Handle multiple operations

3. **PatchResourceCommand.cs** (2 hours)
   - Create Medino command: `IRequest<ResourceWrapper?>`
   - Properties: TenantId, ResourceType, ResourceId, PatchDocument (JSON string)

4. **Unit Tests** (14 hours)
   - `FhirPatchOperationTests.cs` - 15 tests
     - Parse single operation (add, insert, delete, replace, move)
     - Parse multiple operations
     - Validate required fields
     - Validate type mapping
     - Extract complex values (HumanName, ContactPoint, etc.)
     - Error handling (missing fields, invalid type, etc.)

**Deliverables**:
- ✅ FhirPatchOperation model
- ✅ Parser (Parameters → FhirPatchOperation[])
- ✅ PatchResourceCommand
- ✅ 15 unit tests (100% parser coverage)

### Week 92: Operation Executors (24 hours)

**Goal**: Implement logic for each operation type using ITypedElement manipulation.

**Tasks**:
1. **IOperationExecutor.cs** (1 hour)
   - Interface with OperationType property
   - ExecuteAsync method

2. **AddOperationExecutor.cs** (4 hours)
   - Evaluate FHIRPath expression
   - Validate target is collection (0..* or 1..*)
   - Add value to collection
   - Validate type match

3. **InsertOperationExecutor.cs** (4 hours)
   - Evaluate FHIRPath expression
   - Validate index (0 <= index <= length)
   - Insert value at position
   - Validate type match

4. **DeleteOperationExecutor.cs** (3 hours)
   - Evaluate FHIRPath expression
   - Remove matched element(s)
   - Validate not deleting immutable properties

5. **ReplaceOperationExecutor.cs** (4 hours)
   - Evaluate FHIRPath expression
   - Validate exactly one match
   - Replace value
   - Validate type match

6. **MoveOperationExecutor.cs** (4 hours)
   - Evaluate source and destination FHIRPath expressions
   - Validate same element type
   - Move element within collection

7. **Unit Tests** (4 hours)
   - `PatchOperationExecutorTests.cs` - 25 tests
     - Each operation type: success case
     - Each operation type: type mismatch error
     - Each operation type: path not found error
     - Add: to non-collection error
     - Insert: index out of range error
     - Delete: immutable property error
     - Replace: multiple matches error
     - Move: type mismatch error

**Deliverables**:
- ✅ IOperationExecutor interface
- ✅ 5 operation executors (Add, Insert, Delete, Replace, Move)
- ✅ 25 unit tests (80%+ executor coverage)

### Week 93: Minimal API Endpoint & Handler (24 hours)

**Goal**: Create PATCH endpoint and Medino handler orchestration.

**Tasks**:
1. **PatchEndpoints.cs** (6 hours)
   - Create `MapPatchEndpoints()` extension method
   - Define tenant-explicit route: `PATCH /tenant/{tenantId}/{resourceType}/{id}`
   - Define tenant-agnostic route: `PATCH /{resourceType}/{id}`
   - Read Parameters resource from body
   - Create PatchResourceCommand
   - Send via IMediator
   - Return 200 OK (patched resource) or 404 Not Found

2. **PatchResourceHandler.cs** (8 hours)
   - Get repository via IFhirRepositoryFactory
   - Fetch existing resource (404 if not found)
   - Parse Parameters → FhirPatchOperation[]
   - Execute operations via FhirPatchOperationExecutor
   - Validate immutable properties unchanged (id, meta.versionId, meta.lastUpdated)
   - Update resource via repository.CreateOrUpdateAsync()
   - Return patched ResourceWrapper

3. **FhirPatchValidator.cs** (4 hours)
   - Validate Parameters resource structure
   - Validate operation parts (type, path required)
   - Validate operation type is valid enum value
   - Return OperationOutcome for validation errors

4. **ImmutablePropertyValidator.cs** (2 hours)
   - Compare pre-patch and post-patch resource
   - Validate id, meta.versionId, meta.lastUpdated unchanged
   - Return OperationOutcome if immutables changed

5. **Register in Program.cs** (1 hour)
   - Add `app.MapPatchEndpoints()`
   - Register handlers in Autofac
   - Register executors in Autofac

6. **Integration Tests** (3 hours)
   - `PatchEndpointsTests.cs` - 20 tests
     - PATCH success (all 5 operation types)
     - PATCH 404 (resource not found)
     - PATCH 400 (invalid Parameters)
     - PATCH 400 (immutable property changed)
     - PATCH 400 (FHIRPath evaluation error)
     - Multi-operation atomicity (all-or-nothing)
     - ETag support (If-Match header)

**Deliverables**:
- ✅ PatchEndpoints.cs (Minimal API)
- ✅ PatchResourceHandler.cs (Medino)
- ✅ FhirPatchValidator.cs
- ✅ ImmutablePropertyValidator.cs
- ✅ Program.cs registration
- ✅ 20 integration tests

### Week 94: Multi-Tenant Support & Testing (16 hours)

**Goal**: Ensure multi-tenant routing works, add E2E tests.

**Tasks**:
1. **Multi-Tenant Routing Verification** (2 hours)
   - Test tenant-explicit routes work
   - Test tenant-agnostic auto-detect works (single-tenant)
   - Test tenant-agnostic returns 400 (multi-tenant mode)
   - Test TenantResolutionMiddleware integration

2. **E2E Tests** (10 hours)
   - `PatchMultiTenantTests.cs` - 30+ tests (port from legacy FhirPathPatchTests.cs)
     - Add operation: Add name, telecom, address
     - Insert operation: Insert at beginning, middle, end
     - Delete operation: Delete by index, delete by path
     - Replace operation: Replace primitive, replace complex
     - Move operation: Move within collection
     - Multi-operation: Complex scenarios
     - Error cases: Path not found, type mismatch, immutable property
     - Multi-tenant: Different tenants, isolation verification

3. **Performance Testing** (2 hours)
   - Benchmark PATCH vs PUT (measure payload size reduction)
   - Benchmark operation execution time (<100ms for typical patch)
   - Memory profiling (ensure no ITypedElement leaks)

4. **Build Verification** (1 hour)
   - Run `dotnet build All.sln` (0 warnings, 0 errors)
   - Run `dotnet test All.sln` (all tests pass)

5. **Documentation** (1 hour)
   - Update CLAUDE.md with PATCH endpoint details
   - Update capability statement (BaseCapabilities.json)

**Deliverables**:
- ✅ Multi-tenant routing verified
- ✅ 30+ E2E tests (ported from legacy)
- ✅ Performance benchmarks
- ✅ Build verification
- ✅ Documentation updates

## Code Examples

### Example 1: Parameters Resource (Replace Operation)

**Request**:
```json
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "replace" },
      { "name": "path", "valueString": "Patient.name[0].family" },
      { "name": "value", "valueString": "Johnson" }
    ]
  }]
}
```

**Before**:
```json
{
  "resourceType": "Patient",
  "id": "123",
  "meta": { "versionId": "2" },
  "name": [{
    "use": "official",
    "family": "Smith",
    "given": ["John"]
  }]
}
```

**After**:
```json
{
  "resourceType": "Patient",
  "id": "123",
  "meta": { "versionId": "3" },
  "name": [{
    "use": "official",
    "family": "Johnson",
    "given": ["John"]
  }]
}
```

### Example 2: PatchEndpoints.cs (Minimal API)

```csharp
using Ignixa.Application.Features.Patch;
using Ignixa.Domain.Abstractions;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Minimal API endpoints for FHIR PATCH operations.
/// </summary>
public static class PatchEndpoints
{
    public static IEndpointRouteBuilder MapPatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Tenant-explicit route
        endpoints.MapMethods(
            "/tenant/{tenantId:int}/{resourceType}/{id}",
            new[] { "PATCH" },
            HandlePatchResource)
            .WithName("PatchResource")
            .WithOpenApi()
            .Accepts<object>("application/fhir+json")
            .Produces<object>(StatusCodes.Status200OK, "application/fhir+json")
            .Produces<object>(StatusCodes.Status400BadRequest, "application/fhir+json")
            .Produces(StatusCodes.Status404NotFound);

        // Tenant-agnostic route (single-tenant auto-detect)
        endpoints.MapMethods(
            "/{resourceType}/{id}",
            new[] { "PATCH" },
            (HttpContext context, string resourceType, string id,
             [FromServices] IMediator mediator,
             CancellationToken cancellationToken) =>
                HandlePatchResource(
                    context,
                    ExtractTenantId(context),
                    resourceType,
                    id,
                    mediator,
                    cancellationToken))
            .WithName("PatchResourceAgnostic")
            .WithOpenApi()
            .Accepts<object>("application/fhir+json")
            .Produces<object>(StatusCodes.Status200OK, "application/fhir+json")
            .Produces<object>(StatusCodes.Status400BadRequest, "application/fhir+json")
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandlePatchResource(
        HttpContext context,
        int tenantId,
        string resourceType,
        string id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            // Read Parameters resource from body
            using var reader = new StreamReader(context.Request.Body);
            var patchDocument = await reader.ReadToEndAsync(cancellationToken);

            // Create command
            var command = new PatchResourceCommand(
                tenantId,
                resourceType,
                id,
                patchDocument);

            // Execute via Medino
            var result = await mediator.SendAsync(command, cancellationToken);

            if (result == null)
            {
                return Results.NotFound();
            }

            // Return patched resource
            context.Response.Headers["ETag"] = $"W/\"{result.Version}\"";
            context.Response.Headers["Last-Modified"] = result.LastUpdated.ToString("R");

            return Results.Ok(result);
        }
        catch (FhirPatchValidationException ex)
        {
            // Return OperationOutcome
            return Results.BadRequest(ex.OperationOutcome);
        }
    }

    private static int ExtractTenantId(HttpContext context)
    {
        // Delegate to TenantResolutionMiddleware
        return context.GetTenantId();
    }
}
```

### Example 3: PatchResourceHandler.cs (Medino Handler)

```csharp
using Ignixa.Application.Features.Patch.Validation;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Handles PATCH operations on FHIR resources.
/// </summary>
public class PatchResourceHandler : IRequestHandler<PatchResourceCommand, ResourceWrapper?>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly FhirPatchParametersParser _parametersParser;
    private readonly FhirPatchOperationExecutor _operationExecutor;
    private readonly FhirPatchValidator _validator;
    private readonly ImmutablePropertyValidator _immutableValidator;
    private readonly ILogger<PatchResourceHandler> _logger;

    public PatchResourceHandler(
        IFhirRepositoryFactory repositoryFactory,
        FhirPatchParametersParser parametersParser,
        FhirPatchOperationExecutor operationExecutor,
        FhirPatchValidator validator,
        ImmutablePropertyValidator immutableValidator,
        ILogger<PatchResourceHandler> logger)
    {
        _repositoryFactory = repositoryFactory;
        _parametersParser = parametersParser;
        _operationExecutor = operationExecutor;
        _validator = validator;
        _immutableValidator = immutableValidator;
        _logger = logger;
    }

    public async Task<ResourceWrapper?> HandleAsync(
        PatchResourceCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Get tenant-specific repository
        var repository = _repositoryFactory.GetRepository(request.TenantId);

        // 2. Fetch existing resource
        var key = new ResourceKey(request.ResourceType, request.ResourceId, null);
        var existing = await repository.GetAsync(key, cancellationToken);

        if (existing == null)
        {
            _logger.LogWarning(
                "PATCH failed: Resource {ResourceType}/{ResourceId} not found in tenant {TenantId}",
                request.ResourceType,
                request.ResourceId,
                request.TenantId);
            return null;
        }

        // 3. Parse Parameters resource → FhirPatchOperation[]
        var operations = _parametersParser.Parse(request.PatchDocument);

        // 4. Validate Parameters structure
        _validator.Validate(operations);

        // 5. Execute patch operations
        var patched = await _operationExecutor.ExecuteAsync(
            existing.Resource, // ITypedElement
            operations,
            cancellationToken);

        // 6. Validate immutable properties unchanged
        _immutableValidator.Validate(existing.Resource, patched);

        // 7. Update resource in repository
        var wrapper = new ResourceWrapper(
            new ResourceKey(request.ResourceType, request.ResourceId, null),
            patched,
            existing.RawJson, // Will be regenerated by repository
            lastUpdated: DateTimeOffset.UtcNow);

        var updated = await repository.CreateOrUpdateAsync(wrapper, cancellationToken);

        _logger.LogInformation(
            "PATCH succeeded: {ResourceType}/{ResourceId} updated to version {Version} in tenant {TenantId}",
            request.ResourceType,
            request.ResourceId,
            updated.Version,
            request.TenantId);

        return updated;
    }
}
```

### Example 4: FhirPatchOperation Model

```csharp
namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Represents a single FHIR Patch operation from a Parameters resource.
/// </summary>
public record FhirPatchOperation
{
    /// <summary>
    /// Operation type: add, insert, delete, replace, move
    /// </summary>
    public required FhirPatchOperationType Type { get; init; }

    /// <summary>
    /// FHIRPath expression to target element (required for all operations)
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Value to set (required for add, insert, replace; omit for delete, move)
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Index for insert operation (0-based)
    /// </summary>
    public int? Index { get; init; }

    /// <summary>
    /// Source path for move operation
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Destination path for move operation
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>
    /// Parse a Parameters resource into an array of operations.
    /// </summary>
    public static FhirPatchOperation[] ParseFromParameters(string parametersJson)
    {
        // Delegate to FhirPatchParametersParser
        var parser = new FhirPatchParametersParser();
        return parser.Parse(parametersJson);
    }
}

public enum FhirPatchOperationType
{
    Add,
    Insert,
    Delete,
    Replace,
    Move
}
```

### Example 5: Operation Executor Interface

```csharp
using Ignixa.SourceNodeSerialization.ElementModel;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes a specific patch operation type on a FHIR resource.
/// </summary>
public interface IOperationExecutor
{
    /// <summary>
    /// Operation type this executor handles
    /// </summary>
    FhirPatchOperationType OperationType { get; }

    /// <summary>
    /// Execute the operation on the resource
    /// </summary>
    /// <param name="resource">Resource to patch (ITypedElement)</param>
    /// <param name="operation">Operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Patched resource (ITypedElement)</returns>
    Task<ITypedElement> ExecuteAsync(
        ITypedElement resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken);
}
```

### Example 6: ReplaceOperationExecutor

```csharp
using Ignixa.FhirPath.Evaluation;
using Ignixa.SourceNodeSerialization.ElementModel;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes replace operations on FHIR resources.
/// </summary>
public class ReplaceOperationExecutor : IOperationExecutor
{
    private readonly IFhirPathEvaluator _fhirPathEvaluator;
    private readonly ILogger<ReplaceOperationExecutor> _logger;

    public FhirPatchOperationType OperationType => FhirPatchOperationType.Replace;

    public ReplaceOperationExecutor(
        IFhirPathEvaluator fhirPathEvaluator,
        ILogger<ReplaceOperationExecutor> logger)
    {
        _fhirPathEvaluator = fhirPathEvaluator;
        _logger = logger;
    }

    public async Task<ITypedElement> ExecuteAsync(
        ITypedElement resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        // 1. Evaluate FHIRPath expression
        var matches = await _fhirPathEvaluator.EvaluateAsync(
            resource,
            operation.Path,
            cancellationToken);

        // 2. Validate exactly one match
        if (matches.Count == 0)
        {
            throw new FhirPatchException(
                $"FHIRPath expression '{operation.Path}' did not match any elements");
        }

        if (matches.Count > 1)
        {
            throw new FhirPatchException(
                $"FHIRPath expression '{operation.Path}' matched {matches.Count} elements (expected 1)");
        }

        var target = matches[0];

        // 3. Validate value type matches target type
        if (!IsTypeMatch(target, operation.Value))
        {
            throw new FhirPatchException(
                $"Value type mismatch: expected {target.InstanceType}, got {operation.Value?.GetType().Name}");
        }

        // 4. Replace value
        var replaced = target.Replace(operation.Value);

        _logger.LogDebug(
            "Replace operation: {Path} = {Value}",
            operation.Path,
            operation.Value);

        return replaced;
    }

    private static bool IsTypeMatch(ITypedElement target, object? value)
    {
        // Type checking logic (string → string, integer → integer, etc.)
        // Simplified for example
        return true;
    }
}
```

### Example 7: Error Handling (OperationOutcome)

**Invalid Parameters**:
```json
// Request: Missing 'type' field
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "path", "valueString": "Patient.name[0].family" }
    ]
  }]
}

// Response: 400 Bad Request
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "invalid",
    "diagnostics": "Parameter 'type' is required for patch operation"
  }]
}
```

**FHIRPath Evaluation Error**:
```json
// Request: Path not found
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "replace" },
      { "name": "path", "valueString": "Patient.name[99].family" },
      { "name": "value", "valueString": "Johnson" }
    ]
  }]
}

// Response: 400 Bad Request
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "invalid",
    "diagnostics": "FHIRPath expression 'Patient.name[99].family' did not match any elements"
  }]
}
```

**Immutable Property Changed**:
```json
// Request: Attempting to change id
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      { "name": "type", "valueCode": "replace" },
      { "name": "path", "valueString": "Patient.id" },
      { "name": "value", "valueString": "new-id" }
    ]
  }]
}

// Response: 400 Bad Request
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "business-rule",
    "diagnostics": "Cannot modify immutable property 'id' via PATCH (use PUT instead)"
  }]
}
```

### Example 8: Multi-Tenant Route Examples

**Tenant-Explicit (Multi-Tenant Mode)**:
```http
PATCH /tenant/1/Patient/mayo-123 HTTP/1.1
Host: fhir.example.com
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{ /* ... */ }]
}
```

**Tenant-Agnostic (Single-Tenant Auto-Detect)**:
```http
PATCH /Patient/123 HTTP/1.1
Host: fhir.example.com
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{ /* ... */ }]
}
```

## Validation Requirements

### Structural Validation (Always On)

**Tier 1 Validation** (<50ms overhead):
1. **Parameters Resource**:
   - ✅ Valid JSON
   - ✅ resourceType = "Parameters"
   - ✅ parameter[] array exists

2. **Operation Parts**:
   - ✅ Each parameter has name = "operation"
   - ✅ Each operation has part[] array
   - ✅ Required parts: `type`, `path`
   - ✅ Conditional parts: `value` (add/insert/replace), `index` (insert), `source`/`destination` (move)

3. **Operation Type**:
   - ✅ type is one of: add, insert, delete, replace, move
   - ✅ type maps to valid FhirPatchOperationType enum

4. **FHIRPath Expression**:
   - ✅ path is non-empty string
   - ✅ path is syntactically valid FHIRPath

5. **Value Type**:
   - ✅ value type matches target element type (string, integer, HumanName, etc.)
   - ✅ value conforms to element constraints

**Validation Response** (400 Bad Request):
```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "invalid",
    "diagnostics": "Validation error details"
  }]
}
```

### Profile Validation (Opt-In)

**Tier 2 Validation** (disabled by default, configurable):
1. **Schema Validation**:
   - Patched resource conforms to FHIR R4 schema
   - Required elements present (1..1, 1..*)
   - Cardinality constraints met

2. **Profile Validation**:
   - Patched resource conforms to declared profile(s)
   - Extensions valid
   - ValueSet bindings valid

3. **Reference Validation**:
   - Referenced resources exist (for Reference types)
   - Referential integrity maintained

**Configuration**:
```json
{
  "Validation": {
    "StructuralValidation": true,    // Always on
    "ProfileValidation": false,       // Opt-in (default: off)
    "ReferenceValidation": false      // Opt-in (default: off)
  }
}
```

### Immutable Property Protection

**Protected Fields**:
- `Resource.id` - Cannot be changed via PATCH (use PUT to change logical ID)
- `Resource.meta.versionId` - Server-managed (auto-incremented)
- `Resource.meta.lastUpdated` - Server-managed (auto-updated)

**Validation Logic**:
```csharp
public class ImmutablePropertyValidator
{
    public void Validate(ITypedElement before, ITypedElement after)
    {
        // Compare id
        var beforeId = before.Scalar("id")?.ToString();
        var afterId = after.Scalar("id")?.ToString();
        if (beforeId != afterId)
            throw new FhirPatchException("Cannot modify 'id' via PATCH");

        // Compare meta.versionId
        var beforeVersionId = before.Scalar("meta.versionId")?.ToString();
        var afterVersionId = after.Scalar("meta.versionId")?.ToString();
        if (beforeVersionId != afterVersionId)
            throw new FhirPatchException("Cannot modify 'meta.versionId' via PATCH");

        // Compare meta.lastUpdated
        var beforeLastUpdated = before.Scalar("meta.lastUpdated")?.ToString();
        var afterLastUpdated = after.Scalar("meta.lastUpdated")?.ToString();
        if (beforeLastUpdated != afterLastUpdated)
            throw new FhirPatchException("Cannot modify 'meta.lastUpdated' via PATCH");
    }
}
```

### Error Responses

**404 Not Found** (Resource doesn't exist):
```http
HTTP/1.1 404 Not Found
Content-Type: application/fhir+json

{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "not-found",
    "diagnostics": "Resource Patient/123 not found"
  }]
}
```

**400 Bad Request** (Validation failure):
```http
HTTP/1.1 400 Bad Request
Content-Type: application/fhir+json

{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "invalid",
    "diagnostics": "FHIRPath expression 'Patient.name[99].family' did not match any elements"
  }]
}
```

**412 Precondition Failed** (Version conflict):
```http
HTTP/1.1 412 Precondition Failed
Content-Type: application/fhir+json

{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "conflict",
    "diagnostics": "Resource version mismatch: expected W/\"2\", found W/\"3\""
  }]
}
```

## Testing Strategy

### Unit Tests (80% Coverage Minimum)

**FhirPatchOperationTests.cs** (15 tests):
- Parse single add operation
- Parse single insert operation
- Parse single delete operation
- Parse single replace operation
- Parse single move operation
- Parse multiple operations
- Validate required fields (type, path)
- Validate type mapping (valueCode → enum)
- Extract complex values (HumanName, ContactPoint, Address)
- Error: Missing type field
- Error: Missing path field
- Error: Invalid type value
- Error: Invalid Parameters structure
- Error: Empty parameter array
- Error: Malformed JSON

**PatchOperationExecutorTests.cs** (25 tests):
- **Add**: Add to collection (success)
- **Add**: Type mismatch error
- **Add**: Add to non-collection error
- **Add**: Path not found error
- **Insert**: Insert at beginning (success)
- **Insert**: Insert at end (success)
- **Insert**: Insert in middle (success)
- **Insert**: Index out of range error
- **Insert**: Type mismatch error
- **Delete**: Delete by index (success)
- **Delete**: Delete by path (success)
- **Delete**: Immutable property error
- **Delete**: Path not found error
- **Replace**: Replace primitive (success)
- **Replace**: Replace complex (success)
- **Replace**: Multiple matches error
- **Replace**: Type mismatch error
- **Replace**: Path not found error
- **Replace**: Immutable property error
- **Move**: Move within collection (success)
- **Move**: Type mismatch error
- **Move**: Source not found error
- **Move**: Destination not found error
- **Move**: Cross-collection error
- **Move**: Immutable property error

**PatchResourceHandlerTests.cs** (10 tests):
- Handler success (all operation types)
- Handler returns null (resource not found)
- Handler throws (validation error)
- Handler throws (FHIRPath evaluation error)
- Handler throws (immutable property error)
- Multi-operation atomicity (all-or-nothing)
- Version increment verification
- LastUpdated update verification
- Tenant isolation verification
- ETag support verification

### Integration Tests (20 Tests)

**PatchEndpointsTests.cs** (20 tests):
- PATCH 200 OK (add operation)
- PATCH 200 OK (insert operation)
- PATCH 200 OK (delete operation)
- PATCH 200 OK (replace operation)
- PATCH 200 OK (move operation)
- PATCH 200 OK (multi-operation)
- PATCH 404 Not Found (resource not found)
- PATCH 400 Bad Request (invalid Parameters)
- PATCH 400 Bad Request (missing type)
- PATCH 400 Bad Request (missing path)
- PATCH 400 Bad Request (invalid type)
- PATCH 400 Bad Request (FHIRPath evaluation error)
- PATCH 400 Bad Request (type mismatch)
- PATCH 400 Bad Request (immutable property changed)
- PATCH 412 Precondition Failed (version conflict)
- ETag header returned (W/"3")
- Last-Modified header returned
- Content-Type negotiation (application/fhir+json)
- Tenant-explicit route works
- Tenant-agnostic route works (single-tenant)

### E2E Tests (30+ Tests)

**PatchMultiTenantTests.cs** (port from legacy FhirPathPatchTests.cs):
- **Add Operations** (5 tests):
  - Add name to Patient
  - Add telecom to Patient
  - Add address to Patient
  - Add identifier to Patient
  - Add extension to Patient

- **Insert Operations** (5 tests):
  - Insert name at beginning
  - Insert telecom at middle
  - Insert address at end
  - Insert identifier at position 2
  - Insert extension at position 0

- **Delete Operations** (5 tests):
  - Delete name by index
  - Delete telecom by path
  - Delete address by index
  - Delete identifier by path
  - Delete extension by index

- **Replace Operations** (5 tests):
  - Replace family name
  - Replace telecom value
  - Replace address city
  - Replace birthDate
  - Replace gender

- **Move Operations** (3 tests):
  - Move name from index 1 to 0
  - Move telecom from index 2 to 1
  - Move address from index 0 to 2

- **Multi-Operation** (3 tests):
  - Replace name AND add telecom (atomic)
  - Delete address AND replace city (atomic)
  - Complex: add, delete, replace in one request

- **Error Cases** (4 tests):
  - Path not found (400)
  - Type mismatch (400)
  - Immutable property changed (400)
  - Index out of range (400)

**Multi-Tenant Isolation Tests** (3 tests):
- Tenant 1: PATCH Patient/123 (success)
- Tenant 2: PATCH Patient/123 (different resource, success)
- Cross-tenant verification (tenant 1 unchanged)

**Performance Tests** (2 tests):
- PATCH vs PUT payload size comparison (90% reduction)
- PATCH operation execution time (<100ms)

## Dependencies

### Existing Packages (No New Dependencies)

| Package | Version | Purpose | Layer |
|---------|---------|---------|-------|
| **Hl7.Fhir.R4** | 6.0.0 | Parse Parameters resource | API only |
| **Ignixa.FhirPath.Evaluation** | N/A | FHIRPath expression evaluation | Application |
| **Ignixa.SourceNodeSerialization** | N/A | ITypedElement manipulation | Application |
| **Medino** | 2.0.1 | Command/handler pattern | Application |
| **Autofac** | 8.2.0 | Dependency injection | API |

### Required Components (Already Exist)

- ✅ `IFhirRepositoryFactory` - Multi-tenant repository access
- ✅ `IFhirRepository` - CreateOrUpdateAsync() method
- ✅ `ResourceWrapper` - Resource model with metadata
- ✅ `ResourceKey` - Resource identification
- ✅ `TenantResolutionMiddleware` - Tenant extraction
- ✅ Minimal API infrastructure - Endpoint pattern
- ✅ `IMediator` - Medino messaging

### New Components (To Be Created)

- ❌ `FhirPatchOperation` - Patch operation model
- ❌ `FhirPatchParametersParser` - Parse Parameters resource
- ❌ `FhirPatchOperationExecutor` - Execute operations
- ❌ `IOperationExecutor` - Operation executor interface
- ❌ `AddOperationExecutor` - Add operation logic
- ❌ `InsertOperationExecutor` - Insert operation logic
- ❌ `DeleteOperationExecutor` - Delete operation logic
- ❌ `ReplaceOperationExecutor` - Replace operation logic
- ❌ `MoveOperationExecutor` - Move operation logic
- ❌ `FhirPatchValidator` - Validate Parameters
- ❌ `ImmutablePropertyValidator` - Protect immutables
- ❌ `PatchResourceCommand` - Medino command
- ❌ `PatchResourceHandler` - Medino handler
- ❌ `PatchEndpoints` - Minimal API routes

## Risks & Mitigation

### Risk 1: FHIRPath Expression Evaluation Complexity

**Risk**: FHIRPath expressions can be complex (e.g., `Patient.name.where(use='official').family`).

**Mitigation**:
- ✅ Use `Ignixa.FhirPath.Evaluation` (existing, proven)
- ✅ Limit supported FHIRPath features (no functions initially)
- ✅ Comprehensive error messages for unsupported expressions
- ✅ Unit tests covering common path patterns

**Severity**: Medium

### Risk 2: Type-Safe Value Setting

**Risk**: Setting values via ITypedElement requires type awareness (string vs integer vs complex types).

**Mitigation**:
- ✅ Use `Ignixa.SourceNodeSerialization.ElementModel.ITypedElement`
- ✅ Type validation before setting values
- ✅ Clear error messages for type mismatches
- ✅ Comprehensive unit tests for all FHIR types

**Severity**: Medium

### Risk 3: Multi-Operation Atomicity

**Risk**: If one operation fails, all operations should be rolled back (all-or-nothing).

**Mitigation**:
- ✅ Execute operations on cloned resource
- ✅ Only commit to repository if ALL operations succeed
- ✅ Existing transaction support (DeferredWriteCoordinator)
- ✅ Clear error messages indicating which operation failed

**Severity**: Low (existing transaction framework handles this)

### Risk 4: Version Conflicts (Optimistic Concurrency)

**Risk**: Multiple clients patching same resource concurrently can cause version conflicts.

**Mitigation**:
- ✅ ETag support with If-Match header
- ✅ Return 412 Precondition Failed on version mismatch
- ✅ Client retry logic recommended
- ✅ Document concurrency handling in API docs

**Severity**: Low (standard FHIR pattern)

### Risk 5: Immutable Property Protection

**Risk**: Client might attempt to modify id, versionId, lastUpdated via PATCH.

**Mitigation**:
- ✅ `ImmutablePropertyValidator` compares pre/post state
- ✅ Return 400 Bad Request if immutables changed
- ✅ Clear error message: "Cannot modify 'id' via PATCH (use PUT instead)"
- ✅ Unit tests covering all immutable properties

**Severity**: Low (validation catches this)

### Risk 6: Performance Overhead

**Risk**: FHIRPath evaluation and ITypedElement manipulation might be slow.

**Mitigation**:
- ✅ Performance testing (<100ms for typical patches)
- ✅ Caching of FHIRPath expressions (if needed)
- ✅ Benchmarking vs PUT (measure overhead)
- ✅ Memory profiling (ensure no ITypedElement leaks)

**Severity**: Low (benefit of 90% bandwidth reduction outweighs overhead)

## Consequences

### Benefits

1. **FHIR Compliance**: Full support for FHIR R4 PATCH operations per spec
2. **Bandwidth Reduction**: 90%+ payload size reduction vs PUT (150 KB → 800 bytes)
3. **Mobile Offline Sync**: Efficient delta updates for offline-first apps
4. **Conflict Resolution**: Granular merge strategies for concurrent edits
5. **Audit Trails**: Field-level change tracking (when combined with _history)
6. **Multi-Tenant Support**: Isolation and distributed modes supported
7. **Performance**: <100ms operation execution time (typical patches)
8. **Extensibility**: JSON Patch can be added later if needed
9. **Type Safety**: FHIR-aware validation prevents type mismatches
10. **Immutable Protection**: Automatic protection of id, versionId, lastUpdated

### Risks

1. **FHIRPath Complexity**: Complex expressions may be hard to debug
2. **Validation Overhead**: Type checking adds processing time (~50ms)
3. **Client Learning Curve**: FHIRPath syntax vs JSON Pointer
4. **Limited Tooling**: Fewer tools support FHIRPath Patch vs JSON Patch

### Migration Strategy

**No Breaking Changes**:
- ✅ PATCH is a new capability (pure addition)
- ✅ Existing PUT/GET/POST/DELETE endpoints unchanged
- ✅ No configuration changes required
- ✅ Backward compatible with Phase 1-22

**Adoption Path**:
1. **Phase 17**: FHIRPath Patch implemented, documented
2. **Phase 18+**: Clients can adopt PATCH incrementally
3. **Future**: JSON Patch added if customers request it

**Documentation**:
- Update CLAUDE.md with PATCH endpoint details
- Update capability statement (BaseCapabilities.json)
- Add examples to API documentation

### Performance Impact

**Expected Performance**:
- **Operation Execution**: <100ms for typical patches (5-10 operations)
- **Payload Size**: 90-99% reduction vs PUT (150 KB → 800 bytes)
- **Memory**: Similar to PUT (ITypedElement manipulation)
- **Throughput**: Minimal impact (PATCH is I/O bound, not CPU bound)

**Benchmarks** (to be measured in Week 94):
- PATCH Patient.name[0].family: ~50ms
- PATCH with 10 operations: ~150ms
- PUT full Patient resource: ~100ms (baseline)

## References

### FHIR Specifications

- **FHIR R4 Patch Operations**: https://hl7.org/fhir/R4/http.html#patch
- **FHIRPath Patch**: https://hl7.org/fhir/R4/fhirpatch.html
- **JSON Patch**: https://hl7.org/fhir/R4/http.html#patch (Section 3.1.0.7.2)
- **RFC 6902 (JSON Patch)**: https://tools.ietf.org/html/rfc6902
- **FHIRPath Specification**: https://hl7.org/fhirpath/

### Internal Documentation

- **Master Roadmap**: [ADR-2500](adr-2500-master-implementation-roadmap.md)
- **Multi-Tenancy**: [ADR-2502](adr-2502-multi-tenancy-architecture.md)
- **Bundle Non-CRUD Operations**: [bundle-non-crud-operations.md](../investigations/bundle-non-crud-operations.md)
- **Dynamic FHIR Routing**: [dynamic-fhir-routing.md](../investigations/dynamic-fhir-routing.md)

### Legacy Code References

**Legacy FHIR Server (src-old/)**:
- `src-old/Microsoft.Health.Fhir.Core/Features/Operations/Patch/PatchPayload.cs`
- `src-old/Microsoft.Health.Fhir.Core/Features/Operations/Patch/PatchParametersExtensions.cs`
- `src-old/test/Microsoft.Health.Fhir.Tests.E2E/Rest/Patch/FhirPathPatchTests.cs`
- `src-old/test/Microsoft.Health.Fhir.Tests.E2E/Rest/Patch/JsonPatchTests.cs`

**Key Insights from Legacy**:
- FhirPathPatchTests.cs has 30+ comprehensive test cases
- JsonPatchTests.cs has JSON Patch examples (optional enhancement)
- PatchParametersExtensions.cs shows Parameters parsing patterns

### External Resources

- **HAPI FHIR Patch Support**: https://hapifhir.io/hapi-fhir/docs/server_plain/rest_operations.html#patch
- **Firely SDK ITypedElement**: https://docs.fire.ly/projects/Firely-NET-SDK/model/poco-introduction.html
- **FHIRPath Evaluator**: https://docs.fire.ly/projects/Firely-NET-SDK/client/fhirpath.html

---

## Document Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-10-17 | Claude (fhir-coordinator) | Initial ADR creation |

---

**End of ADR-2520**
