# FhirFakes Enhancement Proposals

**Date**: 2025-12-11  
**Context**: E2E Test Gap Analysis  
**Related**: `e2e-test-gap-analysis.md`

## Overview

This document proposes enhancements to the `Ignixa.FhirFakes` library to support comprehensive E2E testing based on fhir-candle test patterns. All proposals maintain the existing builder/state pattern and are backward compatible.

---

## Proposal 1: PatientBuilder - MultipleBirth Support

### Current State
PatientBuilder doesn't support the `multipleBirth[x]` field (can be boolean or integer).

### Use Case
Test number searches with comparison operators:
```
GET /Patient?multiplebirth=3
GET /Patient?multiplebirth=le3
GET /Patient?multiplebirth=lt3
```

### Proposed API

```csharp
public class PatientBuilder
{
    private int? _multipleBirthInteger;
    private bool? _multipleBirthBoolean;
    
    /// <summary>
    /// Sets multipleBirthInteger to indicate birth order in multiple birth.
    /// </summary>
    public PatientBuilder WithMultipleBirth(int order)
    {
        if (order < 1)
            throw new ArgumentException("Birth order must be positive", nameof(order));
            
        _multipleBirthInteger = order;
        _multipleBirthBoolean = null; // Clear boolean variant
        return this;
    }
    
    /// <summary>
    /// Sets multipleBirthBoolean to indicate if patient is part of multiple birth.
    /// </summary>
    public PatientBuilder WithMultipleBirth(bool isMultipleBirth)
    {
        _multipleBirthBoolean = isMultipleBirth;
        _multipleBirthInteger = null; // Clear integer variant
        return this;
    }
    
    // In Build():
    private void ApplyMultipleBirth(JsonObject patient)
    {
        if (_multipleBirthInteger.HasValue)
        {
            patient["multipleBirthInteger"] = _multipleBirthInteger.Value;
        }
        else if (_multipleBirthBoolean.HasValue)
        {
            patient["multipleBirthBoolean"] = _multipleBirthBoolean.Value;
        }
    }
}
```

### Test Usage

```csharp
[Fact]
public async Task GivenMultipleBirthPatients_WhenSearchedWithComparison_ThenReturnsMatching()
{
    var tag = Guid.NewGuid().ToString();
    
    var triplet1 = CreatePatient().WithMultipleBirth(1).WithTag(tag).Build();
    var triplet2 = CreatePatient().WithMultipleBirth(2).WithTag(tag).Build();
    var triplet3 = CreatePatient().WithMultipleBirth(3).WithTag(tag).Build();
    var singleton = CreatePatient().WithMultipleBirth(false).WithTag(tag).Build();
    
    await Harness.CreateResourcesAsync([triplet1, triplet2, triplet3, singleton]);
    
    // Test exact match
    var results = await Harness.SearchAsync("Patient", $"_tag={tag}&multiplebirth=3");
    results.Should().HaveCount(1);
    
    // Test less-than-or-equal
    var results2 = await Harness.SearchAsync("Patient", $"_tag={tag}&multiplebirth=le3");
    results2.Should().HaveCount(3);
}
```

### Effort
Low (1-2 hours) - Simple field addition

---

## Proposal 2: PatientBuilder - BirthDate Precision Control

### Current State
PatientBuilder generates full date (year-month-day) for birthDate. FHIR supports partial dates with varying precision.

### Use Case
Test date searches with different precision levels:
```
GET /Patient?birthdate=1982        (year only, should match 1982-01-01 to 1982-12-31)
GET /Patient?birthdate=1982-01     (month precision)
GET /Patient?birthdate=1982-01-23  (day precision)
```

### Proposed API

```csharp
public class PatientBuilder
{
    private int? _birthYear;
    private int? _birthMonth;
    private int? _birthDay;
    
    /// <summary>
    /// Sets birth date with year precision only (e.g., "1982").
    /// </summary>
    public PatientBuilder WithBirthDate(int year)
    {
        ValidateYear(year);
        _birthYear = year;
        _birthMonth = null;
        _birthDay = null;
        _age = CalculateAge(year);
        return this;
    }
    
    /// <summary>
    /// Sets birth date with month precision (e.g., "1982-01").
    /// </summary>
    public PatientBuilder WithBirthDate(int year, int month)
    {
        ValidateYear(year);
        ValidateMonth(month);
        _birthYear = year;
        _birthMonth = month;
        _birthDay = null;
        _age = CalculateAge(year, month);
        return this;
    }
    
    /// <summary>
    /// Sets birth date with day precision (e.g., "1982-01-23").
    /// Existing method - no change needed.
    /// </summary>
    public PatientBuilder WithBirthDate(int year, int month, int day)
    {
        // Existing implementation
    }
    
    // In Build():
    private void ApplyBirthDate(JsonObject patient)
    {
        if (_birthYear.HasValue)
        {
            if (_birthDay.HasValue)
            {
                patient["birthDate"] = $"{_birthYear:D4}-{_birthMonth:D2}-{_birthDay:D2}";
            }
            else if (_birthMonth.HasValue)
            {
                patient["birthDate"] = $"{_birthYear:D4}-{_birthMonth:D2}";
            }
            else
            {
                patient["birthDate"] = $"{_birthYear:D4}";
            }
        }
    }
}
```

### Test Usage

```csharp
[Theory]
[InlineData(1982, 2)]           // Year-only search matches 2 patients
[InlineData(1982, 1, 1)]        // Month precision matches 1 patient
[InlineData(1982, 1, 23, 1)]    // Day precision matches 1 patient
public async Task GivenPatientsWithVariedBirthDates_WhenSearchedWithPrecision_ThenReturnsMatching(
    int year, int? month, int? day, int expectedCount)
{
    var tag = Guid.NewGuid().ToString();
    
    var patient1 = CreatePatient()
        .WithBirthDate(1982, 1, 23)
        .WithTag(tag)
        .Build();
    var patient2 = CreatePatient()
        .WithBirthDate(1982, 6, 15)
        .WithTag(tag)
        .Build();
    var patient3 = CreatePatient()
        .WithBirthDate(1990)
        .WithTag(tag)
        .Build();
        
    await Harness.CreateResourcesAsync([patient1, patient2, patient3]);
    
    var searchParam = month.HasValue 
        ? (day.HasValue ? $"birthdate={year:D4}-{month:D2}-{day:D2}" : $"birthdate={year:D4}-{month:D2}")
        : $"birthdate={year:D4}";
        
    var results = await Harness.SearchAsync("Patient", $"_tag={tag}&{searchParam}");
    results.Should().HaveCount(expectedCount);
}
```

### Effort
Low (1-2 hours) - Simple overload addition

---

## Proposal 3: PatientBuilder - Explicit Field Omission

### Current State
PatientBuilder generates all optional fields. Some tests require explicitly missing fields to test `:missing` modifier.

### Use Case
Test :missing modifier:
```
GET /Patient?active:missing=true   (should return patients without active field)
GET /Patient?active:missing=false  (should return patients with active field)
```

### Proposed API

```csharp
public class PatientBuilder
{
    private bool? _active = true; // Default: include active=true
    private bool _includeActive = true; // New: control inclusion
    
    /// <summary>
    /// Sets the active flag value (default: true).
    /// </summary>
    public PatientBuilder WithActive(bool active)
    {
        _active = active;
        _includeActive = true;
        return this;
    }
    
    /// <summary>
    /// Explicitly omits the active field from the patient resource.
    /// Useful for testing :missing modifier.
    /// </summary>
    public PatientBuilder WithoutActive()
    {
        _includeActive = false;
        return this;
    }
    
    // Similarly for other optional fields
    public PatientBuilder WithoutTelecom() { ... }
    public PatientBuilder WithoutAddress() { ... }
    
    // In Build():
    private void ApplyActive(JsonObject patient)
    {
        if (_includeActive && _active.HasValue)
        {
            patient["active"] = _active.Value;
        }
        // If !_includeActive, field is omitted
    }
}
```

### Test Usage

```csharp
[Fact]
public async Task GivenPatientsWithAndWithoutActive_WhenSearchedWithMissing_ThenReturnsCorrectSubset()
{
    var tag = Guid.NewGuid().ToString();
    
    var withActive = CreatePatient().WithActive(true).WithTag(tag).Build();
    var withoutActive = CreatePatient().WithoutActive().WithTag(tag).Build();
    
    await Harness.CreateResourcesAsync([withActive, withoutActive]);
    
    // Search for patients WITHOUT active field
    var missing = await Harness.SearchAsync("Patient", $"_tag={tag}&active:missing=true");
    missing.Should().HaveCount(1);
    missing[0].Id.Should().Be(withoutActive.Id);
    
    // Search for patients WITH active field
    var present = await Harness.SearchAsync("Patient", $"_tag={tag}&active:missing=false");
    present.Should().HaveCount(1);
    present[0].Id.Should().Be(withActive.Id);
}
```

### Effort
Medium (2-4 hours) - Requires refactoring field generation logic

---

## Proposal 4: ResourceBuilder Base - Profile Metadata

### Current State
PatientBuilder has `WithProfile(IPatientProfile)` for profile-specific data generation, but doesn't set `meta.profile` in the resource.

### Use Case
Test _profile searches:
```
GET /Observation?_profile=http://hl7.org/fhir/StructureDefinition/vitalsigns
GET /Observation?_profile:missing=true
GET /Observation?_profile:missing=false
```

### Proposed API

```csharp
public class PatientBuilder
{
    private readonly List<string> _profileUrls = [];
    
    /// <summary>
    /// Adds a profile URL to meta.profile (can be called multiple times).
    /// </summary>
    public PatientBuilder WithProfileUri(string profileUrl)
    {
        if (string.IsNullOrWhiteSpace(profileUrl))
            throw new ArgumentException("Profile URL cannot be empty", nameof(profileUrl));
            
        _profileUrls.Add(profileUrl);
        return this;
    }
    
    // In Build():
    private void ApplyProfiles(JsonObject patient)
    {
        if (_profileUrls.Count > 0)
        {
            var meta = patient["meta"] as JsonObject ?? new JsonObject();
            meta["profile"] = new JsonArray(_profileUrls.Select(u => JsonValue.Create(u)).ToArray());
            patient["meta"] = meta;
        }
    }
}
```

### Alternative: ResourceBuilder<T> Base Class

```csharp
public abstract class ResourceBuilder<T> where T : ResourceBuilder<T>
{
    protected readonly List<string> _profileUrls = [];
    
    public T WithProfile(string profileUrl)
    {
        _profileUrls.Add(profileUrl);
        return (T)this;
    }
    
    protected void ApplyProfiles(JsonObject resource)
    {
        if (_profileUrls.Count > 0)
        {
            var meta = resource["meta"] as JsonObject ?? new JsonObject();
            meta["profile"] = new JsonArray(_profileUrls.Select(u => JsonValue.Create(u)).ToArray());
            resource["meta"] = meta;
        }
    }
}

// PatientBuilder inherits from ResourceBuilder<PatientBuilder>
public class PatientBuilder : ResourceBuilder<PatientBuilder>
{
    // ... existing code ...
    
    public JsonObject Build()
    {
        var patient = _schemaProvider.Generate("Patient");
        // ... existing field setup ...
        ApplyProfiles(patient); // Add profiles
        return patient;
    }
}
```

### Test Usage

```csharp
[Fact]
public async Task GivenObservationsWithProfiles_WhenSearchedByProfile_ThenReturnsMatching()
{
    var tag = Guid.NewGuid().ToString();
    
    var vitalSign = CreateObservation()
        .WithCode("85354-9", "http://loinc.org", "Blood pressure")
        .WithProfileUri("http://hl7.org/fhir/StructureDefinition/vitalsigns")
        .WithTag(tag)
        .Build();
        
    var labResult = CreateObservation()
        .WithCode("2345-7", "http://loinc.org", "Glucose")
        .WithTag(tag)
        .Build();
        
    await Harness.CreateResourcesAsync([vitalSign, labResult]);
    
    var results = await Harness.SearchAsync(
        "Observation", 
        $"_tag={tag}&_profile=http://hl7.org/fhir/StructureDefinition/vitalsigns");
        
    results.Should().HaveCount(1);
    results[0].Id.Should().Be(vitalSign.Id);
}
```

### Effort
Medium (3-4 hours) - Affects all resource builders if base class approach

---

## Proposal 5: ObservationBuilder - Quantity and Composite Support

### Current State
`ObservationState` has `Value`, `Unit`, `UnitCode` properties but no convenient builder API for creating test observations with quantities.

### Use Case
Test quantity searches:
```
GET /Observation?value-quantity=185
GET /Observation?value-quantity=ge185
GET /Observation?value-quantity=185|http://unitsofmeasure.org|[lb_av]
GET /Observation?code-value-quantity=http://loinc.org|29463-7$185|http://unitsofmeasure.org|[lb_av]
```

### Proposed API

Create a new `ObservationBuilder` class similar to `PatientBuilder`:

```csharp
public class ObservationBuilder
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private string? _id;
    private string? _tag;
    private string _status = "final";
    
    // Code
    private string? _codeCode;
    private string? _codeSystem;
    private string? _codeDisplay;
    
    // Value
    private decimal? _valueQuantity;
    private string? _valueUnit;
    private string? _valueSystem = "http://unitsofmeasure.org";
    
    // Subject
    private string? _subjectReference;
    
    public ObservationBuilder(IFhirSchemaProvider schemaProvider)
    {
        _schemaProvider = schemaProvider;
    }
    
    public ObservationBuilder WithCode(string code, string system, string? display = null)
    {
        _codeCode = code;
        _codeSystem = system;
        _codeDisplay = display;
        return this;
    }
    
    public ObservationBuilder WithQuantityValue(decimal value, string unit, string? system = null)
    {
        _valueQuantity = value;
        _valueUnit = unit;
        _valueSystem = system ?? "http://unitsofmeasure.org";
        return this;
    }
    
    public ObservationBuilder WithSubject(string patientId)
    {
        _subjectReference = $"Patient/{patientId}";
        return this;
    }
    
    public ObservationBuilder WithTag(string tag)
    {
        _tag = tag;
        return this;
    }
    
    public JsonObject Build()
    {
        var obs = _schemaProvider.Generate("Observation");
        
        obs["id"] = _id ?? Guid.NewGuid().ToString();
        obs["status"] = _status;
        
        if (_codeCode != null)
        {
            obs["code"] = new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = _codeSystem,
                        ["code"] = _codeCode,
                        ["display"] = _codeDisplay ?? _codeCode
                    }
                }
            };
        }
        
        if (_valueQuantity.HasValue)
        {
            obs["valueQuantity"] = new JsonObject
            {
                ["value"] = _valueQuantity.Value,
                ["unit"] = _valueUnit,
                ["system"] = _valueSystem,
                ["code"] = _valueUnit
            };
        }
        
        if (_subjectReference != null)
        {
            obs["subject"] = new JsonObject { ["reference"] = _subjectReference };
        }
        
        if (_tag != null)
        {
            obs["meta"] = new JsonObject
            {
                ["tag"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
                        ["code"] = _tag
                    }
                }
            };
        }
        
        return obs;
    }
}

// Factory
public static class ObservationBuilderFactory
{
    public static ObservationBuilder Create(IFhirSchemaProvider schemaProvider)
        => new(schemaProvider);
}
```

### Test Usage

```csharp
[Theory]
[InlineData("value-quantity=185", 1)]
[InlineData("value-quantity=ge185", 2)]
[InlineData("value-quantity=gt185", 1)]
[InlineData("value-quantity=le185", 1)]
[InlineData("value-quantity=lt185", 0)]
public async Task GivenObservationsWithQuantities_WhenSearchedWithComparisons_ThenReturnsMatching(
    string searchParam, int expectedCount)
{
    var tag = Guid.NewGuid().ToString();
    var patient = CreatePatient().WithTag(tag).Build();
    await Harness.CreateResourceAsync(patient);
    
    var obs1 = ObservationBuilderFactory.Create(SchemaProvider)
        .WithCode("29463-7", "http://loinc.org", "Body Weight")
        .WithQuantityValue(185, "[lb_av]")
        .WithSubject(patient.Id)
        .WithTag(tag)
        .Build();
        
    var obs2 = ObservationBuilderFactory.Create(SchemaProvider)
        .WithCode("29463-7", "http://loinc.org", "Body Weight")
        .WithQuantityValue(190, "[lb_av]")
        .WithSubject(patient.Id)
        .WithTag(tag)
        .Build();
        
    await Harness.CreateResourcesAsync([obs1, obs2]);
    
    var results = await Harness.SearchAsync("Observation", $"_tag={tag}&{searchParam}");
    results.Should().HaveCount(expectedCount);
}
```

### Effort
Medium-High (4-6 hours) - New builder class with comprehensive support

---

## Proposal 6: Identifier Builder Helper

### Current State
No convenient way to add identifiers with type coding (for `identifier:of-type` modifier).

### Use Case
```
GET /Patient?identifier:of-type=http://terminology.hl7.org/CodeSystem/v2-0203|MR|12345
```

### Proposed API

```csharp
public class PatientBuilder
{
    private readonly List<(string System, string Value, string? TypeSystem, string? TypeCode)> _identifiers = [];
    
    public PatientBuilder WithIdentifier(
        string system, 
        string value, 
        string? typeSystem = null, 
        string? typeCode = null)
    {
        _identifiers.Add((system, value, typeSystem, typeCode));
        return this;
    }
    
    // Convenience for common types
    public PatientBuilder WithMedicalRecordNumber(string value, string? system = null)
    {
        return WithIdentifier(
            system ?? "urn:oid:example.org",
            value,
            "http://terminology.hl7.org/CodeSystem/v2-0203",
            "MR");
    }
    
    // In Build():
    private void ApplyIdentifiers(JsonObject patient)
    {
        if (_identifiers.Count > 0)
        {
            patient["identifier"] = new JsonArray(
                _identifiers.Select(id =>
                {
                    var ident = new JsonObject
                    {
                        ["system"] = id.System,
                        ["value"] = id.Value
                    };
                    
                    if (id.TypeSystem != null && id.TypeCode != null)
                    {
                        ident["type"] = new JsonObject
                        {
                            ["coding"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["system"] = id.TypeSystem,
                                    ["code"] = id.TypeCode
                                }
                            }
                        };
                    }
                    
                    return ident;
                }).ToArray());
        }
    }
}
```

### Test Usage

```csharp
[Fact]
public async Task GivenPatientWithTypedIdentifier_WhenSearchedWithOfType_ThenReturnsMatch()
{
    var tag = Guid.NewGuid().ToString();
    
    var patient = CreatePatient()
        .WithMedicalRecordNumber("12345")
        .WithTag(tag)
        .Build();
        
    await Harness.CreateResourceAsync(patient);
    
    var results = await Harness.SearchAsync(
        "Patient",
        $"_tag={tag}&identifier:of-type=http://terminology.hl7.org/CodeSystem/v2-0203|MR|12345");
        
    results.Should().HaveCount(1);
}
```

### Effort
Low-Medium (2-3 hours)

---

## Implementation Priority

### Immediate (for Phase 1 tests)
1. ✅ **Proposal 4** - Profile metadata (needed for _profile searches)
2. ✅ **Proposal 3** - Field omission (needed for :missing tests)
3. ✅ **Proposal 5** - ObservationBuilder (needed for quantity searches)

### Near-term (for Phase 2 tests)
4. ✅ **Proposal 1** - MultipleBirth (needed for number searches)
5. ✅ **Proposal 2** - BirthDate precision (needed for date tests)
6. ✅ **Proposal 6** - Identifier helper (needed for :of-type tests)

### Future
7. Unit conversion support (not required yet)
8. Additional resource builders (Condition, Procedure, etc.)

---

## Backward Compatibility

All proposals are backward compatible:
- New methods don't change existing behavior
- Optional parameters with sensible defaults
- Existing tests continue to work unchanged

---

## Testing Strategy

Each enhancement should include:
1. Unit tests in `Ignixa.FhirFakes.Tests`
2. E2E tests demonstrating the search capability
3. XML doc comments with examples

Example unit test:
```csharp
[Fact]
public void GivenPatientWithMultipleBirth_WhenBuilt_ThenHasMultipleBirthInteger()
{
    var patient = PatientBuilderFactory.Create(SchemaProvider)
        .WithMultipleBirth(3)
        .Build();
        
    patient["multipleBirthInteger"].Should().Be(3);
    patient.Should().NotContainKey("multipleBirthBoolean");
}
```

---

## Summary

These 6 proposals enable comprehensive E2E testing while maintaining the existing FhirFakes design philosophy:
- ✅ Fluent builder pattern
- ✅ Realistic data generation
- ✅ Cross-version compatibility
- ✅ Type-safe APIs

**Total Effort**: 15-25 hours across all proposals  
**Value**: Enables 40-50 additional E2E tests covering core FHIR search functionality
