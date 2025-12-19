# Investigation: ADR: Advanced FHIR Resource Scenario Generation (Synthea-Inspired)

**Feature**: fhir-faker
**Status**: Viable
**Created**: ** 2025-12-02

---


**Status:** Proposed
**Date:** 2025-12-02
**Context:** Phase 22 - Building realistic test data generation for E2E testing

---

## Problem Statement

The current `SchemaBasedFhirResourceFaker` generates individual FHIR resources with realistic field values, but it does not model:

1. **Longitudinal patient journeys** - A patient's medical history over time
2. **Resource relationships** - How Encounters, Conditions, Observations, Medications relate
3. **Clinical plausibility** - Disease progressions, treatment protocols, temporal ordering
4. **Scenario templates** - Pre-defined clinical workflows (e.g., "diabetic patient with hypertension")

This limits the realism and usefulness of generated test data for integration and E2E testing.

---

## Synthea Analysis: Key Patterns

After analyzing the Synthea source code (Java-based patient simulator), we identified these core patterns:

### 1. State Machine Architecture

**File:** `State.java`, `Module.java`

Synthea uses a **Finite State Machine (FSM)** approach where:
- Each clinical scenario is a JSON module with states
- States represent clinical events: `Initial`, `Delay`, `ConditionOnset`, `Encounter`, `MedicationOrder`, `Observation`, `Terminal`
- Transitions between states can be:
  - **Direct**: Always go to next state
  - **Conditional**: Based on attributes (age, gender, existing conditions)
  - **Distributed**: Probabilistic (85% chance of transition A, 15% chance of B)
  - **Complex**: Multiple conditions with logic (AND/OR)

**Example:** Allergic Rhinitis Module
```json
{
  "states": {
    "Initial": { "type": "Initial", "direct_transition": "Delay_For_Atopy" },
    "Delay_For_Atopy": {
      "type": "Delay",
      "exact": { "quantity": 1, "unit": "weeks" },
      "conditional_transition": [
        { "condition": { "attribute": "atopic", "operator": "is not nil" }, "transition": "Atopic" },
        { "transition": "Not_Atopic" }
      ]
    },
    "Atopic": {
      "type": "Simple",
      "distributed_transition": [
        { "distribution": 0.85, "transition": "Delay_Until_Early_Mid_Childhood" },
        { "distribution": 0.15, "transition": "Terminal" }
      ]
    },
    "Delay_Until_Early_Mid_Childhood": {
      "type": "Delay",
      "range": { "low": 2, "high": 6, "unit": "years" },
      "direct_transition": "Has_Allergic_Rhinitis"
    },
    "Has_Allergic_Rhinitis": {
      "type": "ConditionOnset",
      "assign_to_attribute": "allergic_rhinitis",
      "target_encounter": "Allergic_Rhinitis_Diagnosis",
      "codes": [{ "system": "SNOMED-CT", "code": "367498001", "display": "Seasonal allergic rhinitis" }],
      "direct_transition": "Allergic_Rhinitis_Symptom1"
    }
  }
}
```

### 2. Attribute-Based Patient State

**Files:** `Person.java`, `HealthRecord.java`

Patients maintain:
- **Demographics**: Age, gender, race, ethnicity
- **Attributes**: Key-value store for disease severity, risk factors (e.g., `diabetes_severity: 3`, `atopic: true`)
- **Health Record**: Timeline of Encounters, Conditions, Observations, Medications
- **Vital Signs**: BMI, blood pressure, glucose levels (updated over time)

### 3. Disease Progression with Severity Levels

**File:** `metabolic_syndrome/medications.json`

Diseases progress through stages, triggering treatment escalation:
```json
{
  "Monotherapy": {
    "conditional_transition": [{
      "condition": {
        "attribute": "diabetes_severity",
        "operator": ">=",
        "value": 2
      },
      "transition": "Prescribe_Metformin"
    }]
  },
  "Bitherapy": {
    "conditional_transition": [{
      "condition": {
        "attribute": "diabetes_severity",
        "operator": ">=",
        "value": 3
      },
      "transition": "Prescribe_Liraglutide"
    }]
  },
  "Insulin": {
    "conditional_transition": [{
      "condition": {
        "attribute": "diabetes_severity",
        "operator": ">=",
        "value": 5
      },
      "transition": "Prescribe_Insulin"
    }]
  }
}
```

### 4. Temporal Sequencing

**Delay State:**
- `exact`: Precise time (e.g., 1 week)
- `range`: Random within range (e.g., 2-6 years for childhood onset)

This ensures:
- Conditions appear at age-appropriate times
- Follow-up encounters happen at realistic intervals
- Medications are prescribed after diagnoses

### 5. Symptoms and Observations

**Symptom State:**
```json
{
  "Allergic_Rhinitis_Symptom1": {
    "type": "Symptom",
    "symptom": "Nasal Congestion",
    "range": { "low": 0, "high": 25 },
    "direct_transition": "Allergic_Rhinitis_Symptom2"
  }
}
```

Symptoms drive encounters and observations (e.g., high glucose triggers A1C test).

---

## Proposed Architecture for Ignixa

### Design Goals

1. **Incremental Adoption**: Start simple, extend over time
2. **Schema-Driven**: Leverage `IFhirSchemaProvider` for FHIR version compatibility
3. **Composable Scenarios**: Build complex journeys from simple building blocks
4. **Testable**: Easy to verify generated data meets expectations
5. **JSON-Based Templates**: External configuration, not hard-coded logic

### Phase 1: Scenario Builder (Foundation)

**New Classes:**

```csharp
// test/Ignixa.Api.E2ETests/Fakers/Scenarios/ScenarioGenerator.cs
public class ScenarioGenerator
{
    private readonly SchemaBasedFhirResourceFaker _resourceFaker;
    private readonly IFhirSchemaProvider _schemaProvider;

    public ScenarioContext GenerateScenario(string scenarioName)
    {
        // Load scenario template from JSON
        // Execute state machine
        // Return collection of related resources
    }
}

// test/Ignixa.Api.E2ETests/Fakers/Scenarios/ScenarioContext.cs
public class ScenarioContext
{
    public string ScenarioId { get; init; }
    public string ScenarioName { get; init; }
    public ResourceJsonNode Patient { get; init; }
    public List<ResourceJsonNode> Encounters { get; init; } = [];
    public List<ResourceJsonNode> Conditions { get; init; } = [];
    public List<ResourceJsonNode> Observations { get; init; } = [];
    public List<ResourceJsonNode> Medications { get; init; } = [];
    public List<ResourceJsonNode> Procedures { get; init; } = [];
    public Dictionary<string, object> Attributes { get; init; } = [];

    // Timeline of events (for temporal ordering)
    public List<ScenarioEvent> Timeline { get; init; } = [];
}

public record ScenarioEvent(DateTime Timestamp, string EventType, string ResourceId, string Description);

// test/Ignixa.Api.E2ETests/Fakers/Scenarios/ScenarioTemplate.cs
public class ScenarioTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, ScenarioState> States { get; set; } = [];
    public string InitialState { get; set; } = "Initial";
}

public abstract class ScenarioState
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ScenarioTransition? Transition { get; set; }

    public abstract Task<ScenarioStateResult> ExecuteAsync(ScenarioContext context, CancellationToken cancellationToken);
}

// State types (similar to Synthea)
public class InitialState : ScenarioState { }
public class DelayState : ScenarioState
{
    public TimeSpan? Exact { get; set; }
    public TimeSpan? RangeLow { get; set; }
    public TimeSpan? RangeHigh { get; set; }
}
public class ConditionOnsetState : ScenarioState
{
    public List<FhirCode> Codes { get; set; } = [];
    public string? AssignToAttribute { get; set; }
    public string? TargetEncounter { get; set; }
}
public class EncounterState : ScenarioState
{
    public string EncounterClass { get; set; } = "ambulatory";
    public string? Reason { get; set; }
    public List<FhirCode> Codes { get; set; } = [];
}
public class MedicationOrderState : ScenarioState
{
    public List<FhirCode> Codes { get; set; } = [];
    public string? Reason { get; set; }
    public bool Chronic { get; set; }
}
public class ObservationState : ScenarioState
{
    public List<FhirCode> Codes { get; set; } = [];
    public ObservationValue? Value { get; set; }
}
public class TerminalState : ScenarioState { }

// Transitions
public abstract class ScenarioTransition
{
    public abstract string? GetNextState(ScenarioContext context);
}

public class DirectTransition(string targetState) : ScenarioTransition
{
    public override string? GetNextState(ScenarioContext context) => targetState;
}

public class ConditionalTransition : ScenarioTransition
{
    public List<ConditionalTransitionOption> Options { get; set; } = [];

    public override string? GetNextState(ScenarioContext context)
    {
        foreach (var option in Options)
        {
            if (option.Condition is null || option.Condition.Evaluate(context))
            {
                return option.Transition;
            }
        }
        return null;
    }
}

public record ConditionalTransitionOption(ScenarioCondition? Condition, string Transition);

public abstract class ScenarioCondition
{
    public abstract bool Evaluate(ScenarioContext context);
}

public class AttributeCondition(string attribute, string op, object value) : ScenarioCondition
{
    public override bool Evaluate(ScenarioContext context)
    {
        if (!context.Attributes.TryGetValue(attribute, out var attrValue))
            return false;

        return op switch
        {
            "==" => Equals(attrValue, value),
            "!=" => !Equals(attrValue, value),
            ">" => Compare(attrValue, value) > 0,
            ">=" => Compare(attrValue, value) >= 0,
            "<" => Compare(attrValue, value) < 0,
            "<=" => Compare(attrValue, value) <= 0,
            "is not nil" => attrValue is not null,
            _ => false
        };
    }

    private static int Compare(object a, object b)
    {
        // Convert to comparable types
        return a switch
        {
            int intA when b is int intB => intA.CompareTo(intB),
            double dblA when b is double dblB => dblA.CompareTo(dblB),
            DateTime dtA when b is DateTime dtB => dtA.CompareTo(dtB),
            _ => 0
        };
    }
}

public class DistributedTransition : ScenarioTransition
{
    public List<DistributedTransitionOption> Options { get; set; } = [];

    public override string? GetNextState(ScenarioContext context)
    {
        var random = new Random();
        var value = random.NextDouble();
        var cumulative = 0.0;

        foreach (var option in Options)
        {
            cumulative += option.Distribution;
            if (value <= cumulative)
            {
                return option.Transition;
            }
        }

        return Options.LastOrDefault()?.Transition;
    }
}

public record DistributedTransitionOption(double Distribution, string Transition);
```

### Phase 2: JSON Scenario Templates

**Directory:** `test/Ignixa.Api.E2ETests/Fakers/Scenarios/Templates`

**Example:** `diabetic_patient.json`
```json
{
  "name": "Type 2 Diabetes with Hypertension",
  "description": "Patient diagnosed with Type 2 Diabetes, develops hypertension, receives medication escalation over time",
  "initialState": "Initial",
  "states": {
    "Initial": {
      "type": "Initial",
      "transition": { "type": "Direct", "target": "Generate_Patient" }
    },
    "Generate_Patient": {
      "type": "PatientGeneration",
      "ageRange": { "low": 45, "high": 65 },
      "attributes": {
        "diabetes_risk": 0.7,
        "hypertension_risk": 0.6
      },
      "transition": { "type": "Direct", "target": "Delay_Until_Diagnosis" }
    },
    "Delay_Until_Diagnosis": {
      "type": "Delay",
      "range": { "low": "1", "high": "6", "unit": "months" },
      "transition": { "type": "Direct", "target": "Initial_Symptoms" }
    },
    "Initial_Symptoms": {
      "type": "SetAttribute",
      "attribute": "diabetes_severity",
      "value": 1,
      "transition": { "type": "Direct", "target": "Initial_Encounter" }
    },
    "Initial_Encounter": {
      "type": "Encounter",
      "encounterClass": "ambulatory",
      "reason": "Routine checkup",
      "codes": [
        { "system": "SNOMED-CT", "code": "185349003", "display": "Encounter for check up" }
      ],
      "transition": { "type": "Direct", "target": "Glucose_Observation" }
    },
    "Glucose_Observation": {
      "type": "Observation",
      "codes": [
        { "system": "LOINC", "code": "2339-0", "display": "Glucose [Mass/volume] in Blood" }
      ],
      "value": {
        "type": "Range",
        "low": 126,
        "high": 180,
        "unit": "mg/dL"
      },
      "transition": { "type": "Direct", "target": "Diabetes_Diagnosis" }
    },
    "Diabetes_Diagnosis": {
      "type": "ConditionOnset",
      "assignToAttribute": "diabetes_condition",
      "codes": [
        { "system": "SNOMED-CT", "code": "44054006", "display": "Diabetes mellitus type 2" }
      ],
      "transition": { "type": "Direct", "target": "Check_Severity_For_Medication" }
    },
    "Check_Severity_For_Medication": {
      "type": "Simple",
      "transition": {
        "type": "Conditional",
        "options": [
          {
            "condition": {
              "type": "Attribute",
              "attribute": "diabetes_severity",
              "operator": ">=",
              "value": 1
            },
            "target": "Prescribe_Metformin"
          },
          { "target": "Follow_Up_Delay" }
        ]
      }
    },
    "Prescribe_Metformin": {
      "type": "MedicationOrder",
      "codes": [
        { "system": "RxNorm", "code": "860975", "display": "24 HR Metformin hydrochloride 500 MG Extended Release Oral Tablet" }
      ],
      "reason": "diabetes_condition",
      "chronic": true,
      "transition": { "type": "Direct", "target": "Follow_Up_Delay" }
    },
    "Follow_Up_Delay": {
      "type": "Delay",
      "exact": { "quantity": "3", "unit": "months" },
      "transition": { "type": "Direct", "target": "Follow_Up_Encounter" }
    },
    "Follow_Up_Encounter": {
      "type": "Encounter",
      "encounterClass": "ambulatory",
      "reason": "diabetes_condition",
      "codes": [
        { "system": "SNOMED-CT", "code": "390906007", "display": "Follow-up encounter" }
      ],
      "transition": { "type": "Direct", "target": "A1C_Observation" }
    },
    "A1C_Observation": {
      "type": "Observation",
      "codes": [
        { "system": "LOINC", "code": "4548-4", "display": "Hemoglobin A1c/Hemoglobin.total in Blood" }
      ],
      "value": {
        "type": "AttributeBased",
        "attribute": "diabetes_severity",
        "mapping": [
          { "severity": 1, "value": 7.2 },
          { "severity": 2, "value": 8.1 },
          { "severity": 3, "value": 9.3 }
        ],
        "unit": "%"
      },
      "transition": { "type": "Direct", "target": "Check_Disease_Progression" }
    },
    "Check_Disease_Progression": {
      "type": "Simple",
      "transition": {
        "type": "Distributed",
        "options": [
          { "distribution": 0.3, "target": "Increase_Severity" },
          { "distribution": 0.7, "target": "Terminal" }
        ]
      }
    },
    "Increase_Severity": {
      "type": "SetAttribute",
      "attribute": "diabetes_severity",
      "operation": "increment",
      "value": 1,
      "transition": { "type": "Direct", "target": "Check_Severity_For_Medication" }
    },
    "Terminal": {
      "type": "Terminal"
    }
  }
}
```

### Phase 3: Implementation Plan

**Step 1: Core State Machine Engine (Week 1)**
- Implement `ScenarioGenerator`, `ScenarioContext`, `ScenarioState` classes
- Support basic states: `Initial`, `Terminal`, `Simple`, `SetAttribute`
- Implement `DirectTransition` and `ConditionalTransition`
- Write unit tests for state transitions

**Step 2: FHIR Resource Generation States (Week 2)**
- Implement `ConditionOnsetState`, `EncounterState`, `ObservationState`, `MedicationOrderState`
- Integrate with `SchemaBasedFhirResourceFaker` for realistic field values
- Add reference linking (Encounter references Patient, Condition references Encounter)
- Write integration tests

**Step 3: Temporal Logic (Week 3)**
- Implement `DelayState` with exact and range delays
- Add timeline tracking to `ScenarioContext`
- Ensure resource timestamps are chronologically consistent
- Test multi-encounter scenarios

**Step 4: Advanced Conditions (Week 4)**
- Implement `DistributedTransition` (probabilistic)
- Implement `AttributeCondition` (age, severity, existing conditions)
- Add `AndCondition`, `OrCondition`, `NotCondition` for complex logic
- Test disease progression scenarios

**Step 5: JSON Template Loading (Week 5)**
- JSON deserialization for `ScenarioTemplate`
- Template validation (ensure all state transitions are valid)
- Error handling for malformed templates
- Create 3-5 common scenario templates (diabetes, hypertension, pregnancy, asthma, COVID-19)

**Step 6: Observation Value Strategies (Week 6)**
- Implement `ObservationValue` strategies:
  - `ExactValue`: Fixed value
  - `RangeValue`: Random within range
  - `AttributeBased`: Value depends on disease severity
  - `FormulaBased`: Calculate from other observations (e.g., BMI from height/weight)
- Test value generation for vital signs and lab results

---

## Benefits

1. **Realistic Test Data**: Scenarios model real patient journeys, not isolated resources
2. **Composable**: Build complex scenarios from simple states
3. **Data-Driven**: Change scenarios without code changes (JSON templates)
4. **Testable**: Verify E2E workflows with multi-resource dependencies
5. **Maintainable**: State machine pattern is well-understood and extensible
6. **FHIR Version Agnostic**: Uses `IFhirSchemaProvider`, works across R4/R4B/R5/STU3

---

## Example Usage

```csharp
// test/Ignixa.Api.E2ETests/Scenarios/DiabeticPatientScenarioTests.cs
public class DiabeticPatientScenarioTests
{
    private readonly ScenarioGenerator _scenarioGenerator;

    public DiabeticPatientScenarioTests()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var resourceFaker = new SchemaBasedFhirResourceFaker(schemaProvider);
        _scenarioGenerator = new ScenarioGenerator(resourceFaker, schemaProvider);
    }

    [Fact]
    public void GivenDiabeticScenario_WhenGenerating_ThenCreatesCompletePatientJourney()
    {
        // Act
        var scenario = _scenarioGenerator.GenerateScenario("diabetic_patient");

        // Assert
        scenario.Patient.Should().NotBeNull();
        scenario.Patient.ResourceType.Should().Be("Patient");

        scenario.Conditions.Should().ContainSingle(c =>
            c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "44054006"); // Diabetes

        scenario.Encounters.Should().HaveCountGreaterThan(1); // Initial + follow-up

        scenario.Observations.Should().Contain(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "2339-0"); // Glucose
        scenario.Observations.Should().Contain(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "4548-4"); // A1C

        scenario.Medications.Should().ContainSingle(m =>
            m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>() == "860975"); // Metformin

        // Verify temporal ordering
        var encounterDates = scenario.Encounters
            .Select(e => DateTime.Parse(e.MutableNode["period"]?["start"]?.GetValue<string>() ?? ""))
            .OrderBy(d => d)
            .ToList();
        encounterDates.Should().BeInAscendingOrder();
    }
}
```

---

## Alternatives Considered

### Alternative 1: Hard-Coded Scenario Classes
**Pros:** Type-safe, IDE support
**Cons:** Requires code changes for new scenarios, not data-driven

### Alternative 2: Completely Random Generation
**Pros:** Simple implementation
**Cons:** No clinical plausibility, hard to test specific workflows

### Alternative 3: Use External Libraries (e.g., Synthea directly)
**Pros:** Battle-tested, feature-rich
**Cons:** Java dependency, complex setup, limited customization for our needs

**Decision:** Proceed with JSON template-based state machine (inspired by Synthea) for flexibility and maintainability.

---

## Open Questions

1. **Performance:** How many scenarios can we generate per second? (Target: 100+ patients/sec)
2. **Complexity:** What's the maximum state count we expect? (Synthea has modules with 100+ states)
3. **Validation:** Should we validate generated resources against FHIR profiles? (Tier 1 validation?)
4. **Storage:** Do we persist generated scenarios for regression testing, or regenerate each time?

---

## Next Steps

1. **Prototype Phase 1** - Build core state machine engine (2-3 days)
2. **Validate Approach** - Get feedback from team on architecture
3. **Implement Phases 2-6** - Full scenario generation system (4-6 weeks)
4. **Create Scenario Library** - 10+ common clinical scenarios
5. **Integrate with E2E Tests** - Use scenarios for realistic integration testing

---

## References

- Synthea Source Code: `Old-src/Synthea`
- Synthea GitHub: https://github.com/synthetichealth/synthea
- FHIR Spec - Test Data: http://hl7.org/fhir/testscripts.html
- Current Schema-Based Faker: `test/Ignixa.Api.E2ETests/Fakers/SchemaBasedFhirResourceFaker.cs`
