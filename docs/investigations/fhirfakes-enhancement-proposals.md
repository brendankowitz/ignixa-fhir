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

---

## Additional Enhancements (From Codebase Analysis)

**Date**: 2025-12-12
**Analysis Source**: Current E2E test fixtures and scenarios

### Analysis Summary

After analyzing current E2E test files (TokenSearchTestFixture, DateSearchTestFixture, IncludeTestBase, IncludeTestScenario), we identified **significant manual MutableNode manipulation** that could be eliminated with additional builders and ObservationBuilder enhancements.

**Current Manual Patterns Found**:
- **TokenSearchTestFixture**: 150+ lines of manual observation creation with complex token patterns
- **DateSearchTestFixture**: 100+ lines of manual observation creation with date variations
- **IncludeTestBase**: 300+ lines of helper methods for Location, Practitioner, DiagnosticReport, Group, CareTeam, MedicationRequest, MedicationDispense
- **IncludeTestScenario**: 200+ lines of manual resource creation

**Impact**: ~750 lines of manual JSON manipulation could be reduced to ~160 lines of fluent builder calls (**79% reduction**)

---

## Proposal 7: Additional Resource Builders

### Current State
Multiple resource types are being manually created with MutableNode manipulation in E2E tests. Each test file duplicates similar helper methods.

### Manual Creation Examples

From `IncludeTestScenario.cs` (lines 96-106):
```csharp
// Practitioner - NO BUILDER EXISTS
var practitioner = faker.Generate("Practitioner");
practitioner.MutableNode["name"] = new JsonArray
{
    new JsonObject
    {
        ["family"] = "Anderson",
        ["given"] = new JsonArray { "Alice" }
    }
};
```

From `IncludeTestScenario.cs` (lines 238-258):
```csharp
// Location - NO BUILDER EXISTS
var location = faker.Generate("Location");
location.MutableNode["status"] = "active";
location.MutableNode["managingOrganization"] = CreateReferenceJson("Organization", managingOrganizationId);
location.MutableNode["partOf"] = CreateReferenceJson("Location", partOfLocationId);
```

From `IncludeTestBase.cs` (lines 249-275):
```csharp
// DiagnosticReport - NO BUILDER EXISTS
var report = new ResourceJsonNode { ResourceType = "DiagnosticReport", Id = Guid.NewGuid().ToString() };
report.MutableNode["meta"] = CreateMetaTagJson(tag);
report.MutableNode["status"] = "final";
report.MutableNode["code"] = CreateCodeableConceptJson(codeSystem, code);
report.MutableNode["subject"] = CreateReferenceJson("Patient", patientId);
report.MutableNode["result"] = new JsonArray { CreateReferenceJson("Observation", observationId) };
```

---

### Proposal 7a: PractitionerBuilder

```csharp
public sealed class PractitionerBuilder
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private string? _id;
    private string? _tag;
    private string? _familyName;
    private string? _givenName;
    private readonly List<(string? System, string Value)> _identifiers = [];
    private readonly List<(string Code, string? System, string? Display)> _specialties = [];

    public static PractitionerBuilder Create(IFhirSchemaProvider schemaProvider)
        => new(schemaProvider);

    public PractitionerBuilder WithTag(string tag)
    {
        _tag = tag;
        return this;
    }

    public PractitionerBuilder WithName(string given, string family)
    {
        _givenName = given;
        _familyName = family;
        return this;
    }

    public PractitionerBuilder WithFamilyName(string family)
    {
        _familyName = family;
        return this;
    }

    public PractitionerBuilder WithGivenName(string given)
    {
        _givenName = given;
        return this;
    }

    public PractitionerBuilder WithNpi(string npi)
    {
        _identifiers.Add(("http://hl7.org/fhir/sid/us-npi", npi));
        return this;
    }

    public PractitionerBuilder WithIdentifier(string value, string? system = null)
    {
        _identifiers.Add((system, value));
        return this;
    }

    public PractitionerBuilder WithSpecialty(string code, string? system = null, string? display = null)
    {
        _specialties.Add((code, system ?? "http://snomed.info/sct", display));
        return this;
    }

    public ResourceJsonNode Build() { /* Build implementation */ }
}
```

**Usage**:
```csharp
// Instead of manual creation:
var practitioner = faker.Generate("Practitioner");
practitioner.MutableNode["name"] = new JsonArray { ... };

// Use builder:
var practitioner = PractitionerBuilder.Create(schemaProvider)
    .WithTag(tag)
    .WithName("Alice", "Anderson")
    .WithNpi("1234567890")
    .WithSpecialty("207Q00000X", display: "Family Medicine")
    .Build();
```

**Effort**: Medium (3-4 hours)

---

### Proposal 7b: LocationBuilder

```csharp
public sealed class LocationBuilder
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private string? _id;
    private string? _tag;
    private string? _name;
    private string _status = "active";
    private string? _managingOrganizationId;
    private string? _partOfLocationId;
    private (string? Line, string? City, string? State, string? Zip)? _address;

    public static LocationBuilder Create(IFhirSchemaProvider schemaProvider)
        => new(schemaProvider);

    public LocationBuilder WithTag(string tag)
    {
        _tag = tag;
        return this;
    }

    public LocationBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public LocationBuilder WithStatus(string status)
    {
        _status = status;
        return this;
    }

    public LocationBuilder WithManagingOrganization(string orgId)
    {
        _managingOrganizationId = orgId;
        return this;
    }

    public LocationBuilder WithPartOf(string locationId)
    {
        _partOfLocationId = locationId;
        return this;
    }

    public LocationBuilder WithAddress(string line, string city, string state, string zip)
    {
        _address = (line, city, state, zip);
        return this;
    }

    public ResourceJsonNode Build() { /* Build implementation */ }
}
```

**Usage**:
```csharp
// Instead of:
var location = faker.Generate("Location");
location.MutableNode["status"] = "active";
location.MutableNode["managingOrganization"] = CreateReferenceJson(...);
location.MutableNode["partOf"] = CreateReferenceJson(...);

// Use builder:
var location = LocationBuilder.Create(schemaProvider)
    .WithTag(tag)
    .WithName("Main Clinic")
    .WithManagingOrganization(orgId)
    .WithPartOf(parentLocationId)
    .Build();
```

**Effort**: Medium (3-4 hours)

---

### Proposal 7c: DiagnosticReportBuilder

```csharp
public sealed class DiagnosticReportBuilder
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private string? _id;
    private string? _tag;
    private string _status = "final";
    private string? _subjectId;
    private (string System, string Code, string? Display)? _code;
    private readonly List<string> _resultObservationIds = [];

    public static DiagnosticReportBuilder Create(IFhirSchemaProvider schemaProvider)
        => new(schemaProvider);

    public DiagnosticReportBuilder WithTag(string tag)
    {
        _tag = tag;
        return this;
    }

    public DiagnosticReportBuilder WithStatus(string status)
    {
        _status = status;
        return this;
    }

    public DiagnosticReportBuilder WithSubject(string patientId)
    {
        _subjectId = patientId;
        return this;
    }

    public DiagnosticReportBuilder WithCode(string code, string system, string? display = null)
    {
        _code = (system, code, display);
        return this;
    }

    public DiagnosticReportBuilder WithResult(string observationId)
    {
        _resultObservationIds.Add(observationId);
        return this;
    }

    public DiagnosticReportBuilder WithResults(params string[] observationIds)
    {
        _resultObservationIds.AddRange(observationIds);
        return this;
    }

    public ResourceJsonNode Build() { /* Build implementation */ }
}
```

**Usage**:
```csharp
// Instead of manual JSON:
var report = faker.Generate("DiagnosticReport");
report.MutableNode["status"] = "final";
report.MutableNode["code"] = CreateCodeableConceptJson(...);
report.MutableNode["subject"] = CreateReferenceJson(...);
report.MutableNode["result"] = new JsonArray { ... };

// Use builder:
var report = DiagnosticReportBuilder.Create(schemaProvider)
    .WithTag(tag)
    .WithSubject(patientId)
    .WithCode("4548-4", "http://loinc.org", "Hemoglobin A1c")
    .WithResults(obs1.Id, obs2.Id)
    .Build();
```

**Effort**: Medium (3-4 hours)

---

### Proposal 7d-7g: Additional Builders (Lower Priority)

Following the same pattern, create builders for:

| Builder | Usage in Tests | Complexity | Effort |
|---------|---------------|------------|--------|
| **GroupBuilder** | IncludeTestScenario line 329 | Medium | 3-4 hours |
| **CareTeamBuilder** | IncludeTestScenario line 358 | Medium | 3-4 hours |
| **MedicationRequestBuilder** | IncludeTestBase line 405 | Low-Medium | 2-3 hours |
| **MedicationDispenseBuilder** | IncludeTestBase line 425 | Medium | 3-4 hours |

**Total Effort for 7a-7g**: 18-26 hours

---

## Proposal 8: ObservationBuilder Extended Features

### Current State
Proposal 5 covers basic ObservationBuilder with quantity support. E2E test analysis reveals **additional features needed**.

### Analysis Findings

**TokenSearchTestFixture** (lines 56-125) creates observations with:
- `valueCodeableConcept` (not just `valueQuantity`)
- `category` field
- `identifier` field (for case-sensitive tests)
- Complex token patterns in `coding` arrays

**DateSearchTestFixture** (lines 139-184) creates observations with:
- `effectivePeriod` (start/end dates, not just `effectiveDateTime`)

**IncludeTestScenario** (lines 263-295) creates observations with:
- `performer` array (Practitioner or Organization references)

---

### Proposal 8a: Support for effectivePeriod

```csharp
public class ObservationBuilder
{
    private string? _effectiveDateTime;
    private string? _effectivePeriodStart;
    private string? _effectivePeriodEnd;

    // Existing method
    public ObservationBuilder WithEffectiveDateTime(string dateTime)
    {
        _effectiveDateTime = dateTime;
        _effectivePeriodStart = null;
        _effectivePeriodEnd = null;
        return this;
    }

    /// <summary>
    /// Sets effectivePeriod instead of effectiveDateTime.
    /// Used for observations that span a time range.
    /// </summary>
    public ObservationBuilder WithEffectivePeriod(string startDate, string endDate)
    {
        _effectivePeriodStart = startDate;
        _effectivePeriodEnd = endDate;
        _effectiveDateTime = null; // Clear DateTime variant
        return this;
    }

    // In Build():
    private void ApplyEffectiveTiming(JsonObject obs)
    {
        if (_effectivePeriodStart is not null && _effectivePeriodEnd is not null)
        {
            obs["effectivePeriod"] = new JsonObject
            {
                ["start"] = _effectivePeriodStart,
                ["end"] = _effectivePeriodEnd
            };
        }
        else if (_effectiveDateTime is not null)
        {
            obs["effectiveDateTime"] = _effectiveDateTime;
        }
    }
}
```

**Usage** (from DateSearchTestFixture.cs:139):
```csharp
// Instead of:
observation.MutableNode["effectivePeriod"] = new JsonObject
{
    ["start"] = "1980-05-16",
    ["end"] = "1980-05-17"
};

// Use builder:
var obs = ObservationBuilder.Create(schemaProvider)
    .WithCode("4548-4", "http://loinc.org")
    .WithEffectivePeriod("1980-05-16", "1980-05-17")
    .WithSubject(patientId)
    .WithTag(tag)
    .Build();
```

**Effort**: Low (1 hour)

---

### Proposal 8b: Support for category field

```csharp
public class ObservationBuilder
{
    private string? _categoryCode;
    private string? _categorySystem;
    private string? _categoryDisplay;

    /// <summary>
    /// Sets the observation category (e.g., "vital-signs", "laboratory").
    /// </summary>
    public ObservationBuilder WithCategory(
        string code,
        string? system = null,
        string? display = null)
    {
        _categoryCode = code;
        _categorySystem = system ?? "http://terminology.hl7.org/CodeSystem/observation-category";
        _categoryDisplay = display;
        return this;
    }

    // In Build():
    private void ApplyCategory(JsonObject obs)
    {
        if (_categoryCode is not null)
        {
            obs["category"] = new JsonArray
            {
                new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = _categorySystem,
                            ["code"] = _categoryCode,
                            ["display"] = _categoryDisplay ?? _categoryCode
                        }
                    }
                }
            };
        }
    }
}
```

**Usage** (from TokenSearchTestFixture.cs:224):
```csharp
// Instead of:
observation.MutableNode["category"] = new JsonArray
{
    new JsonObject
    {
        ["coding"] = new JsonArray
        {
            new JsonObject { ["system"] = "system", ["code"] = "test" }
        }
    }
};

// Use builder:
var obs = ObservationBuilder.Create(schemaProvider)
    .WithCategory("laboratory")
    .WithTag(tag)
    .Build();
```

**Effort**: Low (1 hour)

---

### Proposal 8c: Support for valueCodeableConcept

```csharp
public class ObservationBuilder
{
    // Existing valueQuantity fields
    private decimal? _valueQuantity;
    private string? _valueUnit;

    // New valueCodeableConcept fields
    private string? _valueCodeableConceptCode;
    private string? _valueCodeableConceptSystem;
    private string? _valueCodeableConceptDisplay;
    private string? _valueCodeableConceptText;

    /// <summary>
    /// Sets valueCodeableConcept instead of valueQuantity.
    /// Used for coded observations (e.g., blood type).
    /// </summary>
    public ObservationBuilder WithCodedValue(
        string code,
        string? system = null,
        string? display = null,
        string? text = null)
    {
        _valueCodeableConceptCode = code;
        _valueCodeableConceptSystem = system;
        _valueCodeableConceptDisplay = display;
        _valueCodeableConceptText = text;
        _valueQuantity = null; // Clear quantity variant
        return this;
    }

    // In Build():
    private void ApplyValue(JsonObject obs)
    {
        if (_valueCodeableConceptCode is not null)
        {
            var coding = new JsonObject
            {
                ["code"] = _valueCodeableConceptCode
            };

            if (_valueCodeableConceptSystem is not null)
            {
                coding["system"] = _valueCodeableConceptSystem;
            }

            if (_valueCodeableConceptDisplay is not null)
            {
                coding["display"] = _valueCodeableConceptDisplay;
            }

            var concept = new JsonObject
            {
                ["coding"] = new JsonArray { coding }
            };

            if (_valueCodeableConceptText is not null)
            {
                concept["text"] = _valueCodeableConceptText;
            }

            obs["valueCodeableConcept"] = concept;
        }
        else if (_valueQuantity.HasValue)
        {
            // Existing quantity logic
        }
    }
}
```

**Usage** (from TokenSearchTestFixture.cs:56-125):
```csharp
// Instead of complex manual creation:
var valueCodeableConcept = new JsonObject();
valueCodeableConcept["coding"] = new JsonArray
{
    new JsonObject { ["system"] = "system1", ["code"] = "code1" }
};
observation.MutableNode["valueCodeableConcept"] = valueCodeableConcept;

// Use builder:
var obs = ObservationBuilder.Create(schemaProvider)
    .WithCode("obs-code", "http://loinc.org")
    .WithCodedValue("code1", "system1", display: "text")
    .WithTag(tag)
    .Build();
```

**Effort**: Low-Medium (2 hours)

---

### Proposal 8d: Support for identifier field

```csharp
public class ObservationBuilder
{
    private readonly List<(string? System, string Value)> _identifiers = [];

    /// <summary>
    /// Adds an identifier to the observation.
    /// Can be called multiple times to add multiple identifiers.
    /// </summary>
    public ObservationBuilder WithIdentifier(string value, string? system = null)
    {
        _identifiers.Add((system, value));
        return this;
    }

    // In Build():
    private void ApplyIdentifiers(JsonObject obs)
    {
        if (_identifiers.Count > 0)
        {
            obs["identifier"] = new JsonArray(
                _identifiers.Select(id =>
                {
                    var ident = new JsonObject
                    {
                        ["value"] = id.Value
                    };

                    if (id.System is not null)
                    {
                        ident["system"] = id.System;
                    }

                    return ident;
                }).ToArray());
        }
    }
}
```

**Usage** (from TokenSearchTestFixture.cs:282):
```csharp
// Instead of:
observation.MutableNode["identifier"] = new JsonArray
{
    new JsonObject { ["system"] = "test", ["value"] = "VALUE" },
    new JsonObject { ["system"] = "test", ["value"] = "value" }
};

// Use builder:
var obs = ObservationBuilder.Create(schemaProvider)
    .WithIdentifier("VALUE", "test")
    .WithIdentifier("value", "test")  // Case-sensitive test
    .WithTag(tag)
    .Build();
```

**Effort**: Low (1 hour)

---

### Proposal 8e: Enhanced performer support

```csharp
public class ObservationBuilder
{
    private readonly List<(string ResourceType, string Id)> _performers = [];

    /// <summary>
    /// Adds a performer reference.
    /// Can be called multiple times for multiple performers.
    /// </summary>
    public ObservationBuilder WithPerformer(string resourceType, string id)
    {
        _performers.Add((resourceType, id));
        return this;
    }

    /// <summary>
    /// Adds a Practitioner performer (convenience method).
    /// </summary>
    public ObservationBuilder WithPractitionerPerformer(string practitionerId)
    {
        return WithPerformer("Practitioner", practitionerId);
    }

    /// <summary>
    /// Adds an Organization performer (convenience method).
    /// </summary>
    public ObservationBuilder WithOrganizationPerformer(string organizationId)
    {
        return WithPerformer("Organization", organizationId);
    }

    // In Build():
    private void ApplyPerformers(JsonObject obs)
    {
        if (_performers.Count > 0)
        {
            obs["performer"] = new JsonArray(
                _performers.Select(p => new JsonObject
                {
                    ["reference"] = $"{p.ResourceType}/{p.Id}"
                }).ToArray());
        }
    }
}
```

**Usage** (from IncludeTestScenario.cs:282):
```csharp
// Instead of:
if (practitionerId is not null || organizationId is not null)
{
    var performers = new JsonArray();
    if (practitionerId is not null)
        performers.Add(CreateReferenceJson("Practitioner", practitionerId));
    if (organizationId is not null)
        performers.Add(CreateReferenceJson("Organization", organizationId));
    observation.MutableNode["performer"] = performers;
}

// Use builder:
var obs = ObservationBuilder.Create(schemaProvider)
    .WithCode("4548-4", "http://loinc.org")
    .WithPractitionerPerformer(practitionerId)
    .WithOrganizationPerformer(organizationId)
    .Build();
```

**Effort**: Low (1 hour)

---

**Total Effort for Proposal 8**: 6-7 hours
**Impact**: Eliminates ~250 lines of manual observation creation across test fixtures

---

## Proposal 9: Base Builder Infrastructure

### Current State
Each builder reimplements common patterns:
- `WithTag()`, `WithId()` - duplicated in PatientBuilder, OrganizationBuilder
- `BuildMeta()` - duplicated meta.tag logic
- `CreateReference()`, `CreateCodeableConcept()` - duplicated helper methods

### Analysis
- **PatientBuilder**: Implements WithTag, WithId, BuildMeta
- **OrganizationBuilder**: Implements WithTag, WithId, BuildMeta (identical code)
- **Proposed builders** (7a-7g, 8): Will need same functionality → more duplication

### Proposed Solution

Create a base class for all FHIR resource builders:

```csharp
/// <summary>
/// Base class for all FHIR resource builders.
/// Provides common functionality like ID, tags, and profile metadata.
/// </summary>
public abstract class FhirResourceBuilder<TBuilder>
    where TBuilder : FhirResourceBuilder<TBuilder>
{
    protected readonly IFhirSchemaProvider _schemaProvider;
    protected string? _id;
    protected string? _tag;
    protected readonly List<string> _profileUrls = [];

    protected FhirResourceBuilder(IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        _schemaProvider = schemaProvider;
    }

    /// <summary>
    /// Sets the resource ID.
    /// </summary>
    public TBuilder WithId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _id = id;
        return (TBuilder)this;
    }

    /// <summary>
    /// Sets a tag for test isolation.
    /// </summary>
    public TBuilder WithTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _tag = tag;
        return (TBuilder)this;
    }

    /// <summary>
    /// Adds a profile URL to meta.profile.
    /// Can be called multiple times.
    /// </summary>
    public TBuilder WithProfile(string profileUrl)
    {
        ArgumentNullException.ThrowIfNull(profileUrl);
        _profileUrls.Add(profileUrl);
        return (TBuilder)this;
    }

    /// <summary>
    /// Builds the meta element with tag and profile.
    /// </summary>
    protected JsonObject BuildMeta()
    {
        var meta = new JsonObject
        {
            ["versionId"] = "1",
            ["lastUpdated"] = DateTime.UtcNow.ToString("o")
        };

        if (_tag is not null)
        {
            meta["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://ignixa.dev/test-isolation",
                    ["code"] = _tag
                }
            };
        }

        if (_profileUrls.Count > 0)
        {
            meta["profile"] = new JsonArray(
                _profileUrls.Select(u => JsonValue.Create(u)).ToArray());
        }

        return meta;
    }

    /// <summary>
    /// Creates a FHIR Reference JSON object.
    /// </summary>
    protected static JsonObject CreateReference(string resourceType, string id)
    {
        return new JsonObject
        {
            ["reference"] = $"{resourceType}/{id}"
        };
    }

    /// <summary>
    /// Creates a FHIR CodeableConcept JSON object.
    /// </summary>
    protected static JsonObject CreateCodeableConcept(
        string code,
        string system,
        string? display = null,
        string? text = null)
    {
        var coding = new JsonObject
        {
            ["system"] = system,
            ["code"] = code
        };

        if (display is not null)
        {
            coding["display"] = display;
        }

        var concept = new JsonObject
        {
            ["coding"] = new JsonArray { coding }
        };

        if (text is not null)
        {
            concept["text"] = text;
        }

        return concept;
    }

    /// <summary>
    /// Build method must be implemented by derived classes.
    /// </summary>
    public abstract ResourceJsonNode Build();
}
```

### Refactored Builders

```csharp
// Before:
public sealed class PatientBuilder
{
    private string? _id;
    private string? _tag;

    public PatientBuilder WithTag(string tag) { ... }
    public PatientBuilder WithId(string id) { ... }
    private JsonObject BuildMeta() { ... }
}

public sealed class OrganizationBuilder
{
    private string? _id;
    private string? _tag;

    public OrganizationBuilder WithTag(string tag) { ... }  // Duplicate!
    public OrganizationBuilder WithId(string id) { ... }     // Duplicate!
    private JsonObject BuildMeta() { ... }                   // Duplicate!
}

// After:
public sealed class PatientBuilder : FhirResourceBuilder<PatientBuilder>
{
    // No need to reimplement WithTag, WithId, BuildMeta, CreateReference, etc.

    public PatientBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider) { }

    public override ResourceJsonNode Build()
    {
        var patient = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["id"] = _id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),  // From base class
            // ... patient-specific fields
        };

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(patient.ToJsonString());
    }
}

public sealed class OrganizationBuilder : FhirResourceBuilder<OrganizationBuilder>
{
    // Automatically inherits WithTag, WithId, BuildMeta, etc.

    public override ResourceJsonNode Build() { ... }
}

public sealed class ObservationBuilder : FhirResourceBuilder<ObservationBuilder>
{
    // Automatically inherits all base functionality

    public override ResourceJsonNode Build() { ... }
}
```

### Benefits

1. **Eliminates duplication**: ~50 lines per builder saved
2. **Consistency**: All builders have identical tag/id/profile APIs
3. **Discoverability**: IntelliSense shows common methods
4. **Type safety**: Fluent API with proper return types
5. **Extensibility**: Easy to add new common functionality (e.g., `WithLanguage()`)

**Effort**: Medium (4-5 hours including refactoring existing builders)
**Impact**:
- Eliminates ~150 lines of duplicated code across 3 existing + 7 new builders
- Makes future builders faster to implement (~30% time savings)

---

## Proposal 10: Helper Extension Methods (Optional)

### Current State
Test files have duplicate helper methods:
- `CreateReferenceJson()` - 11 occurrences
- `CreateCodeableConceptJson()` - 8 occurrences
- `CreateMetaTagJson()` - 6 occurrences

### Note
**Proposal 9 (base class)** already provides these as protected methods for builders. This proposal is for **direct test usage** when builders don't fully support the use case.

### Proposed API

```csharp
namespace Ignixa.FhirFakes;

/// <summary>
/// Helper methods for FHIR test data creation.
/// Use builders when available - these are for edge cases and manual manipulation.
/// </summary>
public static class FhirTestHelpers
{
    /// <summary>
    /// Creates a FHIR Reference JSON object.
    /// </summary>
    public static JsonObject CreateReference(string resourceType, string id)
    {
        return new JsonObject
        {
            ["reference"] = $"{resourceType}/{id}"
        };
    }

    /// <summary>
    /// Creates a FHIR CodeableConcept JSON object.
    /// </summary>
    public static JsonObject CreateCodeableConcept(
        string code,
        string system,
        string? display = null,
        string? text = null)
    {
        var coding = new JsonObject
        {
            ["system"] = system,
            ["code"] = code
        };

        if (display is not null)
        {
            coding["display"] = display;
        }

        var concept = new JsonObject
        {
            ["coding"] = new JsonArray { coding }
        };

        if (text is not null)
        {
            concept["text"] = text;
        }

        return concept;
    }

    /// <summary>
    /// Creates a meta.tag structure for test isolation.
    /// </summary>
    public static JsonObject CreateMetaTag(string tag)
    {
        return new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://ignixa.dev/test-isolation",
                    ["code"] = tag
                }
            }
        };
    }
}
```

**Usage**:
```csharp
using Ignixa.FhirFakes;

// When builder doesn't support your exact use case:
var customResource = new ResourceJsonNode { ResourceType = "CustomResource" };
customResource.MutableNode["subject"] = FhirTestHelpers.CreateReference("Patient", patientId);
customResource.MutableNode["code"] = FhirTestHelpers.CreateCodeableConcept("code", "system");
```

**Effort**: Low (1-2 hours)
**Priority**: LOW - Proposal 9 already provides this for builders. Only needed for edge cases.

---

## Updated Implementation Priority

### Phase 1: HIGH Priority (Immediate Impact) 🔴

**Covers 80% of manual manipulation, enables most E2E tests**

1. **Proposal 8** - ObservationBuilder Extended Features (6-7 hours)
   - 8a: effectivePeriod support
   - 8b: category field
   - 8c: valueCodeableConcept support
   - 8d: identifier field
   - 8e: Enhanced performer support

2. **Proposal 7a-7c** - Core Resource Builders (9-12 hours)
   - 7a: PractitionerBuilder
   - 7b: LocationBuilder
   - 7c: DiagnosticReportBuilder

3. **Proposal 9** - Base Builder Infrastructure (4-5 hours)
   - Create `FhirResourceBuilder<T>` base class
   - Refactor existing builders (PatientBuilder, OrganizationBuilder)
   - Eliminates duplication for future builders

**Phase 1 Total Effort**: 19-24 hours
**Phase 1 Impact**: Eliminates ~600 lines of manual JSON manipulation (~80% reduction)

---

### Phase 2: MEDIUM Priority (Remaining Coverage) 🟡

**Covers remaining 20%, specialized scenarios**

4. **Proposal 7d-7g** - Additional Builders (12-16 hours)
   - 7d: GroupBuilder
   - 7e: CareTeamBuilder
   - 7f: MedicationRequestBuilder
   - 7g: MedicationDispenseBuilder

5. **Proposal 10** - Helper Extension Methods (1-2 hours)
   - For edge cases not covered by builders

**Phase 2 Total Effort**: 13-18 hours
**Phase 2 Impact**: Eliminates remaining ~150 lines

---

### Phase 3: FUTURE Enhancements 🟢

**From original proposals (1-6), already documented**

6. Proposals 1-6 (original document)
   - PatientBuilder enhancements
   - Identifier helpers
   - Field omission support

---

## Total Impact Summary

### Code Reduction

| Area | Before (Manual Lines) | After (Builder Lines) | Reduction |
|------|----------------------|----------------------|-----------|
| **TokenSearchTestFixture** | 150 | 30 | 80% |
| **DateSearchTestFixture** | 100 | 20 | 80% |
| **IncludeTestBase helpers** | 300 | 0 (use builders) | 100% |
| **IncludeTestScenario** | 200 | 60 | 70% |
| **Total** | **~750 lines** | **~110 lines** | **~85%** |

### Maintainability Benefits

1. **Type Safety**: Compile-time checking instead of runtime JSON errors
2. **Discoverability**: IntelliSense shows available methods
3. **Consistency**: All builders follow same patterns
4. **FHIR Compliance**: Builders enforce FHIR constraints
5. **Readability**: Fluent API is self-documenting

### Example Transformation

**Before** (IncludeTestScenario.cs, ~15 lines):
```csharp
var practitioner = faker.Generate("Practitioner");
practitioner.MutableNode["name"] = new JsonArray
{
    new JsonObject
    {
        ["family"] = "Anderson",
        ["given"] = new JsonArray { "Alice" }
    }
};

var location = faker.Generate("Location");
location.MutableNode["status"] = "active";
location.MutableNode["managingOrganization"] = CreateReferenceJson("Organization", orgId);
location.MutableNode["partOf"] = CreateReferenceJson("Location", parentId);
```

**After** (~4 lines):
```csharp
var practitioner = PractitionerBuilder.Create(schemaProvider)
    .WithTag(tag).WithName("Alice", "Anderson").Build();

var location = LocationBuilder.Create(schemaProvider)
    .WithTag(tag).WithManagingOrganization(orgId).WithPartOf(parentId).Build();
```

**Savings**: 73% fewer lines, 100% type-safe, FHIR-compliant

---

## Recommendation

**Implement in this order:**

1. **Week 1**: Proposal 9 (base infrastructure) + Proposal 8 (ObservationBuilder extensions)
   - Provides foundation and highest-impact builder
   - Enables DateSearchTests and TokenSearchTests refactoring

2. **Week 2**: Proposal 7a-7c (PractitionerBuilder, LocationBuilder, DiagnosticReportBuilder)
   - Enables IncludeTests refactoring
   - Removes all helper methods from IncludeTestBase

3. **Week 3** (optional): Proposal 7d-7g (remaining builders)
   - Complete coverage for all current E2E scenarios

**Total Timeline**: 2-3 weeks for complete implementation
**Minimum Viable**: Week 1-2 eliminates 80% of manual manipulation
