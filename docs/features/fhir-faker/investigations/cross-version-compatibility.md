# Investigation: FHIR Faker Library Cross-Version Compatibility Analysis

**Feature**: fhir-faker
**Status**: Viable
**Created**: 2025-12-02

---


## Executive Summary

The Ignixa FhirFakes library generates synthetic FHIR resources for testing and development. While the core `SchemaBasedFhirResourceFaker` class correctly uses schema metadata for dynamic property generation, the scenario state classes (e.g., `MedicationOrderState`, `ObservationState`, `AllergyIntoleranceState`) hardcode property names assuming R4+ structures. This causes potential compatibility issues when generating resources for different FHIR versions (STU3, R4, R4B, R5, R6).

**Key Finding**: Only `ImmunizationState` currently uses version-aware property naming via `FhirVersionHelper`. Other state classes need similar treatment.

## Current Architecture

### Trusted Components (✅ Working Correctly)

#### SchemaBasedFhirResourceFaker
- **Location**: `src/Core/Ignixa.FhirFakes/SchemaBasedFhirResourceFaker.cs`
- **Approach**: Uses `IFhirSchemaProvider` to dynamically query:
  - Element types and cardinality
  - Terminology bindings
  - Required vs optional fields
  - Reference targets
- **Why it works**: Never assumes property names; always queries schema first

**Example** (lines 223-244):
```csharp
private JsonNode? GenerateElementValue(IType element, string parentResourceType)
{
    var elementName = element.Info.Name;  // Gets name from schema
    
    if (element.IsCollection)  // Checks cardinality from schema
    {
        // Generate array of items
    }
    
    return GenerateSingleValue(element, elementName, parentResourceType);
}
```

#### FhirVersionHelper
- **Location**: `src/Core/Ignixa.FhirFakes/FhirVersionHelper.cs`
- **Provides**:
  - Version detection: `IsStu3()`, `IsR4OrLater()`
  - Property existence checks: `HasProperty(resourceType, propertyName)`
  - Immunization-specific field names: `GetImmunizationProtocolFieldName()`, etc.

### Problematic Components (⚠️ Need Fixing)

#### State Classes
15+ state files in `src/Core/Ignixa.FhirFakes/Scenarios/States/` hardcode property names:
- `MedicationOrderState.cs`
- `ObservationState.cs`
- `AllergyIntoleranceState.cs`
- `ConditionOnsetState.cs`
- `EncounterState.cs`
- `ProcedureState.cs`
- `DiagnosticReportState.cs`
- `ServiceRequestState.cs`
- `CareTeamState.cs`
- `CarePlanState.cs`
- `GoalState.cs`
- `CoverageState.cs`
- `OrganizationState.cs`
- `PractitionerState.cs`
- `PatientBuilderState.cs`

**Common Pattern** (problematic):
```csharp
public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
{
    var resource = faker.Generate("ResourceType");
    var node = resource.MutableNode;
    
    node["specificPropertyName"] = value;  // ❌ Assumes property exists in all versions
    node["anotherProperty"] = value;       // ❌ May have different name in STU3/R5
}
```

## Documented Version-Specific Issues

### 1. MedicationRequest (MedicationOrderState.cs) - HIGH PRIORITY

**Current Implementation** (line 94):
```csharp
node["medicationCodeableConcept"] = new JsonObject
{
    ["coding"] = new JsonArray { ... }
};
```

**Problem**: 
- Uses `medicationCodeableConcept` explicitly
- FHIR defines `medication[x]` as a choice type that can be:
  - `medicationCodeableConcept` (inline code)
  - `medicationReference` (reference to Medication resource)
- Different profiles may require different choices
- STU3 vs R4+ may have different binding requirements

**Impact**: Generated resources may not validate against certain profiles or versions.

**Solution**:
```csharp
// Option 1: Query schema for available choice types
var medicationElement = faker.SchemaProvider.GetTypeDefinition("MedicationRequest")
    .Children.FirstOrDefault(c => c.Info.Name.StartsWith("medication"));
    
// Option 2: Add helper method to FhirVersionHelper
var medicationField = FhirVersionHelper.GetChoiceFieldName(
    faker.SchemaProvider, 
    "MedicationRequest", 
    "medication",
    preferredSuffix: "CodeableConcept"  // Falls back to Reference if needed
);
node[medicationField] = ...;
```

### 2. Immunization (ImmunizationState.cs) - ✅ GOLD STANDARD

**Implementation** (lines 225-242):
```csharp
// Set protocol applied (dose series tracking) - version-aware field naming
var protocolFieldName = faker.SchemaProvider.GetImmunizationProtocolFieldName();
node[protocolFieldName] = new JsonArray
{
    CreateProtocolApplied(faker.SchemaProvider)
};

private JsonObject CreateProtocolApplied(IFhirSchemaProvider schemaProvider)
{
    var protocol = new JsonObject();
    
    // Version-aware dose number field
    var doseNumberFieldName = schemaProvider.GetImmunizationDoseNumberFieldName();
    protocol[doseNumberFieldName] = DoseNumber;
    
    // Version-aware series doses field (not present in STU3)
    var seriesDosesFieldName = schemaProvider.GetImmunizationSeriesDosesFieldName();
    if (seriesDosesFieldName is not null && SeriesDosesRecommended.HasValue)
    {
        protocol[seriesDosesFieldName] = SeriesDosesRecommended.Value;
    }
    
    return protocol;
}
```

**Version Handling**:
- **STU3**: `vaccinationProtocol` with `doseSequence` (integer)
- **R4+**: `protocolApplied` with `doseNumberPositiveInt` and `seriesDosesPositiveInt`

**Why it works**:
1. Uses `FhirVersionHelper` to get correct field names
2. Checks for field existence before setting optional fields
3. Properly tested in `CrossVersionCompatibilityTests.cs`

**This is the pattern all other states should follow.**

### 3. AllergyIntolerance (AllergyIntoleranceState.cs) - MEDIUM PRIORITY

**Current Implementation** (lines 100-125):
```csharp
// Set clinical status
node["clinicalStatus"] = new JsonObject
{
    ["coding"] = new JsonArray { ... }
};

// Set verification status
node["verificationStatus"] = new JsonObject
{
    ["coding"] = new JsonArray { ... }
};
```

**Problem**:
- `clinicalStatus` and `verificationStatus` are **required in R4+** but **optional in STU3**
- Current code always sets them
- This works but may generate unnecessary data for STU3

**Evidence** from `CrossVersionCompatibilityTests.cs` (lines 390-398):
```csharp
allergy.MutableNode["patient"].Should().NotBeNull($"patient is required in {schema.Version}");

if (schema.Version != FhirVersion.Stu3)
{
    allergy.MutableNode["clinicalStatus"].Should().NotBeNull(...);
    allergy.MutableNode["verificationStatus"].Should().NotBeNull(...);
    allergy.MutableNode["code"].Should().NotBeNull(...);
}
```

**Solution**: Check schema requirements before setting:
```csharp
// Only set clinicalStatus if required or recommended
var clinicalStatusElement = typeDefinition.Children
    .FirstOrDefault(c => c.Info.Name == "clinicalStatus");
if (clinicalStatusElement?.IsRequired == true || faker.SchemaProvider.IsR4OrLater())
{
    node["clinicalStatus"] = ...;
}
```

### 4. ServiceRequest - CRITICAL ISSUE

**Problem**: 
- ServiceRequest was **introduced in R4**
- In STU3, this resource was called **`ProcedureRequest`**
- Complete resource rename between versions

**Current Handling**: 
- `CrossVersionCompatibilityTests.cs` correctly skips STU3 (lines 698-717)
- State class would fail if used with STU3 schema

**Solution**: 
- Document that ServiceRequest states are R4+ only
- Consider creating `ProcedureRequestState` for STU3 compatibility
- Or add version check in `ServiceRequestState.Execute()`:

```csharp
public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
{
    if (faker.SchemaProvider.IsStu3())
    {
        throw new NotSupportedException(
            "ServiceRequest is not available in STU3. Use ProcedureRequest instead.");
    }
    // ... rest of implementation
}
```

### 5. Observation (ObservationState.cs) - LOW PRIORITY

**Current State**: Mostly compatible across versions
- Core structure similar across STU3, R4, R5
- Component observations work similarly
- Main differences in terminology bindings (handled by SchemaBasedFhirResourceFaker)

**Potential Issues**:
- Value[x] choice types might benefit from schema-driven selection
- Category codes may have different value sets per version

**Risk Level**: LOW - works reasonably well but could be improved

## Recommended Solutions

### Option 1: Expand FhirVersionHelper (RECOMMENDED)

Build on the Immunization pattern by adding generic helper methods:

```csharp
public static class FhirVersionHelper
{
    // Existing methods...
    public static bool IsStu3(this IFhirSchemaProvider schemaProvider);
    public static bool IsR4OrLater(this IFhirSchemaProvider schemaProvider);
    public static bool HasProperty(this IFhirSchemaProvider schemaProvider, 
        string resourceType, string propertyName);
    
    // NEW: Generic choice type resolver
    /// <summary>
    /// Gets the correct field name for a choice type (e.g., medication[x], value[x]).
    /// Returns the first available choice from preferred suffixes.
    /// </summary>
    public static string? GetChoiceFieldName(
        this IFhirSchemaProvider schemaProvider,
        string resourceType,
        string basePropertyName,
        params string[] preferredSuffixes)
    {
        var typeDefinition = schemaProvider.GetTypeDefinition(resourceType);
        if (typeDefinition is null) return null;
        
        // Try each preferred suffix in order
        foreach (var suffix in preferredSuffixes)
        {
            var fieldName = $"{basePropertyName}{suffix}";
            if (typeDefinition.Children.Any(c => c.Info.Name == fieldName))
            {
                return fieldName;
            }
        }
        
        // Fall back to any field starting with basePropertyName
        return typeDefinition.Children
            .FirstOrDefault(c => c.Info.Name.StartsWith(basePropertyName))
            ?.Info.Name;
    }
    
    // NEW: Check if field is required in current version
    public static bool IsRequired(
        this IFhirSchemaProvider schemaProvider,
        string resourceType,
        string propertyName)
    {
        var typeDefinition = schemaProvider.GetTypeDefinition(resourceType);
        var element = typeDefinition?.Children
            .FirstOrDefault(c => c.Info.Name == propertyName);
        return element?.IsRequired ?? false;
    }
}
```

**Usage in MedicationOrderState**:
```csharp
// Get version-appropriate medication field
var medicationField = faker.SchemaProvider.GetChoiceFieldName(
    "MedicationRequest",
    "medication",
    "CodeableConcept",  // Preferred
    "Reference"         // Fallback
);

if (medicationField is not null)
{
    node[medicationField] = CreateMedicationValue(medicationField);
}
```

### Option 2: Schema-First Generation

Instead of building JSON manually, leverage `SchemaBasedFhirResourceFaker.Generate()` more:

```csharp
public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
{
    // Let faker generate base resource with schema compliance
    var medication = faker.Generate("MedicationRequest");
    var node = medication.MutableNode;
    
    // Override only what we need to control
    node["status"] = Status;
    node["intent"] = Intent;
    
    // For medication[x], let the faker's existing logic handle it
    // OR query schema to find the right field
    var medField = faker.SchemaProvider.GetChoiceFieldName("MedicationRequest", "medication", ...);
    if (medField is not null)
    {
        node[medField] = ...;
    }
    
    // Reference fields are safe
    node["subject"] = new JsonObject { ["reference"] = $"Patient/{context.Patient.Id}" };
}
```

**Pros**:
- Leverages existing schema-aware generation
- Reduces code duplication
- Automatically handles version differences for uncontrolled fields

**Cons**:
- Less explicit control over all fields
- May generate unwanted optional fields

### Option 3: Hybrid Approach (RECOMMENDED FOR IMPLEMENTATION)

Combine Option 1 and Option 2:

1. **Extend FhirVersionHelper** with generic utilities
2. **Update state classes** to use helpers for version-specific fields
3. **Keep explicit field setting** for controlled fields (status, codes, etc.)
4. **Use schema queries** for choice types and conditional fields

**Implementation Priority**:
1. ✅ **ImmunizationState** - Already done (reference implementation)
2. 🔴 **MedicationOrderState** - Uses choice types (medication[x])
3. 🟡 **AllergyIntoleranceState** - Has required field differences
4. 🟡 **ObservationState** - Heavy usage, minor differences
5. 🟢 **ConditionOnsetState** - Check for differences
6. 🟢 **DiagnosticReportState** - Check for differences
7. 🟢 **ProcedureState** - Check for differences
8. 🟢 **EncounterState** - Check for differences
9. 🟢 All remaining states

## Implementation Plan

### Phase 1: Extend FhirVersionHelper ⭐ PRIORITY

**Tasks**:
- [ ] Add `GetChoiceFieldName()` method
- [ ] Add `IsRequired()` method  
- [ ] Add resource-specific helpers as needed (following Immunization pattern)
- [ ] Add unit tests for new methods
- [ ] Document usage patterns

**Files to Modify**:
- `src/Core/Ignixa.FhirFakes/FhirVersionHelper.cs`

**Estimated Effort**: 2-4 hours

### Phase 2: Update High-Priority State Classes

#### 2.1 MedicationOrderState (HIGH - Choice Types)
- [ ] Use `GetChoiceFieldName()` for medication[x]
- [ ] Add version compatibility tests
- [ ] Test with STU3, R4, R4B, R5

**Files**:
- `src/Core/Ignixa.FhirFakes/Scenarios/States/MedicationOrderState.cs`
- `test/Ignixa.FhirFakes.Tests/CrossVersionCompatibilityTests.cs`

#### 2.2 AllergyIntoleranceState (MEDIUM - Required Fields)
- [ ] Check requirements before setting clinicalStatus/verificationStatus
- [ ] Add version compatibility tests
- [ ] Test with all versions

**Files**:
- `src/Core/Ignixa.FhirFakes/Scenarios/States/AllergyIntoleranceState.cs`
- `test/Ignixa.FhirFakes.Tests/CrossVersionCompatibilityTests.cs`

#### 2.3 ObservationState (MEDIUM - Heavy Usage)
- [ ] Review for version-specific issues
- [ ] Consider value[x] choice type handling
- [ ] Expand version compatibility tests

**Files**:
- `src/Core/Ignixa.FhirFakes/Scenarios/States/ObservationState.cs`
- `test/Ignixa.FhirFakes.Tests/CrossVersionCompatibilityTests.cs`

**Estimated Effort**: 6-8 hours total

### Phase 3: Update Remaining State Classes

For each remaining state:
1. Review for hardcoded property assumptions
2. Identify choice types (e.g., value[x], performed[x])
3. Identify version-specific required fields
4. Update to use schema helpers
5. Add cross-version tests

**Estimated Effort**: 1-2 hours per state × 12 states = 12-24 hours

### Phase 4: Comprehensive Testing

- [ ] Run all CrossVersionCompatibilityTests with STU3, R4, R4B, R5, R6
- [ ] Add matrix tests for updated states
- [ ] Validate generated resources against official FHIR validators
- [ ] Document any known limitations

**Estimated Effort**: 4-6 hours

### Phase 5: Documentation

- [ ] Update `README.md` with version support matrix
- [ ] Document FhirVersionHelper usage patterns
- [ ] Add migration guide for existing scenario code
- [ ] Document any version-specific limitations

**Estimated Effort**: 2-3 hours

**Total Estimated Effort**: 26-45 hours

## Test Coverage Analysis

### Current Coverage (CrossVersionCompatibilityTests.cs)

**Comprehensive** (Multiple test methods):
- ✅ **Immunization** - 10+ tests covering all field mappings
- ✅ **ServiceRequest** - 7 tests (R4+ only, correct exclusion of STU3)

**Basic** (1-3 tests):
- ⚠️ **DiagnosticReport** - Basic generation and required fields
- ⚠️ **AllergyIntolerance** - Basic generation and required fields
- ⚠️ **Procedure** - Basic generation and required fields

**Missing**:
- ❌ **MedicationRequest** - No version-specific tests
- ❌ **Observation** - No version-specific tests beyond basic
- ❌ **Condition** - No tests
- ❌ **Encounter** - No tests
- ❌ **CareTeam**, **CarePlan**, **Goal**, etc. - No tests

### Recommended Test Additions

For each state class, add:

```csharp
[Fact]
public void Given<Resource>_WhenGeneratedAcrossAllVersions_ThenAllSucceed()
{
    foreach (var schema in _schemaProviders)  // STU3, R4, R4B, R5, R6
    {
        // Generate and verify basic structure
    }
}

[Fact]
public void Given<Resource>_WhenGeneratedAcrossAllVersions_ThenHasRequiredFields()
{
    foreach (var schema in _schemaProviders)
    {
        // Verify version-specific required fields
    }
}

[Fact]
public void Given<Resource>WithChoiceType_WhenGeneratedAcrossAllVersions_ThenUsesCorrectFieldName()
{
    // Test choice type handling (e.g., medication[x], value[x])
}

[Fact]
public void Given<Resource>_WhenGeneratedWithSTU3_ThenUsesSTU3FieldNames()
{
    // Verify STU3-specific field names if applicable
}

[Fact]
public void Given<Resource>_WhenGeneratedWithR4_ThenUsesR4FieldNames()
{
    // Verify R4-specific field names if applicable
}
```

## Risks and Mitigations

### Risk 1: Breaking Existing Scenarios
**Risk**: Updates to state classes might break existing scenario code

**Mitigation**:
- Maintain backward compatibility where possible
- Add deprecation warnings for removed patterns
- Provide migration guide
- Run full test suite before/after changes

### Risk 2: Incomplete Schema Coverage
**Risk**: Some FHIR versions may have incomplete schema definitions

**Mitigation**:
- Test with official FHIR validators
- Document known schema gaps
- Fall back to R4 patterns when schema is unclear

### Risk 3: Performance Impact
**Risk**: Additional schema queries may slow generation

**Mitigation**:
- Cache schema query results where possible
- Profile before/after performance
- Optimize hot paths

## Success Criteria

1. **All state classes** query schema for version-specific properties
2. **No hardcoded property names** for version-varying fields
3. **100% of states** have cross-version compatibility tests
4. **All tests pass** for STU3, R4, R4B, R5, R6
5. **Documentation** clearly states version support
6. **Zero regression** in existing test suite

## Conclusion

The Ignixa FhirFakes library has a solid foundation with `SchemaBasedFhirResourceFaker` demonstrating the correct schema-driven approach. The `ImmunizationState` class serves as an excellent example of version-aware implementation.

**Key Recommendations**:

1. **Follow the Immunization pattern**: Use `FhirVersionHelper` for version-specific field names
2. **Expand FhirVersionHelper**: Add generic methods for choice types and requirement checking
3. **Prioritize high-risk states**: Start with MedicationRequest (choice types) and AllergyIntolerance (required fields)
4. **Test comprehensively**: Ensure all states work across all supported FHIR versions
5. **Document clearly**: Provide version support matrix and migration guide

**Estimated Total Effort**: 26-45 hours for complete implementation

**Expected Outcome**: A robust, version-agnostic faker library that generates valid FHIR resources for STU3, R4, R4B, R5, and R6 with confidence.
