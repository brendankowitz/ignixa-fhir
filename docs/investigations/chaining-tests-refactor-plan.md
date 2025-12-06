# Refactoring Plan: ChainingSearchTests to Use ScenarioBuilder

**Date:** 2025-12-05
**Status:** Proposed (Awaiting Approval)

## Current State (Direct Builder Approach)

```csharp
private ChainingTestData CreateChainingSearchScenario(string tag)
{
    var faker = new SchemaBasedFhirResourceFaker(SchemaProvider);
    var data = new ChainingTestData { Tag = tag, ... };

    // Create Organization manually
    data.Organization = OrganizationBuilder.Create(SchemaProvider)
        .WithIdentifier(data.OrganizationIdentifier)
        .WithCity(data.OrganizationCity)
        .WithTag(tag)
        .Build();

    // Create patients manually with PatientBuilder
    data.AdamsPatient = PatientBuilderFactory.Create(SchemaProvider)
        .WithGender(g => g.Female)
        .WithFamilyName("Adams")
        .WithTag(tag)
        .Build();

    data.SmithPatient = PatientBuilderFactory.Create(SchemaProvider)
        .WithGender(g => g.Male)
        .WithGivenName(data.SmithPatientGivenName)
        .WithManagingOrganization(data.Organization.Id!)
        .WithTag(tag)
        .Build();

    // Create observations manually
    data.SmithSnomedObservation = CreateObservation(faker, data.SmithPatient, snomedCodeJson, "Patient");

    // Create diagnostic reports manually
    data.SmithSnomedDiagnosticReport = CreateDiagnosticReport(
        faker, data.SmithPatient, data.SmithSnomedObservation, snomedCodeJson);

    // Create Group manually
    var group = faker.Generate("Group");
    group.MutableNode["member"] = CreateGroupMemberArray(
        data.AdamsPatient.Id!,
        data.SmithPatient.Id!,
        data.TrumanPatient.Id!);

    // Manual resource ordering
    data.AllResources.AddRange([...]);

    return data;
}
```

**Characteristics:**
- ❌ Manual observation creation with `faker.Generate("Observation")`
- ❌ Manual diagnostic report creation
- ❌ Manual reference linking
- ❌ No temporal sequencing capability
- ❌ No fluent encounter context
- ✅ Full control over resource creation
- ✅ Easy cross-references

## Proposed State (Three ScenarioBuilder Approach)

```csharp
private ChainingTestData CreateChainingSearchScenario(string tag)
{
    var data = new ChainingTestData
    {
        Tag = tag,
        SmithPatientGivenName = Guid.NewGuid().ToString(),
        TrumanPatientGivenName = Guid.NewGuid().ToString(),
        SnomedCode = Guid.NewGuid().ToString(),
        OrganizationCity = Guid.NewGuid().ToString(),
        OrganizationIdentifier = Guid.NewGuid().ToString()
    };

    // Shared SNOMED and LOINC codes for all patients
    var snomedCode = new FhirCode(FhirCode.Systems.SnomedCt, data.SnomedCode, "Test SNOMED");
    var loincCode = new FhirCode(FhirCode.Systems.Loinc, "4548-4", "Hemoglobin A1c");

    // ========================================
    // Scenario 1: Adams (Female, No Org)
    // ========================================
    var adamsScenario = new ScenarioBuilder(SchemaProvider)
        .WithName("Adams Patient Journey")
        .WithDescription("Female patient with LOINC observation")
        .WithTag(tag)
        .WithResolvedReferences()

        // Patient: Adams (female, no org link)
        .WithPatient(age: 32, gender: "female", familyName: "Adams")

        // Encounter + Observation
        .AddEncounter("Annual checkup - Adams")
        .AddObservation(loincCode, 5.8m, "mg/dL", "mg/dL")

        .Build();

    // Extract resources
    data.AdamsPatient = adamsScenario.Patient!;
    data.AdamsLoincObservation = adamsScenario.Observations[0];

    // ========================================
    // Scenario 2: Smith (Male, WITH Org for chaining)
    // ========================================
    var smithScenario = new ScenarioBuilder(SchemaProvider)
        .WithName("Smith Patient Journey")
        .WithDescription("Male patient linked to organization with SNOMED + LOINC observations")
        .WithTag(tag)
        .WithResolvedReferences()

        // Organization (Smith will be linked to this)
        .AddOrganization(new OrganizationState
        {
            Name = "Organization_MainClinic",
            OrganizationName = "Main Clinic",
            Type = OrganizationState.OrganizationTypes.HealthcareProvider,
            Address = new OrganizationAddress(
                Line: "100 Main St",
                City: data.OrganizationCity,  // Unique city for chaining tests
                State: "WA",
                PostalCode: "98101")
        })

        // Patient: Smith (male, auto-linked to CurrentOrganization)
        .WithPatient(age: 45, gender: "male",
            givenName: data.SmithPatientGivenName,  // Unique for search tests
            familyName: "Smith")

        // Encounter + Observations
        .AddEncounter("Diabetes follow-up - Smith")
        .AddObservation(snomedCode, 145m, "mg/dL", "mg/dL")  // SNOMED observation
        .AddObservation(loincCode, 7.2m, "%", "%")            // LOINC observation

        // Diagnostic Reports (reference observations from current encounter)
        .AddDiagnosticReport(new DiagnosticReportState
        {
            Code = new FhirCode(FhirCode.Systems.Loinc, data.SnomedCode, "SNOMED Report"),
            Observations = [(snomedCode, 145m, "mg/dL")]
        })
        .AddDiagnosticReport(new DiagnosticReportState
        {
            Code = new FhirCode(FhirCode.Systems.Loinc, "4548-4", "LOINC Report"),
            Observations = [(loincCode, 7.2m, "%")]
        })

        .Build();

    // Extract resources
    data.Organization = smithScenario.Organizations[0];
    data.SmithPatient = smithScenario.Patient!;
    data.SmithSnomedObservation = smithScenario.Observations[0];
    data.SmithLoincObservation = smithScenario.Observations[1];
    data.SmithSnomedDiagnosticReport = smithScenario.DiagnosticReports[0];
    data.SmithLoincDiagnosticReport = smithScenario.DiagnosticReports[1];

    // ========================================
    // Scenario 3: Truman (Male, No Org)
    // ========================================
    var trumanScenario = new ScenarioBuilder(SchemaProvider)
        .WithName("Truman Patient Journey")
        .WithDescription("Male patient with SNOMED + LOINC observations")
        .WithTag(tag)
        .WithResolvedReferences()

        // Patient: Truman (male, no org link)
        .WithPatient(age: 48, gender: "male",
            givenName: data.TrumanPatientGivenName,  // Unique for search tests
            familyName: "Truman")

        // Encounter + Observations
        .AddEncounter("Annual physical - Truman")
        .AddObservation(snomedCode, 132m, "mg/dL", "mg/dL")  // SNOMED observation
        .AddObservation(loincCode, 6.8m, "%", "%")            // LOINC observation

        // Diagnostic Reports
        .AddDiagnosticReport(new DiagnosticReportState
        {
            Code = new FhirCode(FhirCode.Systems.Loinc, data.SnomedCode, "SNOMED Report"),
            Observations = [(snomedCode, 132m, "mg/dL")]
        })
        .AddDiagnosticReport(new DiagnosticReportState
        {
            Code = new FhirCode(FhirCode.Systems.Loinc, "4548-4", "LOINC Report"),
            Observations = [(loincCode, 6.8m, "%")]
        })

        .Build();

    // Extract resources
    data.TrumanPatient = trumanScenario.Patient!;
    data.TrumanSnomedObservation = trumanScenario.Observations[0];
    data.TrumanLoincObservation = trumanScenario.Observations[1];
    data.TrumanSnomedDiagnosticReport = trumanScenario.DiagnosticReports[0];
    data.TrumanLoincDiagnosticReport = trumanScenario.DiagnosticReports[1];

    // ========================================
    // Cross-Patient Resources
    // ========================================

    // Location (shared, not patient-specific)
    var location = new SchemaBasedFhirResourceFaker(SchemaProvider)
        .WithTag(tag)
        .Generate("Location");
    location.MutableNode["address"] = new JsonObject { ["city"] = "Seattle" };
    data.Location = location;

    // Devices (for Observation.subject Device reference tests)
    var faker = new SchemaBasedFhirResourceFaker(SchemaProvider).WithTag(tag);
    data.DeviceLoincSubject = faker.Generate("Device");
    data.DeviceSnomedSubject = faker.Generate("Device");

    // Device observations (manual creation needed - not patient journeys)
    data.DeviceLoincObservation = CreateObservation(faker, data.DeviceLoincSubject,
        CreateCodeableConceptJson("http://loinc.org", "4548-4"), "Device");
    data.DeviceSnomedObservation = CreateObservation(faker, data.DeviceSnomedSubject,
        CreateCodeableConceptJson("http://snomed.info/sct", data.SnomedCode), "Device");

    // CareTeam linked to Adams
    var careTeam = faker.Generate("CareTeam");
    careTeam.MutableNode["subject"] = CreateReferenceJson("Patient", data.AdamsPatient.Id!);
    data.AdamsCareTeam = careTeam;

    // Group containing all three patients
    var group = faker.Generate("Group");
    group.MutableNode["type"] = "person";
    group.MutableNode["actual"] = true;
    group.MutableNode["member"] = CreateGroupMemberArray(
        data.AdamsPatient.Id!,
        data.SmithPatient.Id!,
        data.TrumanPatient.Id!);
    data.PatientGroup = group;

    // ========================================
    // Merge All Resources
    // ========================================
    data.AllResources.AddRange(adamsScenario.AllResources);
    data.AllResources.AddRange(smithScenario.AllResources);
    data.AllResources.AddRange(trumanScenario.AllResources);
    data.AllResources.Add(data.Location);
    data.AllResources.Add(data.DeviceLoincSubject);
    data.AllResources.Add(data.DeviceSnomedSubject);
    data.AllResources.Add(data.DeviceLoincObservation);
    data.AllResources.Add(data.DeviceSnomedObservation);
    data.AllResources.Add(data.AdamsCareTeam);
    data.AllResources.Add(data.PatientGroup);

    return data;
}
```

## Key Differences

| Aspect | Current (Direct Builders) | Proposed (ScenarioBuilder) |
|--------|---------------------------|----------------------------|
| **Patient Creation** | Manual `PatientBuilderFactory.Create()` | `.WithPatient()` |
| **Org Linking** | Manual `.WithManagingOrganization(org.Id)` | Automatic via `CurrentOrganization` |
| **Observations** | Manual `faker.Generate("Observation")` | `.AddObservation(code, value, unit)` |
| **DiagnosticReports** | Manual `faker.Generate("DiagnosticReport")` | `.AddDiagnosticReport(state)` |
| **Encounters** | Not created | `.AddEncounter("reason")` |
| **Reference Linking** | Manual JSON manipulation | Automatic via context |
| **Code Reusability** | Helper methods `CreateObservation()` | Built-in ScenarioBuilder methods |
| **Temporal Sequencing** | Not available | Available (`.DelayMonths(3)`) |
| **Verbosity** | ~200 lines of setup code | ~120 lines (40% reduction) |

## Migration Steps

### Step 1: Verify ScenarioBuilder Capabilities

**Check if ScenarioBuilder supports all needed resource types:**
- [x] Patient - `.WithPatient()`
- [x] Organization - `.AddOrganization()`
- [x] Encounter - `.AddEncounter()`
- [x] Observation - `.AddObservation()`
- [x] DiagnosticReport - `.AddDiagnosticReport()`
- [ ] CareTeam - **Need to check if exists**
- [ ] Group - **Manual creation required (cross-patient)**
- [ ] Device - **Not patient-specific, manual creation**
- [ ] Location - **Not patient-specific, manual creation**

**Action:** Verify `ScenarioBuilder` has methods for CareTeam or if manual creation is acceptable.

### Step 2: Refactor Patient Scenarios

**Order of implementation:**
1. Adams scenario (simplest - no org, 1 observation)
2. Truman scenario (no org, 2 observations + 2 reports)
3. Smith scenario (with org, 2 observations + 2 reports)

### Step 3: Handle Cross-Patient Resources

**Resources that remain manual:**
- Location (shared infrastructure)
- Devices (not patient-specific)
- Device observations (observations where subject is Device, not Patient)
- CareTeam (if not supported by ScenarioBuilder)
- Group (requires IDs from all three patients)

### Step 4: Test Data Structure Changes

**ChainingTestData class stays the same** - no changes needed:
```csharp
private sealed class ChainingTestData
{
    public required string Tag { get; init; }
    public required string SmithPatientGivenName { get; init; }
    // ... all other properties unchanged
}
```

### Step 5: Verification

**Test that need to pass:**
- All 20 ChainingSearchTests (currently failing in my implementation)
- 2 skipped tests remain skipped

## Risks & Mitigation

### Risk 1: ScenarioBuilder Missing Features

**Issue:** CareTeam creation might not be supported by ScenarioBuilder

**Check:**
```bash
grep -r "AddCareTeam" src/Core/Ignixa.FhirFakes/Scenarios/
```

**Mitigation:** If not supported, keep manual creation (acceptable for cross-patient resources)

### Risk 2: DiagnosticReport Observation Linking

**Issue:** DiagnosticReport needs to reference specific observations from the encounter

**Current approach (manual):**
```csharp
report.MutableNode["result"] = new JsonArray {
    CreateReferenceJson("Observation", observation.Id!)
};
```

**ScenarioBuilder approach:**
```csharp
.AddDiagnosticReport(new DiagnosticReportState {
    Observations = [(code, value, unit)]  // Creates obs automatically?
})
```

**Mitigation:** Verify DiagnosticReportState behavior - does it create new observations or reference existing ones?

### Risk 3: Organization Identifier

**Issue:** Organization needs unique identifier for chaining test: `organization.identifier=X`

**Check:** Does `OrganizationState` support identifier property?

**Mitigation:** If not, manually set identifier after scenario builds:
```csharp
smithScenario.Organizations[0].MutableNode["identifier"] = [
    { "system": "...", "value": data.OrganizationIdentifier }
];
```

### Risk 4: Test Data Uniqueness

**Issue:** Each test needs unique searchable values (GUIDs for names, codes, cities)

**Current:** Generate GUIDs in `ChainingTestData` initializer
**Proposed:** Same - pass GUIDs into FhirCode constructors

**No change needed.**

## Questions Before Proceeding

### Q1: DiagnosticReportState Behavior
Does `.AddDiagnosticReport()` create new observations or reference existing ones from the current encounter?

**Need to check:** ScenarioBuilder implementation

### Q2: CareTeam Support
Does ScenarioBuilder have `.AddCareTeam()` method?

**Need to check:** ScenarioBuilder API

### Q3: Organization Identifier
Can we set Organization identifier via OrganizationState?

**Need to check:** OrganizationState properties

### Q4: Reference Format
The tests currently use resolved references (`Patient/123`). Will `.WithResolvedReferences()` handle this correctly?

**Need to verify:** Reference rewriting logic

## Rollback Plan

If ScenarioBuilder approach fails:
1. `git checkout -- test/Ignixa.Api.E2ETests/ChainingSearchTests.cs`
2. Keep current direct builder approach
3. Document why ScenarioBuilder isn't suitable for this use case

## Approval Checklist

Before proceeding with refactoring:
- [ ] User approves the pattern (3 separate ScenarioBuilder instances)
- [ ] Verify ScenarioBuilder supports DiagnosticReport → Observation linking
- [ ] Verify OrganizationState supports identifier property
- [ ] Check if CareTeam needs manual creation or has ScenarioBuilder support
- [ ] Confirm `.WithResolvedReferences()` produces correct reference format

## Estimated Impact

**Lines of code:**
- Current: ~290 lines in `CreateChainingSearchScenario()`
- Proposed: ~180 lines (37% reduction)

**Readability:**
- ✅ Clearer patient journey structure
- ✅ Less manual JSON manipulation
- ✅ Self-documenting with `.AddEncounter()`, `.AddObservation()`

**Maintainability:**
- ✅ Easier to add temporal sequencing later (`.DelayWeeks()`)
- ✅ Follows established pattern (ChainedSearchTestScenario)
- ✅ Less test-specific helper methods

---

## Decision Point

**Should we proceed with this refactoring?**

**If YES:** I'll implement Step 1-5 above
**If NO:** I'll document why direct builders are preferred for this use case
**If CONDITIONAL:** Please specify what needs verification first
