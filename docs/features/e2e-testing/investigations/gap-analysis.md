# Investigation: E2E Test Gap Analysis: Ignixa vs fhir-candle R4Tests

**Feature**: e2e-testing
**Status**: Complete
**Created**: 2025-12-11

---


**Date**: 2025-12-11  
**Source**: [fhir-candle R4Tests.cs](https://github.com/FHIR/fhir-candle/blob/main/src/fhir-candle.Tests/R4Tests.cs)  
**Target**: `test/Ignixa.Api.E2ETests`

## Executive Summary

This analysis compares our E2E test coverage against fhir-candle's comprehensive R4Tests suite to identify gaps and propose improvements. We have excellent coverage in include/revinclude and datatype searches, but are missing several core FHIR features like compartment searches, conditional operations, and _summary parameter support.

### Quick Stats
- **fhir-candle R4Tests**: ~1450 lines, 100+ test cases across 5 test classes
- **Ignixa E2E Tests**: ~113 tests across 13 files
- **Test Coverage**: Strong in search (70%), Missing in operations (30%)

---

## Current Ignixa E2E Test Coverage

### ✅ Well-Covered Areas

| Test File | Focus | Coverage |
|-----------|-------|----------|
| `BasicSearchTests.cs` | City, family, given name searches | Good |
| `ChainingSearchTests.cs` | Forward chaining (subject.name, etc.) | Good |
| `IncludeTests/*` | _include, _revinclude, :iterate | Excellent |
| `DatatypeSearchTests/*` | Token, String, Date searches | Good |
| `TokenSearchTests.cs` | :text, :not modifiers, system\|code | Good |
| `StringSearchTests.cs` | :contains, :exact modifiers | Good |
| `DateSearchTests.cs` | Prefix operators (ge, le, etc.) | Good |

### ❌ Missing Areas

1. **Compartment Searches** - Not tested at all
2. **Conditional Operations** - No If-None-Exist tests
3. **_summary Parameter** - Core feature not validated
4. **Quantity Searches** - Limited value-quantity support
5. **:missing Modifier** - Not tested across parameters
6. **Number Searches** - No multiplebirth tests
7. **System Search** - No ?_type=Patient,Observation tests
8. **Subscriptions** - Not in scope yet

---

## Detailed Gap Analysis by Category

### 1. Patient Search Tests (fhir-candle: 50+ cases)

**fhir-candle coverage**:
```
- _id, _id:not, _id:missing
- name, name:contains, name:exact
- gender (single and OR'ed values)
- birthdate (year/month/day precision)
- identifier, identifier:of-type
- active, active:missing
- telecom (system|value patterns)
- multiplebirth (number comparisons: =, le, lt)
- _profile, _profile:missing
- _has reverse chaining
- Authorization scoping (patient launch context)
```

**What we're missing**:
- ❌ `multiplebirth` number searches with comparisons
- ❌ `birthdate` precision tests (year-only: `1982`, month: `1982-01`)
- ❌ `identifier:of-type` modifier
- ❌ `active:missing=true/false`
- ❌ `_profile:missing` tests
- ❌ Authorization/patient scope filtering

**Example test case from fhir-candle**:
```csharp
[InlineData(null, "multiplebirth=3", 1)]
[InlineData(null, "multiplebirth=le3", 1)]
[InlineData(null, "multiplebirth=lt3", 0)]
[InlineData(null, "birthdate=1982-01-23", 1)]
[InlineData(null, "birthdate=1982-01", 1)]
[InlineData(null, "birthdate=1982", 2)]
```

**FhirFakes enhancement needed**:
```csharp
// Add to PatientBuilder
public PatientBuilder WithMultipleBirth(int order)
{
    _multipleBirthInteger = order;
    return this;
}

// Usage in test
var patient = CreatePatient()
    .WithMultipleBirth(3)
    .WithTag(tag)
    .Build();
```

---

### 2. Observation Search Tests (fhir-candle: 30+ cases)

**fhir-candle coverage**:
```
- code (token with system|code)
- value-quantity (with units, comparisons ge/gt/le/lt)
- code-value-quantity (composite searches)
- subject, subject:Patient, subject:Device
- _profile, _profile:missing
- Chaining: subject.name, subject._id
```

**What we're missing**:
- ❌ `value-quantity` with comparison prefixes
- ❌ `code-value-quantity` composite parameter
- ❌ Unit conversions (e.g., lb to kg)
- ❌ Reference type modifiers (`:Patient`, `:Device`)

**Example test cases from fhir-candle**:
```csharp
[InlineData(null, "value-quantity=185|http://unitsofmeasure.org|[lb_av]", 1)]
[InlineData(null, "value-quantity=ge185", 2)]
[InlineData(null, "value-quantity=gt185||[lb_av]", 0)]
[InlineData(null, "code-value-quantity=http://loinc.org|29463-7$185|http://unitsofmeasure.org|[lb_av]", 1)]
[InlineData(null, "subject:Patient=Patient/example", 4)]
[InlineData(null, "subject:Device=Patient/example", 0)]
```

**FhirFakes enhancement needed**:
```csharp
// ObservationState already has Value, Unit, UnitCode
// But we need builder convenience methods
public class ObservationBuilder
{
    public ObservationBuilder WithQuantityValue(
        decimal value, 
        string unit, 
        string system = "http://unitsofmeasure.org")
    {
        // Set value[x] = valueQuantity
        return this;
    }
    
    public ObservationBuilder WithCodeValueQuantity(
        string codeSystem, string code, 
        decimal value, string unit)
    {
        // Set both code and value
        return this;
    }
}
```

---

### 3. Compartment Search Tests (fhir-candle: 15+ cases)

**fhir-candle coverage**:
```
GET /Patient/{id}/*                               - All resources in compartment
GET /Patient/{id}/Observation                     - Specific type
GET /Patient/{id}/Observation?_id=blood-pressure  - With search params
POST /Patient/{id}/_search                        - POST variant
Authorization scoping
```

**What we're missing**:
- ❌ **Entire compartment search feature** not tested

**Example test cases**:
```csharp
[InlineData(null, "example", null, null, 4)]                                    // /Patient/example/*
[InlineData(null, "example", "Observation", null, 4)]                           // /Patient/example/Observation
[InlineData(null, "example", "Observation", "_id=blood-pressure", 1)]          // With params
[InlineData("PatientExampleFull", "example", "Observation", "_id=656", 0)]     // Auth scoped
```

**Implementation priority**: **HIGH** - This is a core FHIR interaction pattern.

**Test file to create**: `CompartmentSearchTests.cs`

```csharp
public class CompartmentSearchTests : CapabilityDrivenTestBase
{
    [Fact]
    public async Task GivenPatientCompartment_WhenSearchAllResources_ThenReturnsCompartmentResources()
    {
        var tag = Guid.NewGuid().ToString();
        
        // Create patient with linked resources
        var scenario = CreateScenario()
            .WithTag(tag)
            .WithOutpatientEncounter()
            .WithVitalSigns(bp: true, weight: true)
            .Build();
            
        await Harness.PostScenarioAsync(scenario);
        
        // Act: Search patient compartment
        var patientId = scenario.Patient!.Id;
        var results = await Harness.GetAsync($"Patient/{patientId}/*?_tag={tag}");
        
        // Assert: Should contain observations from the patient
        results.Should().ContainItemsAssignableTo<JsonObject>();
        results.Count.Should().BeGreaterThan(0);
    }
}
```

---

### 4. Conditional Create Tests (fhir-candle: 3 tests)

**fhir-candle coverage**:
```
POST /Patient with If-None-Exist: _id=xyz
- No matches → 201 Created (new resource)
- One match → 200 OK (returns existing, no new version)
- Multiple matches → 412 Precondition Failed
```

**What we're missing**:
- ❌ **No conditional create tests**
- ❌ If-None-Exist header handling not validated

**Example test case**:
```csharp
[Theory]
[MemberData(nameof(ConditionalData))]
public void ConditionalCreateNoMatch(string resourceType, string json)
{
    // Create with If-None-Exist: _id=guid-that-doesnt-exist
    // Should return 201 Created with new resource
}

[Theory]
[MemberData(nameof(ConditionalData))]
public void ConditionalCreateOneMatch(string resourceType, string json)
{
    // First: Create resource with id=xyz
    // Then: Create with If-None-Exist: _id=xyz
    // Should return 200 OK with existing resource (same ETag)
}
```

**Implementation priority**: **HIGH** - Important FHIR operation semantic.

**Test file to create**: `ConditionalOperationTests.cs`

```csharp
[Fact]
public async Task GivenConditionalCreate_WhenNoMatch_ThenCreatesNewResource()
{
    var tag = Guid.NewGuid().ToString();
    var testId = Guid.NewGuid().ToString();
    var patient = CreatePatient()
        .WithId(testId)
        .WithTag(tag)
        .Build();
    
    // Act: POST with If-None-Exist header
    var response = await Harness.PostResourceAsync(
        patient, 
        headers: new() { ["If-None-Exist"] = $"_id={testId}" });
    
    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    response.Headers.Location.Should().Contain($"Patient/{testId}");
}
```

---

### 5. _summary Parameter Tests (fhir-candle: 50+ cases)

**fhir-candle coverage**:
```
_summary=false  - Full representation (longest response)
_summary=true   - Summary elements only
_summary=text   - Text elements only
_summary=data   - Data elements (no text)
_summary=count  - Count only (no entries)
```

**Validates**:
- Response length ordering (false > true > count)
- Entry count (count=0, others have entries)
- Total consistency across all flags
- Self-link contains _summary parameter

**What we're missing**:
- ❌ **No _summary parameter tests at all**

**Example from fhir-candle**:
```csharp
[InlineData("/Patient?_id=example", 1)]
public void Search(string search, int matchCount)
{
    // For each summary flag (false, true, text, data, count):
    var results = DoSearch(search + "&_summary=" + flag);
    
    // Validate:
    // - count should have 0 entries
    // - false should be longest response
    // - all should have same total
    // - self link includes _summary={flag}
}
```

**Implementation priority**: **HIGH** - Core FHIR feature, affects bandwidth/performance.

**Test file to create**: `SummaryParameterTests.cs`

```csharp
[Theory]
[InlineData("false")]
[InlineData("true")]
[InlineData("text")]
[InlineData("data")]
[InlineData("count")]
public async Task GivenSummaryParameter_WhenSearched_ThenReturnsAppropriateContent(
    string summaryFlag)
{
    var tag = Guid.NewGuid().ToString();
    var patients = Enumerable.Range(0, 3)
        .Select(_ => CreatePatient().WithTag(tag).Build())
        .ToArray();
    
    await Harness.CreateResourcesAsync(patients);
    
    // Act
    var results = await Harness.SearchAsync(
        "Patient", 
        $"_tag={tag}&_summary={summaryFlag}");
    
    // Assert
    results.Total.Should().Be(3);
    
    if (summaryFlag == "count")
    {
        results.Entry.Should().BeEmpty();
    }
    else
    {
        results.Entry.Should().HaveCount(3);
    }
    
    results.Link.Should().Contain(l => 
        l.Relation == "self" && l.Url.Contains($"_summary={summaryFlag}"));
}
```

---

### 6. Quantity Search Tests (fhir-candle: 15+ cases)

**fhir-candle coverage**:
```
value-quantity=185                                    - Exact match
value-quantity=ge185                                  - Greater or equal
value-quantity=gt185                                  - Greater than
value-quantity=le185                                  - Less or equal
value-quantity=lt185                                  - Less than
value-quantity=185|http://unitsofmeasure.org|[lb_av] - With system+unit
value-quantity=185||[lb_av]                          - With unit only
value-quantity=84.1|...|[kg]                         - Unit conversion (TODO)
```

**What we're missing**:
- ❌ Quantity searches with comparison prefixes
- ❌ System and unit validation
- ❌ Unit conversions (future)

**Test file to create**: `QuantitySearchTests.cs`

**FhirFakes enhancement**: Already supported via `ObservationState.Value`, just need test data.

---

## Implementation Roadmap

### Phase 1: Core Missing Features (HIGH Priority) 🔴

**Target**: Fill critical FHIR spec gaps

1. **CompartmentSearchTests.cs** (3-5 tests)
   - Basic compartment: `/Patient/{id}/*`
   - Type-scoped: `/Patient/{id}/Observation`
   - With parameters: `/Patient/{id}/Observation?_id=xyz`

2. **ConditionalOperationTests.cs** (3-5 tests)
   - ConditionalCreate_NoMatch
   - ConditionalCreate_OneMatch
   - ConditionalCreate_MultipleMatches

3. **SummaryParameterTests.cs** (5-10 tests)
   - One test per _summary flag
   - Validation of response content
   - Self-link validation

4. **QuantitySearchTests.cs** (5-10 tests)
   - value-quantity with prefixes (ge, gt, le, lt)
   - System and unit combinations
   - Missing unit handling

5. **MissingModifierTests.cs** (5-10 tests)
   - :missing=true/false on various parameters
   - active:missing, _profile:missing, etc.

**Effort**: 2-3 days  
**Value**: High - Addresses FHIR spec compliance gaps

---

### Phase 2: Enhanced Search (MEDIUM Priority) 🟡

**Target**: Expand search parameter coverage

6. Extend `DateSearchTests.cs`
   - Year-only precision: `birthdate=1982`
   - Month precision: `birthdate=1982-01`

7. Create `NumberSearchTests.cs`
   - multiplebirth searches
   - Comparison operators

8. Extend `TokenSearchTests.cs`
   - identifier:of-type modifier
   - Reference type modifiers (:Patient, :Device)

**Effort**: 1-2 days  
**Value**: Medium - Better search coverage

---

### Phase 3: Advanced Features (LOW Priority) 🟢

**Target**: Nice-to-have features

9. **SystemSearchTests.cs**
   - ?_type=Patient,Observation cross-resource searches

10. **SubscriptionTests.cs** (if on roadmap)
    - Topic parsing
    - Subscription parsing
    - Notification handling

**Effort**: 2-3 days  
**Value**: Low - Advanced features, not widely used

---

## FhirFakes Library Enhancements

### 1. PatientBuilder Additions

```csharp
public PatientBuilder WithMultipleBirth(int order)
{
    _multipleBirthInteger = order;
    return this;
}

public PatientBuilder WithBirthDate(int year)
{
    _birthYear = year;
    _birthMonth = null; // Year precision only
    _birthDay = null;
    return this;
}

public PatientBuilder WithBirthDate(int year, int month)
{
    _birthYear = year;
    _birthMonth = month;
    _birthDay = null; // Month precision
    return this;
}

public PatientBuilder WithoutActive()
{
    _includeActive = false; // Explicitly omit field
    return this;
}

public PatientBuilder WithProfileUri(string profileUrl)
{
    _profileUrls.Add(profileUrl);
    return this;
}
```

### 2. ObservationState Enhancements

```csharp
// Already has Value, Unit, UnitCode - just need convenience builders

public class ObservationBuilder
{
    public ObservationBuilder WithQuantityValue(
        decimal value,
        string unit,
        string system = "http://unitsofmeasure.org")
    {
        Value = value;
        Unit = unit;
        UnitCode = unit;
        return this;
    }
}
```

### 3. ResourceBuilder Base Class

```csharp
public abstract class ResourceBuilder<T>
{
    protected List<string> _profileUrls = [];
    
    public T WithProfile(string profileUrl)
    {
        _profileUrls.Add(profileUrl);
        return (T)this;
    }
    
    protected void ApplyProfiles(JsonObject resource)
    {
        if (_profileUrls.Count > 0)
        {
            resource["meta"] = new JsonObject
            {
                ["profile"] = new JsonArray(_profileUrls.Select(u => JsonValue.Create(u)).ToArray())
            };
        }
    }
}
```

---

## Data Files from fhir-candle

The fhir-candle tests load data from `data/r4/*.json` files:

- `patient-example.json` - Example patient for conditional tests
- `Basic-topic-encounter-complete.json` - Subscription topic
- `Subscription-encounter-complete.json` - Active subscription
- `Bundle-notification-handshake.json` - Notification bundle

**Translation strategy**: Don't copy JSON directly. Instead, create equivalent FhirFakes builder code:

```csharp
// Instead of loading patient-example.json:
var examplePatient = CreatePatient()
    .WithId("example")
    .WithGivenName("Peter")
    .WithFamilyName("Chalmers")
    .WithGender("male")
    .WithBirthDate(1974, 12, 25)
    .WithIdentifier("urn:oid:1.2.36.146.595.217.0.1", "12345", "MR")
    .WithActive(true)
    .Build();
```

---

## Testing Best Practices (from Existing Tests)

### ✅ DO: Follow Existing Patterns

1. **Inherit from CapabilityDrivenTestBase**
   ```csharp
   public class NewTests : CapabilityDrivenTestBase
   {
       public NewTests(IgnixaApiFixture fixture) : base(fixture) { }
   }
   ```

2. **Use capability checks**
   ```csharp
   RequireSearchParameter("Patient", "multiplebirth");
   ```

3. **Use tag-based isolation**
   ```csharp
   var tag = Guid.NewGuid().ToString();
   var patient = CreatePatient().WithTag(tag).Build();
   ```

4. **Use SearchTestHarness**
   ```csharp
   var results = await Harness.SearchAsync("Patient", $"_tag={tag}&name=Smith");
   ```

5. **Use Shouldly**
   ```csharp
   results.Count.ShouldBe(2);
   results.ShouldAllBe(r => r.ResourceType == "Patient");
   ```

### ❌ DON'T: Anti-patterns

1. Don't hardcode resource IDs (use tags)
2. Don't share test data across tests (isolation)
3. Don't skip capability checks
4. Don't use raw HttpClient (use Harness)

---

## Appendix: fhir-candle Test Statistics

### Test Method Breakdown

| Test Class | Method Count | Focus |
|------------|--------------|-------|
| R4TestsPatient | 1 method, 50+ InlineData | Patient searches |
| R4TestsObservation | 1 method, 30+ InlineData | Observation searches |
| R4TestsPatientLooped | 1 method | Performance testing |
| R4TestsSummary | 1 method, 50+ InlineData | _summary parameter |
| R4TestConditionals | 3 methods | Conditional operations |
| R4TestSubscriptions | 4 methods | Subscription/topic parsing |

**Total**: ~140 test cases across 11 test methods

### Authorization Test Patterns

fhir-candle uses authorization scoping:

```csharp
_authorizations.Add("PatientExampleFull", new()
{
    LaunchPatient = "Patient/example",
    Scopes = { "patient/*.*" }
});
```

Tests validate filtered results:
```csharp
[InlineData("PatientExampleFull", "_id=example", 1)]      // Can see own record
[InlineData("PatientDoesNotExistFull", "_id=example", 0)] // Cannot see others
```

**For our implementation**: Consider adding authorization tests in Phase 2 or as separate feature.

---

## Summary

This analysis identified **8 major test categories** with significant gaps in our E2E coverage. The **HIGH priority items** (compartments, conditionals, _summary, quantities, :missing) are core FHIR functionality that should be implemented first. The **FhirFakes enhancements** are minimal and follow existing patterns.

**Recommended Action**: Start with Phase 1 (core features), creating 5 new test files over 2-3 days. This will bring our E2E coverage to parity with fhir-candle in the most critical areas.
