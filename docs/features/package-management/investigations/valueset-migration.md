# ValueSet Provider Migration Plan

## ADR: Migration of InMemoryTerminologyService ValueSet Data to Ignixa.Specification

**Date:** 2025-12-11
**Status:** Proposed
**Author:** Claude

---

## Executive Summary

This document outlines the plan to migrate valueset code generation from `Ignixa.Validation` to `Ignixa.Specification`, following the established pattern used by `IReferenceMetadataProvider`. This will:

1. Centralize valueset metadata in the schema provider layer
2. Eliminate duplicate hardcoded values in `BindingCodeMapper`
3. Enable consistent access to valueset codes across the application
4. Maintain backward compatibility with existing consumers

---

## Current State Analysis

### 1. InMemoryTerminologyService (Ignixa.Validation)

**Location:** `src/Core/Ignixa.Validation/Services/InMemoryTerminologyService.cs`

**Generated Files:**
- `src/Core/Ignixa.Validation/Generated/R4InMemoryTerminologyService.g.cs` (~789KB)
- `src/Core/Ignixa.Validation/Generated/R4BInMemoryTerminologyService.g.cs`
- `src/Core/Ignixa.Validation/Generated/R5InMemoryTerminologyService.g.cs`
- `src/Core/Ignixa.Validation/Generated/R6InMemoryTerminologyService.g.cs`
- `src/Core/Ignixa.Validation/Generated/STU3InMemoryTerminologyService.g.cs`

**Code Generator:** `codegen/Ignixa.Specification.Generators/CSharpInMemoryTerminologyLanguage.cs`

**Current Data Structure:**
```csharp
// Generated code stores: Dictionary<ValueSetUrl, HashSet<CodeValue>>
valueSets["http://hl7.org/fhir/ValueSet/administrative-gender"] =
    new HashSet<string>(["male", "female", "other", "unknown"], StringComparer.Ordinal);
```

**Issue:** Only stores code values, not the full code information (system, display).

### 2. BindingCodeMapper (Ignixa.FhirFakes)

**Location:** `src/Core/Ignixa.FhirFakes/BindingCodeMapper.cs`

**Current Data Structure:**
```csharp
// Hardcoded FhirCode arrays for each ValueSet
return normalizedUri switch
{
    "http://hl7.org/fhir/ValueSet/administrative-gender" =>
    [
        new FhirCode("http://hl7.org/fhir/administrative-gender", "male", "Male"),
        new FhirCode("http://hl7.org/fhir/administrative-gender", "female", "Female"),
        // ...
    ],
    // ... ~30 more valuesets
};
```

**Issue:** Duplicate hardcoded data, not version-aware, only covers ~30 valuesets.

### 3. IReferenceMetadataProvider Pattern (Reference Implementation)

**Interface:** `src/Core/Ignixa.Abstractions/IReferenceMetadataProvider.cs`
```csharp
public interface IReferenceMetadataProvider
{
    IReadOnlyList<ReferenceFieldMetadata> GetMetadata(string resourceType);
    bool HasReferences(string resourceType);
}
```

**Generated Implementation:** `src/Core/Ignixa.Specification/Generated/R4ReferenceMetadata.g.cs`

**Schema Provider Exposure:** `src/Core/Ignixa.Specification/Generated/R4CoreSchemaProvider.Partial.cs`
```csharp
public sealed partial class R4CoreSchemaProvider
{
    private readonly Lazy<IReferenceMetadataProvider> _referenceMetadataProvider
        = new(() => new R4ReferenceMetadata());

    public IReferenceMetadataProvider ReferenceMetadataProvider => _referenceMetadataProvider.Value;
}
```

**IFhirSchemaProvider Interface:** `src/Core/Ignixa.Specification/IFhirSchemaProvider.cs`
```csharp
public interface IFhirSchemaProvider : ISchema
{
    FhirVersion Version { get; }
    IReadOnlySet<string> ResourceTypeNames { get; }
    string FullVersion { get; }
    IReferenceMetadataProvider ReferenceMetadataProvider { get; }
}
```

---

## Proposed Design

### 1. New Interface: IValueSetProvider

**Location:** `src/Core/Ignixa.Abstractions/IValueSetProvider.cs`

```csharp
namespace Ignixa.Abstractions;

/// <summary>
/// Provides access to ValueSet code definitions for a specific FHIR version.
/// </summary>
public interface IValueSetProvider
{
    /// <summary>
    /// Gets the codes for a ValueSet by its canonical URL.
    /// </summary>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet (e.g., "http://hl7.org/fhir/ValueSet/administrative-gender").</param>
    /// <returns>A list of codes in the ValueSet, or null if the ValueSet is not known.</returns>
    IReadOnlyList<FhirCode>? GetCodes(string valueSetUrl);

    /// <summary>
    /// Checks if a ValueSet is known by this provider.
    /// </summary>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet.</param>
    /// <returns>True if the ValueSet is known; otherwise, false.</returns>
    bool IsKnownValueSet(string valueSetUrl);

    /// <summary>
    /// Validates whether a code is valid for a given ValueSet.
    /// </summary>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet.</param>
    /// <param name="code">The code to validate.</param>
    /// <returns>True if the code is valid for the ValueSet; false otherwise; null if ValueSet is unknown.</returns>
    bool? IsValidCode(string valueSetUrl, string code);
}
```

### 2. New Record: FhirCode

**Location:** `src/Core/Ignixa.Abstractions/FhirCode.cs`

```csharp
namespace Ignixa.Abstractions;

/// <summary>
/// Represents a FHIR code with system, code, and display values.
/// </summary>
/// <param name="System">The code system URI (e.g., "http://hl7.org/fhir/administrative-gender").</param>
/// <param name="Code">The code value (e.g., "male").</param>
/// <param name="Display">The human-readable display text (e.g., "Male").</param>
public readonly record struct FhirCode(string System, string Code, string Display);
```

**Note:** This is a new record in `Ignixa.Abstractions`. The existing `FhirCode` record in `Ignixa.FhirFakes.Scenarios.Codes` will be updated to use this abstraction.

### 3. Updated IFhirSchemaProvider

**Location:** `src/Core/Ignixa.Specification/IFhirSchemaProvider.cs`

```csharp
public interface IFhirSchemaProvider : ISchema
{
    FhirVersion Version { get; }
    IReadOnlySet<string> ResourceTypeNames { get; }
    string FullVersion { get; }
    IReferenceMetadataProvider ReferenceMetadataProvider { get; }
    IValueSetProvider ValueSetProvider { get; }  // NEW
}
```

### 4. Generated Implementations

**New Files to Generate:**
- `src/Core/Ignixa.Specification/Generated/R4ValueSetProvider.g.cs`
- `src/Core/Ignixa.Specification/Generated/R4BValueSetProvider.g.cs`
- `src/Core/Ignixa.Specification/Generated/R5ValueSetProvider.g.cs`
- `src/Core/Ignixa.Specification/Generated/R6ValueSetProvider.g.cs`
- `src/Core/Ignixa.Specification/Generated/STU3ValueSetProvider.g.cs`

**New Partial Files:**
- `src/Core/Ignixa.Specification/Generated/R4CoreSchemaProvider.ValueSet.g.cs`
- (Similar for other versions)

**Generated Structure:**
```csharp
namespace Ignixa.Specification.Generated;

/// <summary>
/// Pre-generated ValueSet provider for FHIR R4.
/// </summary>
public sealed class R4ValueSetProvider : IValueSetProvider
{
    private static readonly Dictionary<string, FhirCode[]> _valueSets = new(StringComparer.Ordinal)
    {
        ["http://hl7.org/fhir/ValueSet/administrative-gender"] =
        [
            new FhirCode("http://hl7.org/fhir/administrative-gender", "male", "Male"),
            new FhirCode("http://hl7.org/fhir/administrative-gender", "female", "Female"),
            new FhirCode("http://hl7.org/fhir/administrative-gender", "other", "Other"),
            new FhirCode("http://hl7.org/fhir/administrative-gender", "unknown", "Unknown"),
        ],
        // ... all valuesets from FHIR package
    };

    // Optimized lookup set for validation
    private static readonly Dictionary<string, HashSet<string>> _validationSets =
        new(StringComparer.Ordinal);

    static R4ValueSetProvider()
    {
        foreach (var (url, codes) in _valueSets)
        {
            _validationSets[url] = new HashSet<string>(
                codes.Select(c => c.Code),
                StringComparer.Ordinal);
        }
    }

    public IReadOnlyList<FhirCode>? GetCodes(string valueSetUrl)
    {
        var normalized = NormalizeUrl(valueSetUrl);
        return _valueSets.TryGetValue(normalized, out var codes) ? codes : null;
    }

    public bool IsKnownValueSet(string valueSetUrl)
    {
        return _valueSets.ContainsKey(NormalizeUrl(valueSetUrl));
    }

    public bool? IsValidCode(string valueSetUrl, string code)
    {
        var normalized = NormalizeUrl(valueSetUrl);
        if (!_validationSets.TryGetValue(normalized, out var validCodes))
            return null;
        return validCodes.Contains(code);
    }

    private static string NormalizeUrl(string url)
    {
        var pipeIndex = url.IndexOf('|', StringComparison.Ordinal);
        return pipeIndex > 0 ? url[..pipeIndex] : url;
    }
}
```

### 5. Updated Code Generator

**Modify:** `codegen/Ignixa.Specification.Generators/CSharpInMemoryTerminologyLanguage.cs`

**Changes:**
1. Rename to `CSharpValueSetProviderLanguage.cs`
2. Change output directory to `Ignixa.Specification/Generated`
3. Change namespace to `Ignixa.Specification.Generated`
4. Generate `IValueSetProvider` implementations instead of partial classes
5. Extract full code information (system, code, display) from ValueSet definitions
6. Generate schema provider partial class files

**New Mode in Program.cs:**
```csharp
case "valueset-provider":
    Console.WriteLine("Generating ValueSet provider code...");
    var vsProviderLanguage = new CSharpValueSetProviderLanguage();
    var vsProviderConfig = new CSharpValueSetProviderConfig
    {
        OutputDirectory = Path.GetFullPath(outputDir),
        Namespace = "Ignixa.Specification.Generated"
    };
    vsProviderLanguage.Export(vsProviderConfig, definitions);
    break;
```

---

## Migration Steps

### Phase 1: Create New Infrastructure (No Breaking Changes)

1. **Add FhirCode record to Ignixa.Abstractions**
   - Create `src/Core/Ignixa.Abstractions/FhirCode.cs`
   - Keep existing `Ignixa.FhirFakes.Scenarios.Codes.FhirCode` temporarily

2. **Add IValueSetProvider interface to Ignixa.Abstractions**
   - Create `src/Core/Ignixa.Abstractions/IValueSetProvider.cs`

3. **Create new code generator**
   - Create `codegen/Ignixa.Specification.Generators/CSharpValueSetProviderLanguage.cs`
   - Add new mode to `Program.cs`

4. **Generate version-specific providers**
   - Generate `R4ValueSetProvider.g.cs`, etc.
   - Generate schema provider partial files

5. **Update IFhirSchemaProvider**
   - Add `IValueSetProvider ValueSetProvider { get; }` property

### Phase 2: Update Consumers

6. **Update BindingCodeMapper**
   - Remove hardcoded valueset data
   - Add constructor that accepts `IValueSetProvider`
   - Delegate to provider for valueset lookups
   - Keep fallback for clinical terminology codes (Medications, Conditions, etc.)

7. **Update InMemoryTerminologyService**
   - Refactor to use `IValueSetProvider` for validation
   - Keep version-aware constructor
   - Simplify generated code (remove valueset data)

8. **Update FhirFakes FhirCode**
   - Create type alias or migration path from `Ignixa.FhirFakes.Scenarios.Codes.FhirCode` to `Ignixa.Abstractions.FhirCode`

### Phase 3: Cleanup

9. **Remove deprecated generated files**
   - Delete `src/Core/Ignixa.Validation/Generated/*InMemoryTerminologyService.g.cs`

10. **Update documentation**
    - Update CLAUDE.md with new provider usage
    - Document migration for external consumers

---

## File Changes Summary

### New Files

| File | Description |
|------|-------------|
| `src/Core/Ignixa.Abstractions/FhirCode.cs` | New record for code representation |
| `src/Core/Ignixa.Abstractions/IValueSetProvider.cs` | New interface |
| `codegen/Ignixa.Specification.Generators/CSharpValueSetProviderLanguage.cs` | New code generator |
| `src/Core/Ignixa.Specification/Generated/R4ValueSetProvider.g.cs` | Generated R4 implementation |
| `src/Core/Ignixa.Specification/Generated/R4BValueSetProvider.g.cs` | Generated R4B implementation |
| `src/Core/Ignixa.Specification/Generated/R5ValueSetProvider.g.cs` | Generated R5 implementation |
| `src/Core/Ignixa.Specification/Generated/R6ValueSetProvider.g.cs` | Generated R6 implementation |
| `src/Core/Ignixa.Specification/Generated/STU3ValueSetProvider.g.cs` | Generated STU3 implementation |
| `src/Core/Ignixa.Specification/Generated/R4CoreSchemaProvider.ValueSet.g.cs` | Partial class exposing provider |
| (similar for other versions) | |

### Modified Files

| File | Changes |
|------|---------|
| `src/Core/Ignixa.Specification/IFhirSchemaProvider.cs` | Add `IValueSetProvider ValueSetProvider` property |
| `src/Core/Ignixa.FhirFakes/BindingCodeMapper.cs` | Use `IValueSetProvider`, remove hardcoded data |
| `src/Core/Ignixa.Validation/Services/InMemoryTerminologyService.cs` | Use `IValueSetProvider` |
| `codegen/Ignixa.Specification.Generators/Program.cs` | Add new generation mode |
| `src/Core/Ignixa.FhirFakes/Scenarios/Codes/FhirCode.cs` | Possibly migrate to abstraction |

### Files to Delete (Phase 3)

| File | Reason |
|------|--------|
| `src/Core/Ignixa.Validation/Generated/R4InMemoryTerminologyService.g.cs` | Replaced by ValueSetProvider |
| `src/Core/Ignixa.Validation/Generated/R4BInMemoryTerminologyService.g.cs` | Replaced by ValueSetProvider |
| `src/Core/Ignixa.Validation/Generated/R5InMemoryTerminologyService.g.cs` | Replaced by ValueSetProvider |
| `src/Core/Ignixa.Validation/Generated/R6InMemoryTerminologyService.g.cs` | Replaced by ValueSetProvider |
| `src/Core/Ignixa.Validation/Generated/STU3InMemoryTerminologyService.g.cs` | Replaced by ValueSetProvider |
| `codegen/Ignixa.Specification.Generators/CSharpInMemoryTerminologyLanguage.cs` | Replaced by new generator |

---

## Considerations

### 1. Generated File Size

The current R4 terminology file is ~789KB. The new format with full FhirCode data will be larger. Consider:
- Using string constants for common system URLs
- Lazy loading for rarely-used valuesets
- Splitting into multiple files by category

### 2. Clinical Terminology Codes

`BindingCodeMapper` has hardcoded clinical codes (Medications, Conditions, etc.) that come from external code systems (RxNorm, SNOMED CT, LOINC). These are NOT in the FHIR package valuesets. Options:
- Keep these as separate static data in `Ignixa.FhirFakes`
- Create a separate `IClinicalCodeProvider` for scenario-specific codes
- **Recommended:** Keep the existing `Scenarios/Codes/*.cs` files for clinical data generation

### 3. Memory Usage

All valuesets are currently loaded at startup. Consider:
- Lazy initialization per valueset
- LRU cache for infrequently accessed valuesets
- Profile-based loading (only load US Core valuesets, etc.)

### 4. Backward Compatibility

The `InMemoryTerminologyService` API should remain stable. Internal refactoring to use `IValueSetProvider` should be transparent to consumers.

---

## Testing Plan

1. **Unit Tests for IValueSetProvider**
   - Test `GetCodes` returns correct data
   - Test `IsKnownValueSet` for known and unknown valuesets
   - Test `IsValidCode` for valid, invalid, and unknown scenarios
   - Test URL normalization (version suffix removal)

2. **Integration Tests**
   - Verify `IFhirSchemaProvider.ValueSetProvider` is accessible
   - Verify lazy initialization works correctly
   - Verify all FHIR versions return appropriate data

3. **Regression Tests**
   - Ensure `InMemoryTerminologyService` behavior is unchanged
   - Ensure `BindingCodeMapper` behavior is unchanged
   - Verify fake resource generation produces valid codes

4. **Performance Tests**
   - Measure startup time impact
   - Measure memory usage
   - Measure lookup performance

---

## Timeline Estimate

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| Phase 1: Infrastructure | 2-3 days | None |
| Phase 2: Consumer Updates | 2-3 days | Phase 1 |
| Phase 3: Cleanup | 1 day | Phase 2, Validation |
| Testing & Documentation | 1-2 days | All phases |
| **Total** | **6-9 days** | |

---

## Decision Required

- Confirm the approach of centralizing valueset data in `Ignixa.Specification`
- Confirm the FhirCode record should include System, Code, and Display
- Decide whether to keep clinical codes separate in `Ignixa.FhirFakes`
- Decide on lazy loading strategy for memory optimization

---

## Appendix: Code Samples

### A. Current BindingCodeMapper Usage

```csharp
// Current usage in SchemaBasedFhirResourceFaker
if (BindingCodeMapper.TryGetCodesForValueSet(binding.ValueSet, out var codes))
{
    var code = _faker.PickRandom(codes);
    // Use code.System, code.Code, code.Display
}
```

### B. Proposed IValueSetProvider Usage

```csharp
// New usage with schema provider
var valueSetProvider = schemaProvider.ValueSetProvider;
var codes = valueSetProvider.GetCodes(binding.ValueSet);
if (codes != null)
{
    var code = _faker.PickRandom(codes);
    // Use code.System, code.Code, code.Display
}
```

### C. Validation Usage

```csharp
// New usage in InMemoryTerminologyService
var isValid = _valueSetProvider.IsValidCode(valueSetUrl, code);
if (isValid == null)
{
    // Unknown valueset - return warning
}
else if (isValid == false)
{
    // Invalid code - return error
}
```
