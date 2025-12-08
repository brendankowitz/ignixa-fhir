# Version-Aware Field Name Override Pattern

## Overview

The `VersionFieldOverrides` helper provides a clean, maintainable way to handle FHIR field name differences across versions without schema introspection. Uses R4+ normative field names by default, with STU3 overrides only where they differ.

## Motivation

- **Simple**: No runtime schema queries or complex logic
- **Fast**: Single dictionary lookup per field access
- **Explicit**: Version differences documented in one place
- **Maintainable**: Easy to add new overrides as needed
- **Self-Documenting**: Source code shows normative field name

## Architecture

```
R4+ Normative Field Name
         ↓
VersionFieldOverrides.GetFieldName()
         ↓
   Check Overrides?
    /            \
  YES: Use Override
   \            /
    NO: Use Normative
         ↓
   Return Field Name
```

## Usage Pattern

In any State class that needs version-aware field names:

```csharp
public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
{
    var node = observation.MutableNode;

    // Get version-appropriate field name
    // Shows what the R4+ normative name is in the code
    var effectiveField = VersionFieldOverrides.GetFieldName(
        faker.SchemaProvider.Version,
        "Observation",
        "effectiveDateTime");  // ← R4+ normative

    node[effectiveField] = context.CurrentTime.ToString("o");
}
```

## Adding Overrides

When you discover a field name difference across FHIR versions:

1. **Update `VersionFieldOverrides.cs`**:
```csharp
private static readonly Dictionary<(FhirVersion, string, string), string> Overrides = new()
{
    // Format: (Version, ResourceType, NormativeFieldName) -> ActualFieldName
    (FhirVersion.Stu3, "MyResource", "myField") -> "stu3MyField"
};
```

2. **Use in State class**:
```csharp
var fieldName = VersionFieldOverrides.GetFieldName(
    faker.SchemaProvider.Version,
    "MyResource",
    "myField");  // This is the R4+ normative name
```

## Current Overrides

Currently, the overrides dictionary is mostly empty because most field names are the same across STU3 and R4+:

| Resource | Field | STU3 | R4+ | Override |
|----------|-------|------|-----|----------|
| Observation | effective | effectiveDateTime | effectiveDateTime | None |
| Observation | value | valueQuantity | valueQuantity | None |
| Procedure | performed | performedDateTime | performedDateTime | None |

## Real Examples from Codebase

### ObservationState

**Before (Hardcoded)**:
```csharp
node["effectiveDateTime"] = context.CurrentTime.ToString("o");
node["valueQuantity"] = new JsonObject { ... };
```

**After (Version-Aware)**:
```csharp
var effectiveField = VersionFieldOverrides.GetFieldName(
    faker.SchemaProvider.Version,
    "Observation",
    "effectiveDateTime");
node[effectiveField] = context.CurrentTime.ToString("o");

var valueField = VersionFieldOverrides.GetFieldName(
    faker.SchemaProvider.Version,
    "Observation",
    "valueQuantity");
node[valueField] = new JsonObject { ... };
```

## State Classes to Update

These state classes should be updated to use `VersionFieldOverrides`:

1. **ProcedureState** - uses `performed[x]` choice type
2. **DiagnosticReportState** - check for version-specific fields
3. **ConditionState** - check for `onset[x]`
4. **AllergyIntoleranceState** - required field differences
5. **MedicationOrderState** - already partially updated, verify
6. **ImmunizationState** - check for `protocolApplied` vs `vaccinationProtocol`
7. Other state classes as validation discovers issues

## Benefits

✅ **No Schema Lookups**: Plain dictionary lookups at generation time
✅ **Transparent**: R4+ normative shown in code comments
✅ **Explicit**: Overrides clearly defined in one file
✅ **Testable**: Easy to unit test override logic
✅ **Performant**: Zero overhead for versions without overrides
✅ **Maintainable**: Adding new versions just requires adding entries to Overrides dict

## Testing Strategy

When updating a state class:

1. Ensure tests pass for all versions (STU3, R4, R4B, R5, R6)
2. Run validation on generated resources:
   ```bash
   ignixa-faker stu3 resource Observation BloodGlucose --validate
   ignixa-faker r4 resource Observation BloodGlucose --validate
   ignixa-faker r5 resource Observation BloodGlucose --validate
   ```
3. Verify no additional choice type violations introduced

## Future Enhancements

If more complex version handling is needed:

- Add version-dependent field **types** (not just names)
- Add required field checking per version
- Create a version compatibility matrix documentation
- Generate C# from FHIR specs automatically

For now, the simple override pattern handles the most common cases cleanly.
