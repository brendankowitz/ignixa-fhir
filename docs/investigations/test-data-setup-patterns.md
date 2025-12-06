# Investigation: Test Data Setup Patterns

**Date:** 2025-12-05
**Status:** Draft
**Context:** Refactoring ChainingSearchTests to use patient-driven scenario setup

## Problem Statement

How should we structure test data setup for E2E tests that require multiple patients with interconnected resources? The `ChainingSearchTests` currently uses `ChainingTestScenario` (a static scenario generator in the Faker library), but this violates the principle that the Faker library should not contain test-specific scenarios.

## Key Constraint: ScenarioBuilder is Single-Patient

**Critical Discovery:** The `ScenarioBuilder` pattern is **explicitly designed for single-patient scenarios**:

```csharp
public ScenarioBuilder WithPatient(int? age = null, string? gender = null, ...)
{
    if (_hasPatient)
    {
        throw new InvalidOperationException(
            "Cannot add multiple patients to a single scenario. Each scenario supports only one patient. " +
            "To test multiple patients, create them separately and add them directly without using the scenario builder.");
    }
    _hasPatient = true;
    // ...
}
```

This design is intentional:
- ScenarioBuilder models a **patient journey** (timeline of encounters, conditions, observations)
- It maintains context like `CurrentEncounter`, `CurrentPractitioner`, `CurrentOrganization`
- All resources are implicitly linked to the single patient

## Use Case Analysis

### Use Case 1: Single Patient Journey (✅ Use ScenarioBuilder)

**Example:** `ChainedSearchTestScenario.GetObservationPatientChainTest()`

```csharp
return new ScenarioBuilder(schemaProvider)
    .WithName("Observation-Patient Chain Test")
    .WithResolvedReferences()
    .WithTag(testTag)

    // Single patient: Alice Smith
    .WithPatient(age: 44, gender: "female", givenName: "Alice", familyName: "Smith")
    .AddEncounter("Annual Physical - Alice Smith")
    .AddObservation(LabObservations.Glucose, 105m, "mg/dL", "mg/dL")
    .AddObservation(ObservationState.BloodPressure(systolic: 125, diastolic: 82))
    .AddObservation(LabObservations.TotalCholesterol, 195m, "mg/dL", "mg/dL")
    .Build();
```

**Characteristics:**
- One patient
- Patient-centric resources (all linked to the patient)
- Temporal sequencing matters
- Tests: `Observation?subject:Patient.name=Alice` finds Alice's observations

### Use Case 2: Multi-Patient Comparison (❌ Can't Use ScenarioBuilder Directly)

**Example:** `ChainingSearchTests` - needs Adams, Smith, and Truman

```csharp
// Test requirement: Search should distinguish between patients
// DiagnosticReport?subject:Patient.name=Smith → Only Smith's reports
// DiagnosticReport?subject:Patient.name=Truman → Only Truman's reports
```

**Characteristics:**
- Multiple independent patients (not a single journey)
- Shared infrastructure (Organization, Location, Group)
- Cross-patient references (Group contains all three patients)
- Tests chained search filtering across different patients

**Why ScenarioBuilder doesn't work:**
```csharp
var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(givenName: "Smith") // ✅ Works
    .WithPatient(givenName: "Truman") // ❌ Throws exception!
    .Build();
```

## Proposed Pattern: User's Suggestion Analysis

**User's proposed pattern:**
```csharp
var trumanScenario = new ScenarioBuilder(schemaProvider)
    .WithName("Truman Scenario")
    .WithResolvedReferences()
    .WithTag(testTag)
    .WithPatient(age: age, gender: gender)
    .AddDevice()
    .AddEncounter("UTI symptoms visit")
    .AddConditionOnset(FhirCode.Conditions.UrinaryTractInfection, severity: 2)
    .AddObservation(FhirCode.Observations.BodyTemperature, 38.5m, "Cel", "Cel")
    .Build();
```

### Analysis: This Pattern Works for Truman's Journey ✅

**But the full test needs:**
1. **Truman's scenario** (your example)
2. **Smith's scenario** (with different observations/reports)
3. **Adams' scenario** (female patient, different profile)
4. **Shared infrastructure** (Organization, Location, Group containing all three)

### Problem: How to merge three scenarios?

**Option A: Merge AllResources from multiple scenarios**
```csharp
// Create individual scenarios
var smithScenario = new ScenarioBuilder(schemaProvider)
    .WithTag(testTag)
    .WithPatient(givenName: "Smith", familyName: "Smith")
    .AddObservation(snomedCode, ...)
    .Build();

var trumanScenario = new ScenarioBuilder(schemaProvider)
    .WithTag(testTag)
    .WithPatient(givenName: "Truman", familyName: "Truman")
    .AddObservation(snomedCode, ...)
    .Build();

// Merge resources
var allResources = new List<ResourceJsonNode>();
allResources.AddRange(smithScenario.AllResources);
allResources.AddRange(trumanScenario.AllResources);

// ❌ Problem: How to add Group that references both patients?
// Group needs Smith.Id and Truman.Id, but they're in separate scenarios
```

**Issue:** Cross-scenario references are not supported by ScenarioBuilder.

**Option B: Create shared resources outside scenarios**
```csharp
// Shared infrastructure
var organization = OrganizationBuilder.Create(schemaProvider)
    .WithIdentifier(orgId)
    .WithTag(testTag)
    .Build();

// Individual patient scenarios
var smithScenario = new ScenarioBuilder(schemaProvider)
    .WithTag(testTag)
    .WithPatient(...)
    .AddOrganization(/* How to use pre-created org? */)  // ❌ Not supported
    .Build();
```

**Issue:** ScenarioBuilder creates its own Organization; can't inject pre-created ones.

## Current Pattern: Direct Builder Usage (What I Implemented)

```csharp
private ChainingTestData CreateChainingSearchScenario(string tag)
{
    var faker = new SchemaBasedFhirResourceFaker(SchemaProvider);
    var data = new ChainingTestData { Tag = tag, ... };

    // Shared infrastructure
    data.Organization = OrganizationBuilder.Create(SchemaProvider)
        .WithIdentifier(data.OrganizationIdentifier)
        .WithTag(tag)
        .Build();

    // Patient 1: Adams (female, no org)
    data.AdamsPatient = PatientBuilderFactory.Create(SchemaProvider)
        .WithGender(g => g.Female)
        .WithFamilyName("Adams")
        .WithTag(tag)
        .Build();

    // Patient 2: Smith (male, linked to org)
    data.SmithPatient = PatientBuilderFactory.Create(SchemaProvider)
        .WithGender(g => g.Male)
        .WithGivenName(data.SmithPatientGivenName)
        .WithManagingOrganization(data.Organization.Id!)
        .WithTag(tag)
        .Build();

    // Create observations, reports, etc. manually
    data.SmithSnomedObservation = CreateObservation(faker, data.SmithPatient, snomedCode, "Patient");
    // ...

    // Group referencing all three patients
    var group = faker.Generate("Group");
    group.MutableNode["member"] = CreateGroupMemberArray(
        data.AdamsPatient.Id!,
        data.SmithPatient.Id!,
        data.TrumanPatient.Id!);

    return data;
}
```

### Advantages ✅
- Full control over resource creation
- Easy cross-references (Group → Patients)
- Shared infrastructure (one Organization for all)
- Clear test data structure

### Disadvantages ❌
- No temporal sequencing (can't do `.DelayMonths(3)`)
- Manual reference construction
- More verbose than ScenarioBuilder

## Recommendation

### For Single-Patient Journey Tests → Use ScenarioBuilder ✅

```csharp
var scenario = new ScenarioBuilder(schemaProvider)
    .WithName("Truman's UTI Journey")
    .WithTag(testTag)
    .WithPatient(age: 45, gender: "male", givenName: "Truman")
    .AddEncounter("UTI symptoms visit")
    .AddConditionOnset(FhirCode.Conditions.UrinaryTractInfection, severity: 2)
    .AddObservation(FhirCode.Observations.BodyTemperature, 38.5m, "Cel")
    .DelayWeeks(2)
    .AddEncounter("Follow-up visit")
    .Build();

await harness.CreateResourcesAsync(scenario.AllResources.ToArray());
```

**Use when:**
- Testing a single patient's clinical journey
- Temporal sequencing matters (encounters over time)
- Resources are patient-centric

**Examples:**
- `ChainedSearchTestScenario.GetObservationPatientChainTest()` ✅
- `ChainedSearchTestScenario.GetConditionPatientChainTest()` ✅

### For Multi-Patient Comparison Tests → Use Direct Builders ✅

```csharp
private TestData CreateMultiPatientScenario(string tag)
{
    var faker = new SchemaBasedFhirResourceFaker(SchemaProvider);

    // Shared infrastructure
    var org = OrganizationBuilder.Create(SchemaProvider).WithTag(tag).Build();

    // Independent patients
    var smith = PatientBuilderFactory.Create(SchemaProvider)
        .WithGivenName("Smith")
        .WithManagingOrganization(org.Id!)
        .WithTag(tag)
        .Build();

    var truman = PatientBuilderFactory.Create(SchemaProvider)
        .WithGivenName("Truman")
        .WithTag(tag)
        .Build();

    // Cross-patient resources
    var group = faker.Generate("Group");
    group.MutableNode["member"] = new JsonArray
    {
        new JsonObject { ["entity"] = new JsonObject { ["reference"] = $"Patient/{smith.Id}" } },
        new JsonObject { ["entity"] = new JsonObject { ["reference"] = $"Patient/{truman.Id}" } }
    };

    return new TestData { Smith = smith, Truman = truman, Group = group, ... };
}
```

**Use when:**
- Testing search filtering across multiple patients
- Need shared infrastructure (one Organization for multiple patients)
- Cross-patient references (Group containing multiple patients)
- Temporal sequencing doesn't matter

**Examples:**
- `ChainingSearchTests` (needs Adams, Smith, Truman) ✅
- `BasicSearchTests` with multiple patients for comparison ✅

## Alternative: Hybrid Pattern (Future Enhancement)

**Concept:** Create individual patient scenarios, then merge with shared infrastructure

```csharp
// Individual patient journeys
var smithScenario = new ScenarioBuilder(schemaProvider)
    .WithTag(testTag)
    .WithPatient(givenName: "Smith")
    .AddEncounter("Initial visit")
    .AddObservation(snomedCode, ...)
    .Build();

var trumanScenario = new ScenarioBuilder(schemaProvider)
    .WithTag(testTag)
    .WithPatient(givenName: "Truman")
    .AddEncounter("Initial visit")
    .AddObservation(snomedCode, ...)
    .Build();

// Merge with shared infrastructure
var merger = new ScenarioMerger(schemaProvider);
var merged = merger
    .AddScenario(smithScenario)
    .AddScenario(trumanScenario)
    .WithSharedOrganization(org)
    .WithCrossReference(group => {
        group.AddMember(smithScenario.Patient);
        group.AddMember(trumanScenario.Patient);
    })
    .Build();
```

**Status:** Not implemented - would require new `ScenarioMerger` utility.

## Decision

**For ChainingSearchTests refactoring:**

Use **Direct Builder Pattern** (Option B above) because:
1. ScenarioBuilder explicitly doesn't support multiple patients
2. Need cross-patient references (Group containing all three)
3. Need shared Organization referenced by only one patient
4. Temporal sequencing not required for chain search tests

**Keep ChainingTestScenario OUT of Faker library:**
- It's test-specific (unique searchable values per test run)
- Lives in test project as helper method: `CreateChainingSearchScenario()`

**Location:** `test/Ignixa.Api.E2ETests/ChainingSearchTests.cs` (private method)

## Your Proposed Pattern: Verdict

**Your pattern works for Truman's scenario in isolation ✅**

```csharp
var trumanScenario = new ScenarioBuilder(schemaProvider)
    .WithTag(testTag)
    .WithPatient(age: 45, gender: "male")
    .AddEncounter("UTI symptoms visit")
    .AddConditionOnset(FhirCode.Conditions.UrinaryTractInfection, severity: 2)
    .Build();
```

**But ChainingSearchTests needs more:**
- Smith's scenario (different observations)
- Adams' scenario (female, different profile)
- Shared Group containing all three patients
- Shared Organization (only Smith links to it)

**To use your pattern, you'd need:**
1. Create three separate scenarios (Smith, Truman, Adams)
2. Manually merge `AllResources` from all three
3. **Problem:** Create Group outside scenarios, manually reference all patients
4. **Problem:** Organization can't be shared across scenarios

**Conclusion:** ScenarioBuilder is perfect for individual patient journeys, but ChainingSearchTests is fundamentally a **multi-patient comparison test** that requires direct builder usage.

---

## Next Steps

1. ✅ Keep Direct Builder Pattern for ChainingSearchTests
2. Document this pattern in CLAUDE.md under "Multi-Patient Test Scenarios"
3. Consider `ScenarioMerger` utility if this pattern becomes common
