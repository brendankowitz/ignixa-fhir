# Validation System: Codegen Requirements Analysis

**Date**: October 20, 2025
**Status**: Analysis Complete
**Related**: ADR-2527-comprehensive-validation-system.md (Phase 4-6)

## Executive Summary

**ANSWER: YES ✅** - We need additional codegen for comprehensive validation (Phase 4-6).

**Current Codegen Status**:
- ✅ StructureDefinition → `IStructureDefinitionSummaryProvider` (COMPLETE)
- ⚠️ ValueSet → Enums (Language exists, but NOT GENERATED)
- ❌ Binding metadata (strength, ValueSet URLs) NOT extracted
- ❌ FHIRPath invariants NOT extracted
- ❌ Extension definitions NOT extracted

## Current Codegen Capabilities

### What We Have (codegen/)

| Generator | File | Status | Output |
|-----------|------|--------|--------|
| **StructureDefinitionProvider** | `CSharpStructureProviderLanguage.cs` | ✅ COMPLETE | `R4StructureDefinitionSummaryProvider.g.cs` |
| **ValueSet Enums** | `CSharpValueSetLanguage.cs` | ⚠️ EXISTS (NOT RUN) | None (not generated) |
| **SearchParameters** | `CSharpSearchParameterLanguage.cs` | ✅ COMPLETE | Search parameter metadata |
| **Compartments** | `CSharpCompartmentLanguage.cs` | ✅ COMPLETE | Compartment definitions |

### What's Generated vs What's Needed

**Currently Generated** (Phase 1-3):
```
src/Ignixa.Specification/Generated/
├── R4StructureDefinitionSummaryProvider.g.cs    ✅ (59,318 lines)
├── R4BStructureDefinitionSummaryProvider.g.cs   ✅
├── R5StructureDefinitionSummaryProvider.g.cs    ✅
└── STU3StructureDefinitionSummaryProvider.g.cs  ✅
```

**Needed for Phase 4-6**:
```
src/Ignixa.Specification/Generated/
├── R4StructureDefinitionSummaryProvider.g.cs    ✅ EXISTS
├── R4ValueSetProvider.g.cs                      ❌ MISSING (Phase 5)
├── R4BindingProvider.g.cs                       ❌ MISSING (Phase 5)
├── R4InvariantProvider.g.cs                     ❌ MISSING (Phase 4)
└── R4ExtensionProvider.g.cs                     ❌ MISSING (Phase 4)
```

## Phase-by-Phase Codegen Requirements

### Phase 4: Advanced Validators (Weeks 7-8)

**Validation Needs**:
1. ✅ FHIRPath invariants (ele-1, bdl-7, dom-*, ref-*)
2. ✅ Fixed value constraints
3. ✅ Pattern constraints
4. ✅ Choice types (value[x])
5. ✅ Min/max value constraints
6. ⚠️ Extension definitions

**Codegen Required**:

#### 1. FHIRPath Invariant Provider (NEW)

**Purpose**: Extract all FHIRPath constraint expressions from StructureDefinitions

**Example Output**:
```csharp
// src/Ignixa.Specification/Generated/R4InvariantProvider.g.cs
public class R4InvariantProvider : IInvariantProvider
{
    private static readonly Dictionary<string, ConstraintDefinition> Constraints = new()
    {
        ["ele-1"] = new ConstraintDefinition
        {
            Key = "ele-1",
            Severity = ConstraintSeverity.Error,
            Human = "All FHIR elements must have a @value or children",
            Expression = "hasValue() or (children().count() > id.count())",
            Xpath = "@value|f:*|h:div"
        },
        ["bdl-7"] = new ConstraintDefinition
        {
            Key = "bdl-7",
            Severity = ConstraintSeverity.Error,
            Human = "FullUrl must be unique in a bundle, or else entries with the same fullUrl must have different meta.versionId (except in history bundles)",
            Expression = "(type = 'history') or entry.where(fullUrl.exists()).select(fullUrl&resource.meta.versionId).isDistinct()",
            Xpath = "..."
        },
        // ~500 invariants from base FHIR spec
    };

    public ConstraintDefinition? GetConstraint(string key) => Constraints.TryGetValue(key, out var c) ? c : null;

    public IReadOnlyList<ConstraintDefinition> GetConstraintsForType(string resourceType)
    {
        // Return all constraints that apply to this type
    }
}
```

**Data Source**: `StructureDefinition.snapshot.element[].constraint[]`

**Complexity**: Medium
- Parse constraint elements from StructureDefinitions
- Group by resource type
- Extract key, severity, human, expression, xpath

**Benefit**: Enables `FhirPathInvariantAssertion` to validate without runtime parsing

---

#### 2. Extension Definition Provider (NEW - Partial)

**Purpose**: Provide metadata for base FHIR extensions (e.g., patient-birthPlace)

**Example Output**:
```csharp
// src/Ignixa.Specification/Generated/R4ExtensionProvider.g.cs
public class R4ExtensionProvider : IExtensionProvider
{
    private static readonly Dictionary<string, ExtensionDefinition> Extensions = new()
    {
        ["http://hl7.org/fhir/StructureDefinition/patient-birthPlace"] = new ExtensionDefinition
        {
            Url = "http://hl7.org/fhir/StructureDefinition/patient-birthPlace",
            Title = "Patient Birth Place",
            Description = "The registered place of birth of the patient.",
            Context = new[] { "Patient" },
            ValueType = "Address",
            Cardinality = new CardinalityDefinition(0, 1)
        },
        // ~150 base FHIR extensions
    };

    public ExtensionDefinition? GetExtension(string url) => Extensions.TryGetValue(url, out var e) ? e : null;
}
```

**Data Source**: FHIR package `StructureDefinition` resources with `kind="complex-type"` and `type="Extension"`

**Complexity**: Medium
- Filter StructureDefinitions for extensions
- Extract context, value type, cardinality
- Handle multiple contexts

**Benefit**: Basic extension validation without loading full StructureDefinitions

**Note**: Custom IG extensions will need separate loading mechanism (Phase 5+)

---

### Phase 5: Terminology & Slicing (Weeks 9-11)

**Validation Needs**:
1. ✅ Terminology binding validation
2. ✅ Binding strength (required, extensible, preferred, example)
3. ✅ ValueSet expansion (for offline validation)
4. ✅ Slicing discriminators
5. ✅ Extension schema resolution

**Codegen Required**:

#### 1. Binding Provider (NEW)

**Purpose**: Extract binding metadata (ValueSet URLs, strength) from ElementDefinitions

**Example Output**:
```csharp
// src/Ignixa.Specification/Generated/R4BindingProvider.g.cs
public class R4BindingProvider : IBindingProvider
{
    private static readonly Dictionary<string, ElementBindingDefinition> Bindings = new()
    {
        ["Patient.gender"] = new ElementBindingDefinition
        {
            ElementPath = "Patient.gender",
            Strength = BindingStrength.Required,
            ValueSetUrl = "http://hl7.org/fhir/ValueSet/administrative-gender",
            Description = "The gender of a person used for administrative purposes."
        },
        ["Observation.status"] = new ElementBindingDefinition
        {
            ElementPath = "Observation.status",
            Strength = BindingStrength.Required,
            ValueSetUrl = "http://hl7.org/fhir/ValueSet/observation-status",
            Description = "Codes identifying the lifecycle stage of an observation."
        },
        // ~1,200 bindings in base FHIR R4
    };

    public ElementBindingDefinition? GetBinding(string elementPath) => Bindings.TryGetValue(elementPath, out var b) ? b : null;

    public IReadOnlyList<ElementBindingDefinition> GetBindingsForType(string resourceType)
    {
        return Bindings.Values.Where(b => b.ElementPath.StartsWith(resourceType + ".")).ToList();
    }
}
```

**Data Source**: `StructureDefinition.snapshot.element[].binding`

**Complexity**: Low
- Extract binding information from ElementDefinitions
- Store as dictionary keyed by element path
- Include strength and ValueSet URL

**Benefit**: Fast binding lookup without traversing StructureDefinition trees

---

#### 2. ValueSet Enum Generator (ENHANCE EXISTING)

**Current Status**: Language exists (`CSharpValueSetLanguage.cs`) but NOT RUN during codegen

**Purpose**: Generate C# enums for normative ValueSets (static validation)

**Example Output**:
```csharp
// src/Ignixa.Specification/Generated/R4ValueSets.g.cs
public enum AdministrativeGender
{
    [EnumMember(Value = "male")]
    Male,

    [EnumMember(Value = "female")]
    Female,

    [EnumMember(Value = "other")]
    Other,

    [EnumMember(Value = "unknown")]
    Unknown
}

public enum ObservationStatus
{
    [EnumMember(Value = "registered")]
    Registered,

    [EnumMember(Value = "preliminary")]
    Preliminary,

    [EnumMember(Value = "final")]
    Final,

    // ... 8 total values
}

// ~50 normative ValueSets as enums
```

**Data Source**: FHIR package `ValueSet` resources with status="active" and normative maturity

**Complexity**: Low (language already exists)

**Action Required**:
1. Update `generate.ps1` / `generate.sh` to invoke `CSharpValueSetLanguage`
2. Configure output path
3. Test generation

**Benefit**:
- Static validation for normative codes (no terminology service needed)
- Type-safe code completion
- ~50 ValueSets cover 80% of validation needs

---

#### 3. ValueSet Expansion Provider (NEW - Optional)

**Purpose**: Provide expanded ValueSet codes for offline validation

**Example Output**:
```csharp
// src/Ignixa.Specification/Generated/R4ValueSetExpansions.g.cs
public class R4ValueSetExpansions : IValueSetExpansionProvider
{
    // Only for small, stable ValueSets (~500 codes total)
    private static readonly Dictionary<string, HashSet<string>> Expansions = new()
    {
        ["http://hl7.org/fhir/ValueSet/administrative-gender"] = new HashSet<string>
        {
            "male", "female", "other", "unknown"
        },
        ["http://hl7.org/fhir/ValueSet/observation-status"] = new HashSet<string>
        {
            "registered", "preliminary", "final", "amended", "corrected", "cancelled", "entered-in-error", "unknown"
        },
        // ~20 small ValueSets for offline validation
    };

    public bool IsCodeValid(string valueSetUrl, string code, string? system = null)
    {
        return Expansions.TryGetValue(valueSetUrl, out var codes) && codes.Contains(code);
    }
}
```

**Data Source**: FHIR package `ValueSet` resources with pre-computed expansions

**Complexity**: Medium
- Only generate for small, stable ValueSets (<100 codes)
- Large ValueSets (ICD-10, SNOMED) require terminology server
- Extract `ValueSet.expansion.contains[]`

**Benefit**: Offline validation for common codes (no terminology service latency)

**Trade-off**: Code size vs runtime dependency

**Recommendation**: Start with enums only (simpler), add expansions if needed

---

### Phase 6: Integration & Testing (Weeks 12-14)

**Validation Needs**:
1. ✅ No additional codegen (uses Phase 4-5 artifacts)
2. ✅ Testing validates generated code works correctly

---

## Codegen Enhancement Plan

### Priority 1: Phase 4 Requirements (Essential)

**Task 1: FHIRPath Invariant Provider**
- **File**: `codegen/Ignixa.Specification.Generators/CSharpInvariantLanguage.cs` (NEW)
- **Output**: `src/Ignixa.Specification/Generated/R4InvariantProvider.g.cs`
- **Data Source**: `StructureDefinition.snapshot.element[].constraint[]`
- **Effort**: 1 week (includes testing)
- **Blocked By**: None
- **Blocks**: Phase 4 FhirPathInvariantAssertion implementation

**Task 2: Extension Definition Provider (Basic)**
- **File**: `codegen/Ignixa.Specification.Generators/CSharpExtensionLanguage.cs` (NEW)
- **Output**: `src/Ignixa.Specification/Generated/R4ExtensionProvider.g.cs`
- **Data Source**: FHIR package StructureDefinitions with `kind="complex-type"` and `type="Extension"`
- **Effort**: 3 days
- **Blocked By**: None
- **Blocks**: Phase 4 ExtensionAssertion implementation

---

### Priority 2: Phase 5 Requirements (High Priority)

**Task 3: Binding Provider**
- **File**: `codegen/Ignixa.Specification.Generators/CSharpBindingLanguage.cs` (NEW)
- **Output**: `src/Ignixa.Specification/Generated/R4BindingProvider.g.cs`
- **Data Source**: `StructureDefinition.snapshot.element[].binding`
- **Effort**: 4 days
- **Blocked By**: None
- **Blocks**: Phase 5 BindingAssertion implementation

**Task 4: Enable ValueSet Enum Generation**
- **File**: Update `generate.ps1` / `generate.sh` (MODIFY)
- **Output**: `src/Ignixa.Specification/Generated/R4ValueSets.g.cs`
- **Data Source**: FHIR package ValueSet resources
- **Effort**: 1 day (language exists, just enable it)
- **Blocked By**: None
- **Blocks**: Phase 5 static code validation

---

### Priority 3: Phase 5+ Enhancements (Optional)

**Task 5: ValueSet Expansion Provider (Optional)**
- **File**: `codegen/Ignixa.Specification.Generators/CSharpValueSetExpansionLanguage.cs` (NEW)
- **Output**: `src/Ignixa.Specification/Generated/R4ValueSetExpansions.g.cs`
- **Data Source**: FHIR package ValueSet expansions
- **Effort**: 1 week
- **Blocked By**: None
- **Blocks**: Offline terminology validation (optional)
- **Decision**: Defer until Phase 5 integration shows need

---

## Codegen Invocation Changes

### Current (Phase 3)

```bash
cd codegen
./generate.ps1  # Only generates StructureDefinitionProviders
```

### Proposed (Phase 4-6)

```bash
cd codegen
./generate.ps1 -Generators All  # Generate all providers

# OR selectively:
./generate.ps1 -Generators StructureProvider,Invariant,Extension
./generate.ps1 -Generators Binding,ValueSet
```

**Update Required**: Modify `generate.ps1` / `generate.sh` to support multiple generators

---

## Generated Code Size Estimates

| Artifact | Lines of Code | File Size | Build Time |
|----------|---------------|-----------|------------|
| **StructureDefinitionProvider** (current) | 59,318 | 3.5 MB | 11 sec |
| **InvariantProvider** (new) | ~5,000 | 300 KB | 2 sec |
| **ExtensionProvider** (new) | ~2,000 | 120 KB | 1 sec |
| **BindingProvider** (new) | ~15,000 | 900 KB | 3 sec |
| **ValueSet Enums** (new) | ~3,000 | 180 KB | 2 sec |
| **ValueSet Expansions** (optional) | ~10,000 | 600 KB | 2 sec |
| **TOTAL (Phase 4-5)** | ~94,318 | 5.6 MB | 21 sec |

**Impact**: Acceptable for development builds (regenerate rarely, cache compiled DLL)

---

## Alternative Approaches

### Option A: Codegen Everything (Recommended)

**Pros**:
- ✅ Fast runtime performance (no parsing)
- ✅ Type-safe (enums, strong typing)
- ✅ Predictable (no file I/O at runtime)

**Cons**:
- ❌ Larger assembly size (~5.6 MB)
- ❌ Longer initial codegen time (21 seconds)
- ❌ Regeneration required for spec updates

**Recommendation**: Use this approach (matches current StructureProvider pattern)

---

### Option B: Runtime Loading (Not Recommended)

**Pros**:
- ✅ Smaller assembly
- ✅ Dynamic updates without recompilation

**Cons**:
- ❌ Slower performance (parse JSON at runtime)
- ❌ File I/O overhead
- ❌ Harder to cache/optimize
- ❌ Deployment complexity (need to ship JSON files)

**Recommendation**: Avoid unless assembly size becomes critical (>10 MB)

---

## Risk Assessment

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Codegen breaks existing build** | High | Low | Separate generators, incremental rollout |
| **Generated code too large** | Medium | Low | Current 3.5 MB acceptable, 5.6 MB still reasonable |
| **FHIRPath expressions invalid** | Medium | Low | Test against known invariants, use SDK parser |
| **ValueSet enums conflict** | Low | Low | Namespace isolation, prefix with "FhirValueSet" |
| **Binding data incomplete** | Medium | Medium | Fall back to StructureDefinition lookup |

---

## Recommendations

### Immediate Actions (Phase 4 Prep)

1. **Implement CSharpInvariantLanguage** (1 week)
   - Generate `R4InvariantProvider.g.cs`
   - Test with ele-1, bdl-7 invariants
   - Integrate with StructureDefinitionSchemaBuilder

2. **Implement CSharpExtensionLanguage** (3 days)
   - Generate `R4ExtensionProvider.g.cs`
   - Cover base FHIR extensions only
   - Custom IG extensions deferred to runtime loading

3. **Update generate.ps1 / generate.sh** (1 day)
   - Add `-Generators` parameter
   - Support selective generation
   - Default to `All` for CI builds

### Phase 5 Actions

4. **Implement CSharpBindingLanguage** (4 days)
   - Generate `R4BindingProvider.g.cs`
   - Extract all binding metadata

5. **Enable ValueSet enum generation** (1 day)
   - Already implemented, just enable in scripts
   - Generate `R4ValueSets.g.cs`

6. **Test generated providers** (1 week)
   - Unit tests for each provider
   - Integration tests with validation system
   - Performance benchmarks

### Optional (Post-Phase 6)

7. **ValueSet expansion provider** (defer until needed)
   - Evaluate based on Phase 5 performance
   - Only implement if offline validation required

---

## Success Criteria

**Phase 4**:
- ✅ FHIRPath invariants available via `IInvariantProvider`
- ✅ Extension definitions available via `IExtensionProvider`
- ✅ Zero build errors with new generators
- ✅ Generated code compiles successfully
- ✅ <25 second total codegen time

**Phase 5**:
- ✅ Binding metadata available via `IBindingProvider`
- ✅ ValueSet enums generated and usable
- ✅ Binding strength validation works
- ✅ No runtime dependency on FHIR JSON files

**Phase 6**:
- ✅ All validation uses generated providers
- ✅ No StructureDefinition parsing at runtime (except custom profiles)
- ✅ Performance targets met (<25ms Tier 1, <200ms Tier 2, <1000ms Tier 3)

---

## Files to Create/Modify

### New Files (Codegen)

1. `codegen/Ignixa.Specification.Generators/CSharpInvariantLanguage.cs`
2. `codegen/Ignixa.Specification.Generators/CSharpExtensionLanguage.cs`
3. `codegen/Ignixa.Specification.Generators/CSharpBindingLanguage.cs`
4. `codegen/Ignixa.Specification.Generators/CSharpValueSetExpansionLanguage.cs` (optional)

### Modified Files (Codegen)

1. `codegen/generate.ps1` - Add multi-generator support
2. `codegen/generate.sh` - Add multi-generator support
3. `codegen/Ignixa.Specification.Generators/Program.cs` - Register new languages

### Generated Files (Output)

1. `src/Ignixa.Specification/Generated/R4InvariantProvider.g.cs`
2. `src/Ignixa.Specification/Generated/R4ExtensionProvider.g.cs`
3. `src/Ignixa.Specification/Generated/R4BindingProvider.g.cs`
4. `src/Ignixa.Specification/Generated/R4ValueSets.g.cs`
5. `src/Ignixa.Specification/Generated/R4ValueSetExpansions.g.cs` (optional)

### New Interfaces (Validation)

1. `src/Ignixa.Validation/Abstractions/IInvariantProvider.cs`
2. `src/Ignixa.Validation/Abstractions/IExtensionProvider.cs`
3. `src/Ignixa.Validation/Abstractions/IBindingProvider.cs`
4. `src/Ignixa.Validation/Abstractions/IValueSetExpansionProvider.cs` (optional)

---

## Conclusion

**YES ✅** - We need additional codegen for comprehensive validation:

| Generator | Phase | Priority | Effort |
|-----------|-------|----------|--------|
| **Invariant Provider** | Phase 4 | Essential | 1 week |
| **Extension Provider** | Phase 4 | Essential | 3 days |
| **Binding Provider** | Phase 5 | High | 4 days |
| **ValueSet Enums** | Phase 5 | High | 1 day |
| **ValueSet Expansions** | Phase 5+ | Optional | 1 week |

**Total Additional Codegen Effort**: ~2.5 weeks (excluding optional expansion provider)

**Recommendation**: Implement Invariant and Extension providers during Phase 4 prep week, Binding and ValueSet providers during Phase 5 prep week.

This aligns with the revised 27-week Phase 4-6 timeline and provides the necessary infrastructure for production-ready FHIR validation.
