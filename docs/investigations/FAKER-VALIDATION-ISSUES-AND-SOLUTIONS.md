# FHIR Faker Validation Issues & Solutions

## Executive Summary

The newly integrated validation library in the FhirFaker CLI has successfully identified **critical issues** in the faker state classes. This document proposes solutions for the discovered problems.

**Key Finding**: The validation uncovered exactly the issues documented in PR #97—hardcoded choice type field names causing multiple variants to be set simultaneously.

---

## Issues Discovered

### Issue 1: Choice Type Violations (CRITICAL)

**Problem**: Multiple observation and medication state classes generate choice[x] violations by setting multiple variants of the same choice type.

**Example Error**:
```
❌ Choice element 'effective[x]' can only have one type variant,
   but found multiple: effective, effectiveDateTime
```

**Affected State Classes** (from validation run):
- `ObservationState` - Multiple observations failing
- `BloodGlucoseState` - `effective[x]` violation
- Other observation-based states

**Root Cause** (from source analysis):
```csharp
// WRONG: Hardcoded multiple variants of same choice
node["effective"] = ...;  // Creates the bare field
node["effectiveDateTime"] = ...;  // Creates the choice variant
// Result: Both exist, violating FHIR constraint
```

**FHIR Rule**: Choice types (property[x]) can have **exactly ONE** variant present.

---

### Issue 2: Cross-Version Field Name Differences

**Problem**: State classes hardcode field names that differ across FHIR versions.

**Examples**:
- STU3: `vaccinationProtocol` vs R4+: `protocolApplied`
- STU3: `medicationOrder` vs R4: `MedicationRequest` (different resource!)
- Version-specific choice type preferences

**Current Status**: PR #97 partially addresses this for `MedicationOrderState` and `ImmunizationState`, but many states remain unupdated.

---

### Issue 3: Missing Required Fields (Version-Dependent)

**Problem**: Some fields are required in certain FHIR versions but optional in others.

**Example**: `AllergyIntolerance.clinicalStatus`
- STU3: Optional
- R4+: Required

**Current Status**: Faker may generate incomplete resources for STU3 or unnecessary fields for later versions.

---

## Proposed Solutions

### Solution 1: Leverage PR #97 Pattern (Immediate)

**Use the schema-driven approach already validated in PR #97:**

```csharp
// GOOD: Query schema for correct choice field name
var effectiveField = faker.SchemaProvider.GetChoiceFieldName(
    "Observation",
    "effective",
    "DateTime",     // Preferred
    "Period",       // Fallback 1
    "Timing",       // Fallback 2
    "Instant"       // Fallback 3
);

if (effectiveField is not null)
{
    node[effectiveField] = DateTimeOffset.UtcNow;
}
```

**Implementation Steps**:
1. ✅ **Already done in PR #97**: `GetChoiceFieldName()` helper method
2. **Apply to high-risk states** (6-8 hours):
   - `ObservationState` - Heavy usage
   - `ProcedureState` - Uses `performed[x]`
   - `DiagnosticReportState` - Likely has choice types
   - `ConditionState` - Check for `onset[x]`

3. **Add validation tests** to prevent regression:
   ```csharp
   [Fact]
   public void GivenObservationWithEffectiveDateTime_WhenGenerated_ThenOnlyOneEffectiveVariantExists()
   {
       var observation = GenerateBloodGlucoseObservation();

       var effectiveVariants = observation.MutableNode.AsObject()
           .Where(kvp => kvp.Key.StartsWith("effective"))
           .ToList();

       effectiveVariants.Should().HaveCounts(1);  // Exactly 1 variant
       observation.MutableNode.Should().NotContainKey("effective");  // No bare field
   }
   ```

---

### Solution 2: Implement Version-Aware State Helpers

**Create generic helpers for version-specific scenarios:**

```csharp
// In FhirVersionHelper.cs
public static class FhirVersionHelper
{
    // Existing method from PR #97
    public static string? GetChoiceFieldName(
        this IFhirSchemaProvider schemaProvider,
        string resourceType,
        string basePropertyName,
        params string[] preferredSuffixes);

    // NEW: Required fields by version
    public static bool IsFieldRequired(
        this IFhirSchemaProvider schemaProvider,
        string resourceType,
        string fieldName)
    {
        var typeDefinition = schemaProvider.GetTypeDefinition(resourceType);
        var element = typeDefinition?.Children
            .FirstOrDefault(c => c.Info.Name == fieldName);
        return element?.IsRequired ?? false;
    }

    // NEW: Resource rename mapping (STU3 → R4+)
    public static string? GetEquivalentResourceType(
        this IFhirSchemaProvider schemaProvider,
        string resourceType)
    {
        return (resourceType, schemaProvider.IsStu3()) switch
        {
            ("MedicationRequest", true) => "MedicationOrder",
            ("ServiceRequest", true) => "ProcedureRequest",
            ("Appointment", true) => "Schedule",  // Different model
            _ => resourceType
        };
    }
}
```

**Usage in State Classes**:
```csharp
public class AllergyIntoleranceState : IStateExecutor
{
    public void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        var resource = faker.Generate("AllergyIntolerance");
        var node = resource.MutableNode;

        // Only set required fields per version
        if (faker.SchemaProvider.IsFieldRequired("AllergyIntolerance", "clinicalStatus"))
        {
            node["clinicalStatus"] = new JsonObject { ... };
        }

        context.AddResource(resource);
    }
}
```

---

### Solution 3: Validation-Driven Test Harness

**Create a test that validates all state-generated resources:**

```csharp
// File: test/Ignixa.FhirFakes.Tests/StateValidationTests.cs
public class StateValidationTests
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly SchemaBasedFhirResourceFaker _faker;

    public StateValidationTests()
    {
        _schemaProvider = new R4CoreSchemaProvider();
        _faker = new SchemaBasedFhirResourceFaker(_schemaProvider);
    }

    [Theory]
    [MemberData(nameof(GetAllStates))]
    public void GivenAnyState_WhenExecuted_ThenGeneratedResourceIsValid(
        string stateName,
        IStateExecutor state)
    {
        // Arrange
        var context = new ScenarioContext();
        context.Patient = PatientBuilderFactory.Create(_schemaProvider).Build();

        // Act
        state.Execute(context, _faker);

        // Assert
        foreach (var resource in context.AllResources)
        {
            var validationResult = ValidationHelper.ValidateResource(
                resource.MutableNode,
                _schemaProvider);

            validationResult.IsValid.Should()
                .BeTrue($"State '{stateName}' generated invalid resource: " +
                        $"{string.Join(", ", validationResult.Issues.Select(i => i.Message))}");
        }
    }

    public static TheoryData<string, IStateExecutor> GetAllStates()
    {
        var data = new TheoryData<string, IStateExecutor>();

        // Populate with all available states
        foreach (var stateName in StateDiscovery.GetObservationStateNames())
        {
            var state = StateDiscovery.CreateObservationState(stateName);
            if (state != null)
                data.Add($"Observation.{stateName}", state);
        }

        // Add other resource states...

        return data;
    }
}
```

---

### Solution 4: Phased Rollout Plan

**Phase 1 (Immediate - 1 sprint)**:
- [ ] Apply PR #97 `GetChoiceFieldName()` to `ObservationState`
- [ ] Fix `effective[x]` choice type violations
- [ ] Add validation tests to prevent regression
- [ ] Estimated effort: **4 hours**

**Phase 2 (Next sprint)**:
- [ ] Apply to `ProcedureState` (`performed[x]`)
- [ ] Update `MedicationRequestState` if needed
- [ ] Add version-aware field requirement checks
- [ ] Estimated effort: **6 hours**

**Phase 3 (Following sprint)**:
- [ ] Remaining 10+ state classes
- [ ] Create comprehensive state validation test suite
- [ ] Document version-specific limitations
- [ ] Estimated effort: **12-16 hours**

---

## Validation Strategy

### Using CLI for Validation

**Enable validation during testing** (opt-in):

```bash
# Single resource with validation
ignixa-faker r4 resource Patient --out ./output --validate

# Scenario with validation report
ignixa-faker r4 scenario DiabeticPatient --out ./output --validate

# Test across versions
ignixa-faker stu3 resource Observation BloodGlucose --out ./output --validate
ignixa-faker r4 resource Observation BloodGlucose --out ./output --validate
ignixa-faker r5 resource Observation BloodGlucose --out ./output --validate
```

### CI/CD Integration

Add to test pipeline:

```yaml
# .github/workflows/validate-faker.yml
- name: Validate generated test data
  run: |
    for version in stu3 r4 r4b r5; do
      dotnet run --project tools/Ignixa.FhirFaker.Cli \
        -- $version resource Patient --out ./test-output --validate

      if [ $? -ne 0 ]; then
        echo "Validation failed for $version"
        exit 1
      fi
    done
```

---

## Impact Analysis

### Affected Components

| Component | Impact | Severity | Effort |
|-----------|--------|----------|--------|
| `ObservationState` | Choice type violations | Critical | 2-3 hrs |
| `ProcedureState` | Choice type violations | Critical | 2-3 hrs |
| `AllergyIntoleranceState` | Optional/required differences | Medium | 1-2 hrs |
| `MedicationRequestState` | Already addressed in PR #97 | - | 0 hrs |
| `ImmunizationState` | Already addressed (gold standard) | - | 0 hrs |
| Other states (10+) | Potential choice type issues | Medium | 10-12 hrs |

### Risk Level

**Low Risk**: Changes are localized to state classes and use proven patterns from PR #97

**Regression Protection**: Validation tests will immediately catch any regressions

---

## Success Criteria

- [ ] All state-generated resources pass validation for R4, R4B, R5
- [ ] All state-generated resources pass validation for STU3 (with documented version-specific limitations)
- [ ] No choice type violations (only 1 variant present)
- [ ] No missing required fields per version
- [ ] 100% test coverage for updated states
- [ ] CLI validation flag works correctly

---

## Recommendations

1. **Start with PR #97 pattern** - Don't reinvent; build on proven solution
2. **Validation is optional** - Keep `--validate` flag optional for performance
3. **Test-driven fixes** - Write validation test first, fix state class, verify
4. **Document limitations** - Clearly state which FHIR versions each state supports
5. **Monitor with validation** - Use validation regularly during development

---

## References

- **PR #97**: `Add schema-driven property resolution for cross-FHIR version compatibility`
- **ImmunizationState**: Gold standard implementation (see: `src/Core/Ignixa.FhirFakes/Scenarios/States/ImmunizationState.cs`)
- **FhirVersionHelper**: Schema-driven helpers (see: `src/Core/Ignixa.FhirFakes/FhirVersionHelper.cs`)
- **ValidationHelper**: CLI validation support (see: `tools/Ignixa.FhirFaker.Cli/ValidationHelper.cs`)

---

## Conclusion

The validation integration has **successfully identified real, fixable problems** in the faker library. By following the proven pattern from PR #97 and using validation as our guide, we can systematically fix these issues with high confidence and low risk.

The validation library is now a **quality gate** that ensures the faker generates truly valid FHIR resources across all supported versions.
