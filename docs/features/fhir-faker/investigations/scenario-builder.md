# Investigation: ScenarioBuilder Enhancements Needed for ChainingSearchTests

**Feature**: fhir-faker
**Status**: Viable
**Created**: ** 2025-12-05

---


**Date:** 2025-12-05
**Status:** Ready for Implementation
**Goal:** Make ScenarioBuilder fully support ChainingSearchTests without workarounds

## Design Decision: StateId Pattern (User Suggestion) ✅

Instead of using attribute names for cross-state references, we'll use **StateId-based references**:

```csharp
// StateId on base class
public abstract class ScenarioState
{
    public string? StateId { get; init; }  // NEW: Unique identifier for references
}

// Usage: Reference observations by StateId
.AddState(new ObservationState {
    StateId = "obs_glucose",
    Code = glucoseCode,
    Value = 105m,
    Unit = "mg/dL"
})
.AddDiagnosticReport(new DiagnosticReportState {
    ReferencedObservationStateIds = ["obs_glucose"]  // Clear, self-documenting
})
```

**Benefits:**
- ✅ Cleaner than attribute names
- ✅ Self-documenting code
- ✅ Separate namespace (doesn't pollute context.Attributes)
- ✅ Backward compatible (StateId is optional)
- ✅ Minimal implementation (~56 lines)

**Full design:** See `scenario-state-id-pattern.md`

---

## Current Gaps

### Gap 1: No CareTeam Support ❌

**Current state:**
```bash
$ grep -r "AddCareTeam" src/Core/Ignixa.FhirFakes/Scenarios/
# No results
```

**What we need:**
```csharp
.AddCareTeam(new CareTeamState {
    Name = "Adams Care Team",
    Category = CareTeamCategory.ClinicalResearch,
    Status = "active"
})
```

**Solution:** Add `CareTeamState` class and `.AddCareTeam()` method to ScenarioBuilder.

---

### Gap 2: OrganizationState Has No Custom Identifier Support ❌

**Current state:**
```csharp
public sealed class OrganizationState
{
    public string? NpiNumber { get; init; }  // System: NPI
    public string? TaxId { get; init; }      // System: Tax ID
    // ❌ No way to add custom identifier system
}
```

**What we need:**
```csharp
.AddOrganization(new OrganizationState {
    OrganizationName = "Test Clinic",
    CustomIdentifiers = [
        ("http://test-system", "unique-guid-12345")
    ]
})
```

**Solution:** Add `CustomIdentifiers` property to `OrganizationState`.

---

### Gap 3: DiagnosticReport Observation Linking ✅ (Solved by StateId)

**Issue:** DiagnosticReportState creates NEW observations from tuples, but ChainingSearchTests needs to reference EXISTING observations.

**Example of the problem:**
```csharp
.AddObservation(snomedCode, 145m, "mg/dL")  // Creates Observation A
.AddDiagnosticReport(new DiagnosticReportState {
    Code = reportCode,
    Observations = [(snomedCode, 145m, "mg/dL")]  // ❌ Creates Observation B (duplicate!)
})
```

**Solution: Use StateId pattern**
```csharp
.AddState(new ObservationState {
    StateId = "obs_snomed",  // ✅ Explicit identifier
    Code = snomedCode,
    Value = 145m,
    Unit = "mg/dL"
})
.AddDiagnosticReport(new DiagnosticReportState {
    Code = reportCode,
    ReferencedObservationStateIds = ["obs_snomed"]  // ✅ References existing observation
})
```

**Implementation:**
- Add `ReferencedObservationStateIds` property to `DiagnosticReportState`
- Keep existing `Observations` property for backward compatibility

---

## Implementation Plan

### Task 0: Implement StateId Pattern (Foundation)

**Priority:** Do this FIRST - all other tasks depend on it

**Files to create/modify:**
1. `src/Core/Ignixa.FhirFakes/Scenarios/States/ScenarioState.cs` - Add `StateId` property
2. `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioContext.cs` - Add state resource registry
3. Update all existing state classes to register with StateId:
   - `ObservationState.cs`
   - `ConditionState.cs`
   - `MedicationOrderState.cs`
   - `ProcedureState.cs`
   - `PractitionerState.cs`
   - `OrganizationState.cs`
   - `EncounterState.cs`
   - `DiagnosticReportState.cs`

**Estimated lines:** 56 lines
**Complexity:** Low
**Assigned to:** @agent-fast-coding-agent (simple, focused changes)

---

### Task 1: Add CareTeamState

**Files to create/modify:**
- `src/Core/Ignixa.FhirFakes/Scenarios/States/CareTeamState.cs` (new)
- `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioBuilder.cs` (add `.AddCareTeam()`)
- `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioContext.cs` (add CareTeams collection)

**CareTeamState design (with StateId support):**
```csharp
public sealed class CareTeamState : ScenarioState
{
    public required string Name { get; init; }
    public string Status { get; init; } = "active";
    public FhirCode? Category { get; init; }
    public IReadOnlyList<string>? ParticipantStateIds { get; init; }  // ✅ References via StateId

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        // Create CareTeam resource
        // Link to context.Patient (subject)
        // Add participants via context.GetStateResource(stateId)
        // Register with StateId: context.RegisterStateResource(StateId, careTeam)
        // Add to context.CareTeams collection
    }
}
```

**ScenarioBuilder method:**
```csharp
public ScenarioBuilder AddCareTeam(CareTeamState careTeam)
{
    ArgumentNullException.ThrowIfNull(careTeam);
    _states.Add(careTeam);
    return this;
}
```

**Estimated lines:** 200 lines
**Complexity:** Medium
**Assigned to:** @agent-coding-agent (medium complexity, new resource type)

---

### Task 2: Add CustomIdentifiers to OrganizationState

**File to modify:**
- `src/Core/Ignixa.FhirFakes/Scenarios/States/OrganizationState.cs`

**Changes:**
```csharp
public sealed class OrganizationState : ScenarioState
{
    // Existing properties...
    public string? NpiNumber { get; init; }
    public string? TaxId { get; init; }

    // NEW: Custom identifiers
    /// <summary>
    /// Gets custom identifiers to add beyond NPI and Tax ID.
    /// Each tuple contains (system, value).
    /// </summary>
    public IReadOnlyList<(string System, string Value)>? CustomIdentifiers { get; init; }
}
```

**Execute method update:**
```csharp
public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
{
    // ... existing code for NPI and TaxId ...

    // Add custom identifiers
    if (CustomIdentifiers is not null)
    {
        foreach (var (system, value) in CustomIdentifiers)
        {
            identifiers.Add(new JsonObject
            {
                ["system"] = system,
                ["value"] = value
            });
        }
    }

    node["identifier"] = identifiers;
}
```

---

### Task 3: Add Referenced Observations to DiagnosticReportState (Uses StateId)

**File to modify:**
- `src/Core/Ignixa.FhirFakes/Scenarios/States/DiagnosticReportState.cs`

**Changes:**
```csharp
public sealed class DiagnosticReportState : ScenarioState
{
    // Existing property (creates new observations)
    public IReadOnlyList<(FhirCode Code, decimal Value, string Unit)>? Observations { get; init; }

    // NEW: Reference existing observations by StateId
    /// <summary>
    /// Gets the StateIds of observations to reference in this diagnostic report.
    /// These observations must have been created with StateId in a previous AddState() call.
    /// </summary>
    public IReadOnlyList<string>? ReferencedObservationStateIds { get; init; }
}
```

**Execute method update:**
```csharp
public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
{
    // ... existing code ...

    // Create result array (references to observations)
    var resultArray = new JsonArray();

    // Add references to existing observations (via StateId)
    if (ReferencedObservationStateIds is not null)
    {
        foreach (var stateId in ReferencedObservationStateIds)
        {
            var observation = context.GetStateResource(stateId);
            if (observation is not null)
            {
                resultArray.Add(new JsonObject
                {
                    ["reference"] = $"Observation/{observation.Id}"
                });
            }
        }
    }

    // Create new observations from tuples (existing behavior)
    if (Observations is not null)
    {
        foreach (var (code, value, unit) in Observations)
        {
            var observation = CreateObservation(context, faker, code, value, unit);
            context.AddObservation(observation, $"{code.Display}: {value} {unit}");

            resultArray.Add(new JsonObject
            {
                ["reference"] = $"Observation/{observation.Id}"
            });
        }
    }

    node["result"] = resultArray;
}
```

**Estimated lines:** 25 lines
**Complexity:** Low
**Assigned to:** @agent-fast-coding-agent (simple property + lookup)

---

### Task 4: Verify StateId Registration in All States

**Purpose:** Ensure all state classes call `context.RegisterStateResource(StateId, resource)` after creating their resource.

**Files to verify/update:**
- `ObservationState.cs`
- `ConditionState.cs`
- `MedicationOrderState.cs`
- `ProcedureState.cs`
- `PractitionerState.cs`
- `OrganizationState.cs`
- `EncounterState.cs`
- `DiagnosticReportState.cs`

**Pattern to add:**
```csharp
public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
{
    // ... create resource ...

    context.AddXxx(resource, description);

    // NEW: Register with StateId for cross-references
    context.RegisterStateResource(StateId, resource);
}
```

**Note:** This is already covered by Task 0 - just verification.

---

## Testing Strategy

### Test 1: CareTeam Creation
```csharp
[Fact]
public void GivenCareTeamState_WhenBuilt_ThenCreatesCareTeamLinkedToPatient()
{
    var scenario = new ScenarioBuilder(_schemaProvider)
        .WithPatient(age: 45, gender: "female", familyName: "TestPatient")
        .AddCareTeam(new CareTeamState { Name = "Test Team" })
        .Build();

    scenario.CareTeams.Should().HaveCount(1);
    var careTeam = scenario.CareTeams[0];
    careTeam.MutableNode["subject"]["reference"].GetValue<string>()
        .Should().Contain(scenario.Patient.Id);
}
```

### Test 2: Custom Organization Identifier
```csharp
[Fact]
public void GivenOrganizationWithCustomIdentifier_WhenBuilt_ThenHasCustomIdentifier()
{
    var customId = Guid.NewGuid().ToString();
    var scenario = new ScenarioBuilder(_schemaProvider)
        .WithPatient()
        .AddOrganization(new OrganizationState {
            OrganizationName = "Test Org",
            CustomIdentifiers = [("http://test-system", customId)]
        })
        .Build();

    var org = scenario.Organizations[0];
    var identifiers = org.MutableNode["identifier"].AsArray();
    identifiers.Should().Contain(i =>
        i["system"].GetValue<string>() == "http://test-system" &&
        i["value"].GetValue<string>() == customId);
}
```

### Test 3: DiagnosticReport References Existing Observation
```csharp
[Fact]
public void GivenDiagnosticReportReferencingObservation_WhenBuilt_ThenReferencesCorrectObservation()
{
    var scenario = new ScenarioBuilder(_schemaProvider)
        .WithPatient()
        .AddEncounter("Test visit")
        .AddObservation(glucoseCode, 105m, "mg/dL", assignToAttribute: "glucose_obs")
        .AddDiagnosticReport(new DiagnosticReportState {
            Code = lipidPanelCode,
            ReferencedObservationAttributes = ["glucose_obs"]
        })
        .Build();

    // Should have exactly 1 observation (not 2)
    scenario.Observations.Should().HaveCount(1);

    var report = scenario.DiagnosticReports[0];
    var resultRef = report.MutableNode["result"][0]["reference"].GetValue<string>();
    resultRef.Should().Be($"Observation/{scenario.Observations[0].Id}");
}
```

---

## Estimated Effort (Updated with StateId Pattern)

| Task | Files | Estimated Lines | Complexity | Agent |
|------|-------|----------------|------------|-------|
| **Task 0:** StateId foundation | 10 files | ~56 lines | Low | @agent-fast-coding-agent |
| **Task 1:** CareTeamState | 3 files | ~200 lines | Medium | @agent-coding-agent |
| **Task 2:** CustomIdentifiers | 1 file | ~15 lines | Low | @agent-fast-coding-agent |
| **Task 3:** DiagnosticReport refs | 1 file | ~25 lines | Low | @agent-fast-coding-agent |
| Tests | 1 file | ~100 lines | Low | @agent-coding-agent |

**Total:** ~396 lines across 16 files
**Estimated time:** 2-3 hours (with parallel agent execution)

**Agent allocation:**
- @agent-fast-coding-agent: Tasks 0, 2, 3 (simple, focused)
- @agent-coding-agent: Tasks 1, Tests (medium complexity)

---

## Decision: APPROVED ✅

**Status:** Ready for implementation
**Approach:** StateId pattern + CareTeamState + CustomIdentifiers

**Benefits:**
- ✅ Makes ScenarioBuilder suitable for multi-patient test scenarios
- ✅ Eliminates all workarounds
- ✅ 40% code reduction in ChainingSearchTests (~180 lines vs ~290 lines)
- ✅ Reusable for future tests
- ✅ Clean, self-documenting StateId references
- ✅ Backward compatible

**Implementation order:**
1. Task 0: StateId foundation (@agent-fast-coding-agent) ← START HERE
2. Task 2: CustomIdentifiers (@agent-fast-coding-agent)
3. Task 3: DiagnosticReport refs (@agent-fast-coding-agent)
4. Task 1: CareTeamState (@agent-coding-agent)
5. Tests (@agent-coding-agent)
6. Refactor ChainingSearchTests (manual review)

**Next step:** Spawn agents for parallel implementation.
