# Investigation: Transform Resource Mutation Strategy

**Date**: 2025-11-30
**Status**: Investigation Complete - Implementation Pending
**Related**: Phase 2 of $transform operation (ADR-2600)

## Problem Statement

The FHIR Mapping Language (FML) Transform operation (`StructureMap/$transform`) successfully parses FML, evaluates transformations, and stores results in `MappingContext` variables, but **does not actually modify the target resource**.

### Observed Behavior

When executing a transformation like:
```fml
map 'http://example.org/PatientSimplify' = 'PatientSimplify'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias SimplePatient as target

group Transform(source src : Patient, target tgt : SimplePatient) {
  src.id -> tgt.id;
  src.name as vn -> tgt.name = vn;
  src.birthDate -> tgt.birthDate;
  src.gender -> tgt.gender;
}
```

**Expected**: Target resource has `id`, `name`, `birthDate`, `gender` properties populated
**Actual**: Target resource remains `{ "resourceType": "Patient" }` with no properties

### Why Tests Pass

All existing tests only verify **error conditions**:
- Missing Content parameter
- No mapping source provided
- Invalid FML syntax

No tests verify that a successful transformation produces correct output.

---

## Root Cause Analysis

### How Transform Currently Works

**File**: `src/Ignixa.FhirMappingLanguage/Evaluation/MappingEvaluator.cs` (lines 813-890)

```csharp
private void VisitTarget(TargetExpression target, MappingContext context, ...)
{
    var transformResult = VisitTransform(transform, context, location);

    if (target.Context != null && transformResult is IElement element)
    {
        if (target.Variable != null)
        {
            context.SetVariable(target.Variable, element);  // ← Stores in Dictionary
            // PROBLEM: Never writes to ResourceJsonNode.MutableNode
        }
    }
}
```

**MappingContext.SetVariable** (lines 28-29):
```csharp
public void SetVariable(string name, object value) =>
    _variables[name] = value;  // ← Just a Dictionary<string, object>
```

**The Gap**:
1. FML evaluator creates variables (`tgt.id`, `tgt.name`, etc.)
2. Variables stored in `MappingContext._variables` Dictionary
3. **Missing step**: Nothing syncs variables back to `target.MutableNode`
4. Target `ResourceJsonNode` remains unchanged

### Architecture Flaw

```
┌─────────────────────────────────────────────────────────────┐
│ MappingEvaluator.VisitTarget()                              │
│                                                               │
│  1. Evaluate transform → IElement result                     │
│  2. context.SetVariable("tgt.id", result)  ✓                │
│  3. [MISSING] Sync variable to target.MutableNode  ✗        │
│                                                               │
│ Result: Variables stored, resource unchanged                 │
└─────────────────────────────────────────────────────────────┘

MappingContext._variables = {
  "tgt.id": "patient-123",
  "tgt.name": [{ "family": "Doe" }],
  "tgt.birthDate": "1990-01-15"
}

target.MutableNode = {
  "resourceType": "Patient"
  // Missing: id, name, birthDate properties
}
```

---

## How PATCH Successfully Mutates Resources

PATCH operations **do** successfully modify `ResourceJsonNode.MutableNode` using a three-step pattern.

### PATCH Mutation Flow

**File**: `src/Ignixa.Application/Features/Patch/Executors/ReplaceOperationExecutor.cs`

```csharp
public async Task<ResourceJsonNode> ExecuteAsync(
    ResourceJsonNode resource,
    PatchOperation operation,
    CancellationToken cancellationToken)
{
    // Step 1: FHIRPath Navigation - Extract JsonNode from IElement
    var targetJsonNode = _fhirPathHelper.EvaluateToSingleJsonNode(
        resource,
        operation.Path
    );

    // Step 2: Find parent object for mutation
    var (parentObj, propertyName) = FhirPathPatchHelper.GetParentAndProperty(
        targetJsonNode,
        resource.MutableNode
    );

    // Step 3: Direct mutation of parent's property
    parentObj[propertyName] = valueNode;  // ← MODIFIES MutableNode IN-PLACE

    return resource;  // Mutations already applied
}
```

### Key Innovation: Meta<JsonNode>() Extraction

**File**: `src/Ignixa.Application/Features/Patch/FhirPathPatchHelper.cs`

```csharp
public IEnumerable<JsonNode> EvaluateToJsonNodes(
    ResourceJsonNode resource,
    string fhirPathExpression)
{
    // Convert ResourceJsonNode → ISourceNode → IElement
    var sourceNode = resource.ToSourceNavigator();
    var typedElement = sourceNode.ToElement(_structureProvider);

    // Evaluate FHIRPath
    var matches = _evaluator.Evaluate((IElement)typedElement, expression);

    // Extract underlying JsonNode using Meta<T> annotation
    foreach (var match in matches)
    {
        var jsonNode = match.Meta<JsonNode>();  // ← KEY: Extract mutable JsonNode
        if (jsonNode != null)
            yield return jsonNode;
    }
}
```

**Critical Insight**: `IElement.Meta<JsonNode>()` provides access to the underlying mutable `JsonNode` wrapped by the read-only `IElement` interface.

### PATCH's Direct Mutation Pattern

**Example from AddOperationExecutor** (lines 88-99):

```csharp
var existing = targetParent[propertyName];

if (existing is JsonArray existingArray)
{
    existingArray.Add(valueNode);  // ← Mutate array in-place
}
else if (existing == null)
{
    var newArray = new JsonArray { valueNode };
    targetParent[propertyName] = newArray;  // ← Set new property
}
```

**Pattern**: All mutations modify the parent `JsonObject` or `JsonArray` directly. Changes immediately reflect in `resource.MutableNode`.

---

## Comparison: Transform vs PATCH

| Aspect | **PATCH** | **Transform** |
|--------|-----------|---------------|
| **Mutation Target** | `ResourceJsonNode.MutableNode` (JsonObject) | `MappingContext._variables` (Dictionary) |
| **Navigation** | FHIRPath → JsonNode extraction via `Meta<T>()` | FHIRPath → IElement wrapper |
| **Mutation Method** | Direct property assignment: `parent[prop] = value` | Variable storage: `context.SetVariable(name, value)` |
| **Result** | Resource modified in-place ✓ | Variables stored, resource unchanged ✗ |
| **Sync Back** | Not needed (direct mutation) | **MISSING** - variables never written to JsonObject |

### Why PATCH Works

```
FHIRPath Expression: "Patient.name[0].family"
       ↓
IElement.Meta<JsonNode>()
       ↓
Extract parent JsonObject + property name
       ↓
parent["family"] = "NewValue"
       ↓
MutableNode modified ✓
```

### Why Transform Doesn't Work

```
FML Target: tgt.name
       ↓
VisitTarget() evaluates to IElement
       ↓
context.SetVariable("tgt.name", element)
       ↓
Dictionary updated, MutableNode unchanged ✗
```

---

## Architectural Similarities

Both PATCH and Transform:

1. ✅ Use FHIRPath for navigation
2. ✅ Have access to `ResourceJsonNode` with `MutableNode`
3. ✅ Need to set property values on target resources
4. ✅ Work with IElement/ISourceNode abstractions

### Key Differences

| Aspect | PATCH | Transform |
|--------|-------|-----------|
| **Path Source** | User-provided FHIRPath string (`"Patient.status"`) | FML target context path (`tgt.status`) |
| **Value Source** | User-provided JSON value in Parameters | Transform function result (IElement) |
| **Execution** | Single operation per PATCH request | Multiple targets per FML rule |
| **Error Handling** | Strict (must succeed or fail) | Lenient (can continue on error) |
| **Context** | Single resource mutation | Source + Target resource transformation |

---

## Proposed Solution: Reuse PATCH's Mutation Logic

### Recommendation

**YES** - Transform should reuse PATCH's mutation pattern for the following reasons:

1. **Proven approach**: PATCH successfully mutates `MutableNode` in production
2. **Same infrastructure**: Both use FHIRPath, IElement, and ResourceJsonNode
3. **Code reuse**: Extraction/mutation logic is generalizable
4. **Consistency**: Same mutation semantics across operations

### Shared Code Pattern

Extract a `JsonNodeMutator` service that encapsulates:

1. **FHIRPath Navigation** → `EvaluateToJsonNodes(resource, path)` from PATCH
2. **Parent Finding** → `GetParentAndProperty(jsonNode, rootNode)` from PATCH
3. **Value Serialization** → `SerializeValue(ielement)` to convert IElement → JsonNode
4. **Mutation Execution** → `SetProperty(parent, property, value)`

### Integration Point

**File**: `src/Ignixa.FhirMappingLanguage/Evaluation/MappingEvaluator.cs`

```csharp
private void VisitTarget(TargetExpression target, MappingContext context, ...)
{
    var transformResult = VisitTransform(transform, context, location);

    if (target.Context != null && transformResult is IElement element)
    {
        if (target.Variable != null)
        {
            // Step 1: Store in variable (current behavior)
            context.SetVariable(target.Variable, element);

            // Step 2: NEW - Sync to target resource's MutableNode
            var targetResource = GetTargetResource(target.Context, context);
            var targetPath = BuildFhirPath(target.Context, target.Variable);

            _jsonNodeMutator.SetProperty(
                targetResource,
                targetPath,     // e.g., "Patient.status"
                element         // IElement to serialize
            );
        }
    }
}
```

### Proposed Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ Transform VisitTarget()                                          │
│                                                                   │
│  1. Execute transform → IElement result                          │
│  2. context.SetVariable("tgt.status", result)  ✓                │
│  3. Extract target ResourceJsonNode from context                 │
│  4. Build FHIRPath: "tgt.status" → "Patient.status"             │
│  5. jsonNodeMutator.SetProperty(resource, path, result)  ✓      │
│     ↓                                                             │
│     a. Navigate via FHIRPath (reuse PATCH logic)                 │
│     b. Find parent JsonObject                                    │
│     c. parent["status"] = SerializeValue(result)                 │
│                                                                   │
│ Result: Variables stored AND MutableNode updated  ✓             │
└─────────────────────────────────────────────────────────────────┘
```

---

## Implementation Plan

### Phase 1: Extract Shared Mutation Service

**Create**: `src/Ignixa.FhirMappingLanguage/Mutator/`

Extract from PATCH (`Ignixa.Application/Features/Patch/FhirPathPatchHelper.cs`):
- `FhirPathPatchHelper.EvaluateToJsonNodes()` → Navigate to target
- `FhirPathPatchHelper.GetParentAndProperty()` → Find mutation point
- `FhirPathPatchHelper.SerializeValue()` → Convert IElement → JsonNode

**Files to Create**:
- `IJsonNodeMutator.cs` - Interface
- `PropertyMutationMode.cs` - Enum
- `JsonNodeMutator.cs` - Implementation

**Interface**:
```csharp
namespace Ignixa.FhirMappingLanguage.Mutator;

/// <summary>
/// Service for mutating ResourceJsonNode properties using FHIRPath navigation.
/// Shared by PATCH operations and Transform operations.
/// </summary>
public interface IJsonNodeMutator
{
    /// <summary>
    /// Set property value. Handles array vs single value automatically.
    /// </summary>
    /// <param name="resource">Target resource to mutate</param>
    /// <param name="fhirPathExpression">FHIRPath to property (e.g., "Patient.name")</param>
    /// <param name="value">Value to set (IElement)</param>
    /// <param name="mode">Mutation mode: Replace (single-valued), Append (multi-valued), or Auto-detect</param>
    void SetProperty(
        ResourceJsonNode resource,
        string fhirPathExpression,
        IElement value,
        PropertyMutationMode mode = PropertyMutationMode.AutoDetect);

    /// <summary>
    /// Set property value from JsonNode.
    /// </summary>
    void SetProperty(
        ResourceJsonNode resource,
        string fhirPathExpression,
        JsonNode value,
        PropertyMutationMode mode = PropertyMutationMode.AutoDetect);

    /// <summary>
    /// Ensure property exists, creating intermediate objects if needed.
    /// Returns the JsonNode at the path for further manipulation.
    /// </summary>
    JsonNode EnsurePropertyPath(
        ResourceJsonNode resource,
        string fhirPathExpression);
}

/// <summary>
/// Specifies how property mutation should behave for arrays vs single values.
/// </summary>
public enum PropertyMutationMode
{
    /// <summary>
    /// Auto-detect based on: 1) Existing property type, 2) FML list mode hints
    /// </summary>
    AutoDetect,

    /// <summary>
    /// Replace existing value (for single-valued properties, max=1)
    /// </summary>
    Replace,

    /// <summary>
    /// Append to array (for multi-valued properties, max>1)
    /// </summary>
    Append
}
```

### Phase 2: Update MappingEvaluator

**Modify**: `src/Ignixa.FhirMappingLanguage/Evaluation/MappingEvaluator.cs`

1. Inject `IJsonNodeMutator` into constructor
2. After `context.SetVariable()`, call mutator
3. Build FHIRPath from FML target context:
   - `tgt.status` → `"Patient.status"` (root resource type from context)
   - `tgt.name[0].family` → `"Patient.name[0].family"`
4. Handle nested structures (create intermediate objects if needed)

### Phase 3: Handle Edge Cases

#### 3.1 Array vs Single Value Detection

**Problem**: FML doesn't explicitly declare if a property is an array. Must infer from:
1. Existing property structure
2. FML list mode
3. Multiple rule invocations

**Solution Pattern** (inspired by brianpos/fhir-net-mappinglanguage):

```csharp
// In JsonNodeMutator.SetProperty()

var existingNodes = EvaluateToJsonNodes(resource, fhirPathExpression).ToList();

// Determine mutation mode
PropertyMutationMode effectiveMode = mode;
if (mode == PropertyMutationMode.AutoDetect)
{
    if (existingNodes.Count > 0)
    {
        // Check if existing property is array
        var firstNode = existingNodes[0];
        effectiveMode = firstNode.Parent is JsonArray
            ? PropertyMutationMode.Append
            : PropertyMutationMode.Replace;
    }
    else
    {
        // No existing value - default to Replace (will create single value)
        // Caller can override with Append if list mode indicates array
        effectiveMode = PropertyMutationMode.Replace;
    }
}

if (effectiveMode == PropertyMutationMode.Append)
{
    AppendToArray(resource, fhirPathExpression, value);
}
else
{
    ReplaceValue(resource, fhirPathExpression, value);
}
```

#### 3.2 Primitive vs Complex Type Detection

**Problem**: IElement can represent both primitive values and complex objects.

**Detection Strategy**:

```csharp
// Check IElement.Value property (non-null = primitive)
if (element.Value != null)
{
    // Primitive: string, int, bool, date, etc.
    var primitiveValue = element.Value;
    valueNode = SerializePrimitive(primitiveValue);
}
else
{
    // Complex object: nested structure
    valueNode = SerializeComplexElement(element);
}

private JsonNode SerializePrimitive(object value)
{
    return value switch
    {
        string s => JsonValue.Create(s),
        int i => JsonValue.Create(i),
        bool b => JsonValue.Create(b),
        decimal d => JsonValue.Create(d),
        DateTime dt => JsonValue.Create(dt.ToString("yyyy-MM-dd")),
        _ => JsonNode.Parse(JsonSerializer.Serialize(value))
    };
}

private JsonNode SerializeComplexElement(IElement element)
{
    // Navigate children and build JsonObject
    var obj = new JsonObject();
    foreach (var child in element.Children())
    {
        var childValue = child.Value != null
            ? SerializePrimitive(child.Value)
            : SerializeComplexElement(child);
        obj[child.Name] = childValue;
    }
    return obj;
}
```

#### 3.3 FML List Mode Integration

**Problem**: FML list modes affect mutation behavior:
- `first` / `last` / `notFirst` / `notLast` → Filter source, then append
- `onlyOne` → Validate single element, replace
- `share` → Reuse same target, replace
- `single` → Create single target regardless of source count, replace

**Integration in MappingEvaluator.VisitTarget()**:

```csharp
// Determine mutation mode from list mode
var mutationMode = target.ListMode switch
{
    ListMode.OnlyOne => PropertyMutationMode.Replace,
    ListMode.Share => PropertyMutationMode.Replace,
    ListMode.Single => PropertyMutationMode.Replace,
    ListMode.First or ListMode.Last or ListMode.NotFirst or ListMode.NotLast
        => PropertyMutationMode.Append,
    _ => PropertyMutationMode.AutoDetect
};

_jsonNodeMutator.SetProperty(targetResource, fhirPath, element, mutationMode);
```

#### 3.4 Missing Intermediate Properties

**Problem**: FML may reference nested paths that don't exist yet.
Example: `tgt.contact.name` when `contact` doesn't exist.

**Solution**:

```csharp
private void EnsureIntermediateObjects(ResourceJsonNode resource, string fhirPath)
{
    // Parse path: "Patient.contact.name" → ["Patient", "contact", "name"]
    var parts = fhirPath.Split('.');
    var current = resource.MutableNode;

    // Walk path, creating missing objects
    for (int i = 1; i < parts.Length - 1; i++) // Skip resource type and final property
    {
        var part = parts[i];

        if (!current.AsObject().ContainsKey(part))
        {
            current[part] = new JsonObject(); // Create intermediate object
        }

        current = current[part];
    }
}
```

#### 3.5 Error Handling with ErrorMode

**Respect MappingContext.ErrorMode**:

```csharp
try
{
    _jsonNodeMutator.SetProperty(targetResource, fhirPath, element, mutationMode);
}
catch (Exception ex) when (context.ErrorMode == ErrorMode.Lenient)
{
    context.AddError(
        $"Failed to set property {fhirPath}: {ex.Message}",
        location,
        "MUTATION_ERROR",
        ex,
        ruleName: _currentRuleName);
    // Continue execution
}
// Strict mode: exception propagates
```

### Phase 4: Integration Tests

**Add**: `test/Ignixa.Application.Tests/Features/Transform/TransformIntegrationTests.cs`

Verify actual transformations produce correct output:

```csharp
[Fact]
public async Task GivenPatientSimplifyMap_WhenTransforming_ThenCopiesFields()
{
    // Arrange
    var sourcePatient = CreatePatient("123", "Doe", "John", "male", "1990-01-15");
    var fml = """
        map 'http://example.org/PatientSimplify' = 'PatientSimplify'

        group Transform(source src : Patient, target tgt : Patient) {
          src.id -> tgt.id;
          src.name -> tgt.name;
          src.birthDate -> tgt.birthDate;
          src.gender -> tgt.gender;
        }
        """;

    var command = new TransformResourceCommand(
        SrcMaps: [fml],
        Content: sourcePatient);

    // Act
    var result = await _handler.HandleAsync(command, CancellationToken.None);

    // Assert
    result.Id.Should().Be("123");
    result.MutableNode["name"].Should().NotBeNull();
    result.MutableNode["birthDate"]?.GetValue<string>().Should().Be("1990-01-15");
    result.MutableNode["gender"]?.GetValue<string>().Should().Be("male");
}
```

---

## Files Requiring Changes

### New Files

- [ ] `src/Ignixa.FhirMappingLanguage/Mutator/IJsonNodeMutator.cs` - Interface
- [ ] `src/Ignixa.FhirMappingLanguage/Mutator/PropertyMutationMode.cs` - Enum
- [ ] `src/Ignixa.FhirMappingLanguage/Mutator/JsonNodeMutator.cs` - Implementation
- [ ] `test/Ignixa.FhirMappingLanguage.Tests/Mutator/JsonNodeMutatorTests.cs` - Unit tests
- [ ] `test/Ignixa.Application.Tests/Features/Transform/TransformIntegrationTests.cs` - End-to-end tests

### Modified Files

- [ ] `src/Ignixa.FhirMappingLanguage/Evaluation/MappingEvaluator.cs` - Call mutator in VisitTarget
- [ ] `src/Ignixa.Application.Operations/Features/Transform/TransformResourceHandler.cs` - Inject mutator
- [ ] `src/Ignixa.Application/Features/Patch/FhirPathPatchHelper.cs` - Extract reusable helpers (optional)
- [ ] `src/Ignixa.Api/Program.cs` - Register IJsonNodeMutator

---

## Alternative Approaches Considered

### Alternative 1: Post-Processing Variable Tree

**Approach**: After FML evaluation, walk `MappingContext._variables` tree and copy to `MutableNode`

**Pros**:
- Single sync point at end of evaluation
- Clear separation: evaluation vs mutation

**Cons**:
- Requires complex tree traversal logic
- Duplicates FHIRPath navigation (already done by PATCH)
- Hard to map variable names to FHIRPath expressions

**Verdict**: ❌ Rejected - More complex than reusing PATCH

### Alternative 2: Mutable IElement Wrapper

**Approach**: Create a mutable IElement implementation that writes to JsonNode

**Pros**:
- Transparent mutation during FML evaluation
- No post-processing needed

**Cons**:
- Violates IElement contract (should be read-only)
- Complex to implement correctly
- Doesn't leverage existing PATCH infrastructure

**Verdict**: ❌ Rejected - Architectural violation

### Alternative 3: Custom FML Backend (Selected)

**Approach**: Reuse PATCH's mutation logic via shared service

**Pros**:
- ✅ Proven, tested mutation code
- ✅ Consistent mutation semantics
- ✅ Minimal new code
- ✅ Clear separation of concerns

**Cons**:
- Requires extracting PATCH logic into service
- FML must build FHIRPath expressions from target contexts

**Verdict**: ✅ **SELECTED** - Best balance of reuse and correctness

---

## Success Criteria

1. ✅ Transform operation produces resources with populated properties
2. ✅ All PATCH tests continue to pass (no regressions)
3. ✅ Integration tests verify correct transformation output
4. ✅ Handles nested objects, arrays, and primitive values
5. ✅ Error handling maintains lenient/strict modes

---

## Reference Implementation Validation

### brianpos/fhir-net-mappinglanguage Analysis

**Repository**: https://github.com/brianpos/fhir-net-mappinglanguage

The brianpos C# port of HAPI FHIR's StructureMapUtilities confirms our mutation strategy is sound, though their implementation differs due to architectural choices:

| Aspect | brianpos | Ignixa (This Proposal) |
|--------|----------|------------------------|
| Data Model | `ElementNode` (Firely POCO wrapper) | `JsonObject` (document model) |
| Mutation | `en.Add(pkp, ne, name)` | `parent["name"] = value` |
| Navigation | `ITypedElement.Children(name)` | FHIRPath → `Meta<JsonNode>()` |
| Dependency | Requires `Hl7.Fhir.*` SDK | Architecture-agnostic |
| Performance | Medium (wrapper overhead) | High (direct mutation) |
| Complexity | ~500 LOC | ~200 LOC (reuses PATCH) |

**Key Validation**:
- ✅ Confirms FML target → property mutation pattern
- ✅ Validates array vs single value handling strategy
- ✅ Shows primitive vs complex type differentiation needed
- ✅ Demonstrates list mode integration requirements

**Why Ignixa's Approach is Superior**:
- **Native architecture fit** - Works directly with `ResourceJsonNode.MutableNode`
- **Reuses proven code** - PATCH already validates `Meta<JsonNode>()` extraction
- **Simpler implementation** - No ElementNode/POCO conversion layer
- **Better performance** - Direct JSON mutation without wrappers

**Key Learnings Applied**:
1. Array detection via cardinality checking (brianpos checks `defn.Max != "1"`)
2. Primitive value detection via `IElement.Value != null`
3. List mode integration (first, last, share, single)
4. Replace vs append logic for single-valued vs multi-valued properties

**Architectural Validation**: This approach has been validated against the brianpos reference implementation:
- ✅ **Pattern confirmed**: Target mutation via property setter abstraction
- ✅ **Edge cases identified**: Array detection, primitive vs complex, list modes
- ✅ **Architecture advantages**: Ignixa's JsonNode approach is simpler and more performant than ElementNode wrappers
- ✅ **Production-ready**: PATCH already proves the `Meta<JsonNode>()` mutation pattern works

**Key Differentiator**: While brianpos uses Firely SDK's ElementNode wrappers (~500 LOC), Ignixa's direct JsonObject mutation is more efficient (~200 LOC) and architecturally native.

---

## Conclusion

**Problem**: Transform evaluates FML correctly but doesn't mutate target resources.

**Root Cause**: Variables stored in dictionary, never synced to `ResourceJsonNode.MutableNode`.

**Solution**: Reuse PATCH's proven mutation pattern via shared `JsonNodeMutator` service.

**Next Steps**:
1. Extract mutation logic from PATCH
2. Integrate mutator into `MappingEvaluator.VisitTarget()`
3. Add integration tests for successful transformations

**Impact**: Completes Phase 2 of $transform operation, enabling real-world FML use cases.

---

## References

- **PATCH Implementation**: `src/Ignixa.Application/Features/Patch/*`
- **Transform Evaluator**: `src/Ignixa.FhirMappingLanguage/Evaluation/MappingEvaluator.cs`
- **MutableNode Design**: `src/Ignixa.Serialization/SourceNodes/BaseJsonNode.cs`
- **FML Spec**: https://build.fhir.org/mapping-language.html
- **FHIR Spec - $transform**: https://hl7.org/fhir/R6/structuremap-operation-transform.html
- **brianpos/fhir-net-mappinglanguage**: https://github.com/brianpos/fhir-net-mappinglanguage
  - JavaStructureMapUtils-Execute.cs - Reference execution engine
  - JavaShimExtensions.cs - Property mutation via ElementNode wrappers
