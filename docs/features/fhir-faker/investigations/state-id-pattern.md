# Investigation: Pattern: StateId for Cross-State References

**Feature**: fhir-faker
**Status**: Viable
**Created**: ** 2025-12-05

---


**Date:** 2025-12-05
**Status:** Proposed Design
**Author:** User suggestion

## Problem

How should states reference resources created by other states (e.g., DiagnosticReport referencing Observations)?

## Current Approach: Attribute Names (Messy)

```csharp
.AddObservation(code, value, unit, assignToAttribute: "obs_snomed")
.AddDiagnosticReport(new DiagnosticReportState {
    ReferencedObservationAttributes = ["obs_snomed"]  // String lookup
})
```

**Issues:**
- ❌ String-based references (typo-prone)
- ❌ Attribute namespace pollution
- ❌ Tight coupling to context.Attributes dictionary
- ❌ No type safety

## Proposed Approach: StateId (Clean) ✅

### Design

**1. Add `StateId` property to `ScenarioState` base class:**

```csharp
public abstract class ScenarioState
{
    /// <summary>
    /// Gets or sets the name of this state (for debugging/logging).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique identifier for this state.
    /// Used for cross-state references (e.g., DiagnosticReport referencing Observations).
    /// If not specified, states are not referenceable.
    /// </summary>
    public string? StateId { get; init; }

    public abstract void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker);
}
```

**2. Store state-generated resources in context by StateId:**

```csharp
public sealed class ScenarioContext
{
    // Existing collections...
    private readonly Dictionary<string, ResourceJsonNode> _stateResources = [];

    /// <summary>
    /// Registers a resource created by a state with the given StateId.
    /// </summary>
    internal void RegisterStateResource(string? stateId, ResourceJsonNode resource)
    {
        if (!string.IsNullOrEmpty(stateId))
        {
            _stateResources[stateId] = resource;
        }
    }

    /// <summary>
    /// Gets a resource created by a state with the given StateId.
    /// </summary>
    public ResourceJsonNode? GetStateResource(string stateId)
    {
        return _stateResources.TryGetValue(stateId, out var resource) ? resource : null;
    }
}
```

**3. Update states to register themselves:**

```csharp
public sealed class ObservationState : ScenarioState
{
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        // ... create observation ...

        context.AddObservation(observation, description);

        // Register with StateId for cross-state references
        context.RegisterStateResource(StateId, observation);
    }
}
```

**4. Reference states by StateId:**

```csharp
public sealed class DiagnosticReportState : ScenarioState
{
    /// <summary>
    /// Gets the StateIds of observations to reference in this report.
    /// </summary>
    public IReadOnlyList<string>? ReferencedObservationStateIds { get; init; }

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        // ... create diagnostic report ...

        var resultArray = new JsonArray();

        // Add references to observations by StateId
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

        node["result"] = resultArray;
    }
}
```

## Usage Examples

### Example 1: DiagnosticReport Referencing Observations

```csharp
var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(age: 45, gender: "male", familyName: "Smith")
    .AddEncounter("Annual physical")

    // Create observations with StateIds
    .AddState(new ObservationState
    {
        StateId = "obs_glucose",  // ✅ Explicit identifier
        Code = LabObservations.Glucose,
        Value = 105m,
        Unit = "mg/dL"
    })
    .AddState(new ObservationState
    {
        StateId = "obs_cholesterol",
        Code = LabObservations.TotalCholesterol,
        Value = 195m,
        Unit = "mg/dL"
    })

    // DiagnosticReport references observations by StateId
    .AddDiagnosticReport(new DiagnosticReportState
    {
        Code = DiagnosticReports.LipidPanel,
        ReferencedObservationStateIds = ["obs_glucose", "obs_cholesterol"]  // ✅ Clear references
    })

    .Build();
```

### Example 2: CareTeam Referencing Practitioners

```csharp
var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient()

    // Create practitioners with StateIds
    .AddState(new PractitionerState
    {
        StateId = "dr_smith",
        Specialty = Specialties.FamilyMedicine,
        GivenName = "John",
        FamilyName = "Smith"
    })
    .AddState(new PractitionerState
    {
        StateId = "dr_jones",
        Specialty = Specialties.Cardiology,
        GivenName = "Emily",
        FamilyName = "Jones"
    })

    // CareTeam references practitioners by StateId
    .AddCareTeam(new CareTeamState
    {
        Name = "Cardiac Care Team",
        ParticipantStateIds = ["dr_smith", "dr_jones"]  // ✅ Type-safe references
    })

    .Build();
```

### Example 3: Multi-Patient Scenario (ChainingSearchTests)

```csharp
// Smith's scenario
var smithScenario = new ScenarioBuilder(schemaProvider)
    .WithTag(testTag)
    .WithPatient(givenName: smithGivenName)
    .AddEncounter("Smith visit")
    .AddState(new ObservationState
    {
        StateId = "smith_snomed_obs",  // Unique per scenario
        Code = snomedCode,
        Value = 145m,
        Unit = "mg/dL"
    })
    .AddDiagnosticReport(new DiagnosticReportState
    {
        Code = snomedReportCode,
        ReferencedObservationStateIds = ["smith_snomed_obs"]
    })
    .Build();

// Access observation directly
var smithObservation = smithScenario.GetStateResource("smith_snomed_obs");
```

## Benefits

### ✅ Type Safety
```csharp
// Before (string attribute names):
assignToAttribute: "obs_glucos"  // Typo! No compile error

// After (StateId):
StateId = "obs_glucos"  // Still a string, but...
ReferencedObservationStateIds = ["obs_glucos"]  // Typo causes runtime null (easier to debug)
```

### ✅ Clear Intent
```csharp
// Before:
.AddObservation(code, value, unit, assignToAttribute: "obs1")  // What is "obs1"?

// After:
.AddState(new ObservationState {
    StateId = "obs_glucose",  // Self-documenting
    Code = glucose, ...
})
```

### ✅ Decoupling from Attributes
```csharp
// Before: Pollutes context.Attributes dictionary
context.Attributes["obs1"] = observation;  // Mixed with other state

// After: Separate namespace
context._stateResources["obs1"] = observation;  // Clear separation
```

### ✅ Reusable State Objects
```csharp
// Create state objects upfront
var glucoseObs = new ObservationState {
    StateId = "obs_glucose",
    Code = LabObservations.Glucose,
    Value = 105m,
    Unit = "mg/dL"
};

// Use in multiple scenarios
var scenario1 = new ScenarioBuilder(...)
    .AddState(glucoseObs)
    .Build();

var scenario2 = new ScenarioBuilder(...)
    .AddState(glucoseObs)  // Same state, different scenario
    .Build();
```

### ✅ Fluent API Compatibility
```csharp
// Still works with convenience methods
.AddObservation(code, value, unit)  // No StateId needed if not referenced

// Or explicit:
.AddState(new ObservationState { StateId = "obs1", ... })  // StateId when needed
```

## Implementation Changes

### Change 1: Update ScenarioState Base Class

**File:** `src/Core/Ignixa.FhirFakes/Scenarios/States/ScenarioState.cs`

```csharp
public abstract class ScenarioState
{
    public string Name { get; init; } = string.Empty;

    // NEW: StateId for cross-references
    public string? StateId { get; init; }

    public abstract void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker);
}
```

**Lines added:** 5

---

### Change 2: Update ScenarioContext

**File:** `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioContext.cs`

```csharp
public sealed class ScenarioContext
{
    // Existing fields...
    private readonly Dictionary<string, ResourceJsonNode> _stateResources = [];

    // NEW: Register resource by StateId
    internal void RegisterStateResource(string? stateId, ResourceJsonNode resource)
    {
        if (!string.IsNullOrEmpty(stateId))
        {
            _stateResources[stateId] = resource;
        }
    }

    // NEW: Get resource by StateId
    public ResourceJsonNode? GetStateResource(string stateId)
    {
        ArgumentException.ThrowIfNullOrEmpty(stateId);
        return _stateResources.TryGetValue(stateId, out var resource) ? resource : null;
    }
}
```

**Lines added:** 18

---

### Change 3: Update All State Classes to Register StateId

**Files to update:** (Add one line to Execute method)
- `ObservationState.cs`: `context.RegisterStateResource(StateId, observation);`
- `ConditionState.cs`: `context.RegisterStateResource(StateId, condition);`
- `MedicationOrderState.cs`: `context.RegisterStateResource(StateId, medication);`
- `ProcedureState.cs`: `context.RegisterStateResource(StateId, procedure);`
- `PractitionerState.cs`: `context.RegisterStateResource(StateId, practitioner);`
- `OrganizationState.cs`: `context.RegisterStateResource(StateId, organization);`
- `EncounterState.cs`: `context.RegisterStateResource(StateId, encounter);`
- `DiagnosticReportState.cs`: `context.RegisterStateResource(StateId, report);`

**Lines added per file:** 1
**Total files:** 8
**Total lines:** 8

---

### Change 4: Add ReferencedObservationStateIds to DiagnosticReportState

**File:** `src/Core/Ignixa.FhirFakes/Scenarios/States/DiagnosticReportState.cs`

```csharp
public sealed class DiagnosticReportState : ScenarioState
{
    // Existing properties...
    public IReadOnlyList<(FhirCode Code, decimal Value, string Unit)>? Observations { get; init; }

    // NEW: Reference observations by StateId
    public IReadOnlyList<string>? ReferencedObservationStateIds { get; init; }

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        // ... existing code ...

        var resultArray = new JsonArray();

        // NEW: Add references by StateId
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

        // Existing: Create new observations from tuples
        if (Observations is not null)
        {
            // ... existing code ...
        }

        node["result"] = resultArray;
    }
}
```

**Lines added:** 25

---

### Change 5: Add ParticipantStateIds to CareTeamState (when implemented)

**File:** `src/Core/Ignixa.FhirFakes/Scenarios/States/CareTeamState.cs` (new file)

```csharp
public sealed class CareTeamState : ScenarioState
{
    public required string Name { get; init; }

    // NEW: Reference practitioners by StateId
    public IReadOnlyList<string>? ParticipantStateIds { get; init; }

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        // ... create care team ...

        // Add participants by StateId
        if (ParticipantStateIds is not null)
        {
            var participantArray = new JsonArray();
            foreach (var stateId in ParticipantStateIds)
            {
                var practitioner = context.GetStateResource(stateId);
                if (practitioner is not null)
                {
                    participantArray.Add(new JsonObject
                    {
                        ["member"] = new JsonObject
                        {
                            ["reference"] = $"Practitioner/{practitioner.Id}"
                        }
                    });
                }
            }
            node["participant"] = participantArray;
        }
    }
}
```

## Total Changes Summary

| Change | File | Lines Added | Complexity |
|--------|------|-------------|------------|
| Add StateId property | ScenarioState.cs | 5 | Low |
| Add state resource registry | ScenarioContext.cs | 18 | Low |
| Register states in Execute | 8 state files | 8 (1 per file) | Low |
| Add ReferencedObservationStateIds | DiagnosticReportState.cs | 25 | Low |
| Add CareTeamState (optional) | CareTeamState.cs (new) | 200 | Medium |

**Total (without CareTeam):** 56 lines across 10 files
**Total (with CareTeam):** 256 lines across 11 files

**Estimated effort:** 1-2 hours

## Backward Compatibility

✅ **Fully backward compatible** - StateId is optional:

```csharp
// Old way (still works):
.AddObservation(code, value, unit)

// New way (when you need references):
.AddState(new ObservationState { StateId = "obs1", Code = code, ... })
```

## Decision

**Should we implement StateId pattern?**

**Benefits:**
- ✅ Cleaner than attribute names
- ✅ Self-documenting code
- ✅ Minimal changes (56 lines)
- ✅ Backward compatible
- ✅ Enables cross-state references without pollution

**My recommendation:** YES - This is elegant and solves the reference problem cleanly.
