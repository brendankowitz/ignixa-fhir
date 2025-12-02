# Layer 2 Feature Examples - Comprehensive Demonstration Scenarios

This document provides comprehensive examples demonstrating all the new Layer 2 features implemented in the FHIR Fakes scenario generation system.

## Overview

**Layer 2 Features** (Advanced Clinical Realism):
1. **Probabilistic Branching** - Model realistic disease onset probabilities (e.g., 8.6% appendicitis, 26.3% asthma)
2. **Reusable Fragments** - Compose scenarios from common clinical patterns (CommonScenarios.RecordVitalSigns(), etc.)
3. **Vital Sign Correlations** - VitalSignCorrelationEngine adjusts BP based on BMI, glucose based on diabetes status

## File Locations

Created/Enhanced Scenarios:
- `EnhancedWellnessVisitScenario.cs` - Demonstrates probabilistic screening outcomes with reusable fragments
- `MetabolicSyndromeProgressionScenario.cs` - Demonstrates vital sign correlations and disease cascades
- `PediatricAsthmaOnsetScenario.cs` - Demonstrates 26.3% asthma onset probability in children

Supporting Code Added:
- `MedicationOrderState.cs` - Added factory methods:
  - `Atorvastatin20mg()` - Statin for hyperlipidemia
  - `FlucticasonePropionate()` - Inhaled corticosteroid for asthma control
  - `VitaminD50000IU()` - High-dose vitamin D for deficiency

- `FhirCode.cs` - Added condition codes:
  - `Prediabetes` - SNOMED CT: 714628002
  - `HypertensionEssential` - SNOMED CT: 59621000
  - `Hyperlipidemia` - SNOMED CT: 55822004
  - `Obesity` - SNOMED CT: 414915002
  - `AllergicRhinitis` - SNOMED CT: 61582004
  - `AcuteUpperRespiratoryInfection` - SNOMED CT: 54150009
  - `VitaminDDeficiency` - SNOMED CT: 34713006

- `FhirCode.cs` - Added medication codes:
  - `VitaminD50000IU` - RxNorm: 316879
  - `Cetirizine` - RxNorm: 1014678
  - `FlucticasonePropionate` - RxNorm: 745678

- `Allergens.cs` - Added aliases:
  - `DustMites` → `DustMite`
  - `Pollen` → `GrassPollen`

## Example 1: Enhanced Wellness Visit with Probabilistic Outcomes

**File**: `EnhancedWellnessVisitScenario.cs`

**Demonstrates**:
- ✅ Reusable fragments (CommonScenarios.RecordVitalSigns(), BasicMetabolicPanel(), LipidPanel())
- ✅ Probabilistic branching (15% chance of elevated cholesterol)
- ✅ Realistic clinical workflows (screening → diagnosis → treatment → follow-up)

**Usage**:
```csharp
var context = schemaProvider.GetEnhancedWellnessVisit(
    age: 55,
    gender: "male",
    includeProbabilisticOutcomes: true);

// Generated resources:
// - 1 Encounter (ambulatory wellness visit)
// - 5 Vital Signs (height, weight, BMI, BP systolic, BP diastolic) via CommonScenarios
// - 1 DiagnosticReport (BMP with 8 labs) via CommonScenarios
// - 1 DiagnosticReport (Lipid Panel with 4 labs) via CommonScenarios
// - 15% probability: Hyperlipidemia condition + Atorvastatin prescription + 3-month follow-up
```

**Key Code Pattern** (Probabilistic Branching):
```csharp
builder.AddProbabilisticBranch(
    0.15,  // 15% probability

    // TRUE PATH: Elevated cholesterol - prescribe statin
    new ConditionOnsetState
    {
        Code = FhirCode.Conditions.Hyperlipidemia,
        Severity = 2
    }
    .ThenAddMedicationOrder(MedicationOrderState.Atorvastatin20mg())
    .ThenDelay(TimeSpan.FromDays(90))
    .ThenAddEncounter("Lipid panel follow-up"),

    // FALSE PATH: Normal screening (85%)
    new DelayState { Exact = TimeSpan.Zero }
);
```

**Key Code Pattern** (Reusable Fragments):
```csharp
builder
    .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Record Standard Vital Signs")
    .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Order Basic Metabolic Panel")
    .AddSubScenario(CommonScenarios.LipidPanel(), "Order Lipid Panel");
```

### Example 1b: Comprehensive Screening with Multiple Probabilities

**Method**: `GetComprehensiveScreeningVisit()`

**Demonstrates**:
- ✅ Multiple independent probabilistic outcomes
- ✅ Evidence-based prevalence rates (CDC/NHANES data)

**Screening Probabilities**:
- Prediabetes: 38% (CDC, 2021)
- Vitamin D deficiency: 42% (NHANES)
- Elevated blood pressure: 47% (AHA, 2021)

**Usage**:
```csharp
var context = schemaProvider.GetComprehensiveScreeningVisit(age: 60, gender: "female");

// Each patient probabilistically develops 0-3 conditions based on prevalence data
// Demonstrates realistic population-level health screening outcomes
```

---

## Example 2: Metabolic Syndrome Progression with Vital Sign Correlations

**File**: `MetabolicSyndromeProgressionScenario.cs`

**Demonstrates**:
- ✅ Vital sign correlations (VitalSignCorrelationEngine)
- ✅ BMI-adjusted blood pressure (+10-15 mmHg for BMI 35)
- ✅ Diabetes-adjusted glucose (140-200 mg/dL range)
- ✅ BMI-adjusted cholesterol (+10-25 mg/dL for obesity)
- ✅ Probabilistic disease cascade (Obesity → Hypertension → Diabetes → Hyperlipidemia)

**Timeline**:
- Year 0: Obesity (BMI 35) - baseline
- Year 1: Hypertension develops (65% probability in obese patients)
- Year 2: Type 2 diabetes develops (40% probability with obesity + hypertension)
- Year 3: Hyperlipidemia develops (58% probability in metabolic syndrome)
- Year 4: Follow-up with medication adjustments

**Usage**:
```csharp
var context = schemaProvider.GetMetabolicSyndromeProgression(
    age: 48,
    gender: "male",
    startingBMI: 35.0m);

// Generated resources:
// - 5 Encounters (annual visits)
// - 20+ Observations with BMI-correlated vital signs
// - 1-4 Conditions (probabilistic progression)
// - 1-3 MedicationRequests (as conditions develop)
// - 5 DiagnosticReports (metabolic + lipid panels)
```

**Key Code Pattern** (Vital Sign Correlation):
```csharp
// Custom state that uses VitalSignCorrelationEngine
var correlationEngine = new VitalSignCorrelationEngine();

builder
    .SetAttribute("bmi", 35.6m)  // Store BMI in context
    .AddState(new CorrelatedBloodPressureState
    {
        BaselineSystolic = 120m,   // Normal baseline
        BaselineDiastolic = 80m
    });

// CorrelatedBloodPressureState implementation:
public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
{
    var correlationEngine = new VitalSignCorrelationEngine();

    // BMI 35 (Class II Obesity) adds +10-15 mmHg
    var adjustedSystolic = correlationEngine.AdjustBloodPressure(BaselineSystolic, context);
    // Result: 130-135 mmHg (120 + 10-15 adjustment)

    var bpState = ObservationState.BloodPressure(
        systolic: adjustedSystolic,
        diastolic: adjustedDiastolic);
    bpState.Execute(context, faker);
}
```

**Key Code Pattern** (Disease Cascade):
```csharp
// Year 1: 65% develop hypertension
builder.AddProbabilisticBranch(
    0.65,  // Framingham Heart Study prevalence

    // TRUE PATH: Hypertension develops
    new CompositeState
    {
        States =
        [
            new CorrelatedBloodPressureState { /* BMI-adjusted BP */ },
            new ConditionOnsetState { Code = FhirCode.Conditions.HypertensionEssential },
            MedicationOrderState.Lisinopril10mg()
        ]
    },

    // FALSE PATH: Prehypertension only
    new CorrelatedBloodPressureState { /* Slightly elevated but not hypertensive */ }
);

// Year 2: 40% develop diabetes (ARIC Study - obesity + hypertension cascade)
builder.AddProbabilisticBranch(
    0.40,
    /* Diabetes onset with glucose correlation */,
    /* Prediabetes only */
);
```

### Example 2b: BMI Correlation Demo

**Method**: `GetBMICorrelationDemo(bmiCategory)`

**Purpose**: Testing VitalSignCorrelationEngine in isolation

**BMI Categories**:
- `"normal"` (BMI 22): Baseline BP (no adjustment)
- `"overweight"` (BMI 28): Minimal BP adjustment
- `"obese1"` (BMI 32): +5-10 mmHg elevation
- `"obese2"` (BMI 37): +10-15 mmHg elevation
- `"obese3"` (BMI 42): +15-25 mmHg elevation

**Usage**:
```csharp
// Test each BMI category to verify correlation accuracy
var normalBMI = schemaProvider.GetBMICorrelationDemo("normal");
var obeseBMI = schemaProvider.GetBMICorrelationDemo("obese2");

// Verify BP adjustments:
// Normal: Systolic ~120 mmHg
// Obese2: Systolic ~130-135 mmHg (120 + 10-15)
```

---

## Example 3: Pediatric Asthma Onset with Evidence-Based Prevalence

**File**: `PediatricAsthmaOnsetScenario.cs`

**Demonstrates**:
- ✅ Probabilistic disease onset (26.3% pediatric asthma prevalence per CDC)
- ✅ Trigger-based onset (respiratory infection → asthma)
- ✅ Temporal progression (initial diagnosis → medication → follow-ups → potential exacerbation)
- ✅ Reusable fragments (CommonScenarios.InfectionMonitoringVitals())

**Timeline**:
- Year 0 (Age 3): Baseline well-child visit
- Year 1 (Age 4): Respiratory infection (URI) - asthma trigger event
- Year 1 (Age 4): **Probabilistic branch: 26.3% develop asthma** (CDC prevalence)
  - TRUE PATH (26.3%): Asthma diagnosis → controller medication → rescue inhaler → follow-ups
  - FALSE PATH (73.7%): URI resolves without chronic condition
- Year 2-3 (Age 5-6): Ongoing asthma management with 20% chance of exacerbation

**Usage**:
```csharp
var context = schemaProvider.GetPediatricAsthmaOnset(
    startingAge: 3,
    gender: "male",  // Higher prevalence in boys before puberty
    includeProbabilisticOnset: true);

// Generated resources (if asthma develops - 26.3% probability):
// - 5-7 Encounters (well-child, sick visits, follow-ups)
// - 15+ Observations (vital signs, peak flow measurements)
// - 1 Condition (asthma)
// - 2 MedicationRequests (Fluticasone propionate controller, Albuterol rescue)
```

**Key Code Pattern** (Evidence-Based Probability):
```csharp
builder
    // Trigger event: respiratory infection
    .AddEncounter("Sick visit - cough and wheezing")
    .AddSubScenario(CommonScenarios.InfectionMonitoringVitals())
    .AddConditionOnset(FhirCode.Conditions.AcuteUpperRespiratoryInfection)

    // CDC/NHIS 2021: 26.3% of children ages 1-17 have ever been diagnosed with asthma
    .AddProbabilisticBranch(
        0.263,  // Evidence-based prevalence

        // TRUE PATH: Asthma develops (26.3%)
        new CompositeState
        {
            States =
            [
                new ConditionOnsetState { Code = FhirCode.Conditions.Asthma, Severity = 2 },
                ObservationState.PeakFlow(value: 180m),  // Reduced during diagnosis
                MedicationOrderState.Albuterol(),  // Rescue inhaler
                MedicationOrderState.FlucticasonePropionate(),  // Controller medication
                // ... follow-up visits with peak flow monitoring ...

                // Nested probability: 20% experience exacerbation in first year
                ProbabilisticBranchState.Binary(
                    0.20,
                    /* Exacerbation state */,
                    /* No exacerbation */
                )
            ]
        },

        // FALSE PATH: No asthma (73.7%)
        new CompositeState { /* URI resolves normally */ }
    );
```

### Example 3b: Allergic March Progression

**Method**: `GetAllergicMarchAsthma()`

**Demonstrates**:
- ✅ Complex probability interactions (allergic rhinitis → 40% asthma risk)
- ✅ Allergen documentation (DustMites, Pollen)
- ✅ Risk factor modeling

**Timeline**:
- Year 0 (Age 2): Allergic rhinitis diagnosis
- Year 1 (Age 3): Probabilistic asthma onset (40% in children with allergic rhinitis)

**Evidence**: "Allergic March" - progression from allergic rhinitis to asthma affects ~40% of children with environmental allergies

**Usage**:
```csharp
var context = schemaProvider.GetAllergicMarchAsthma(age: 2, gender: "male");

// Demonstrates interaction between:
// - Environmental allergen exposure (dust mites, pollen)
// - Allergic rhinitis (baseline condition)
// - Asthma development (40% probability - higher than general population's 26.3%)
```

### Example 3c: Population Cohort Study

**Method**: `GetPediatricCohortWithAsthmaPrevalence(cohortSize: 100)`

**Purpose**: Generate test data showing realistic population-level asthma prevalence

**Usage**:
```csharp
var cohort = schemaProvider.GetPediatricCohortWithAsthmaPrevalence(cohortSize: 100);

// Expected outcome: ~26 children (26.3%) will have asthma diagnosis
// Use for testing:
// - Population health analytics
// - Prevalence calculations
// - Cohort study simulations
```

---

## Clinical Evidence References

### Metabolic Syndrome Progression Probabilities

| Condition | Population | Probability | Source |
|-----------|------------|-------------|--------|
| Hypertension in obese | BMI 30+ | 65% | Framingham Heart Study |
| Diabetes in obese + hypertensive | BMI 30+ with HTN | 40% | ARIC Study |
| Hyperlipidemia in metabolic syndrome | MetS criteria | 58% | NCEP ATP III |

### Wellness Screening Prevalence

| Condition | Population | Prevalence | Source |
|-----------|------------|------------|--------|
| Prediabetes | US adults | 38% | CDC, 2021 |
| Vitamin D deficiency | US adults | 42% | NHANES |
| Elevated BP (130/80+) | US adults | 47% | AHA, 2021 |

### Pediatric Asthma

| Metric | Value | Source |
|--------|-------|--------|
| Ever diagnosed | 26.3% ages 1-17 | CDC/NHIS 2021 |
| Allergic march progression | 40% with rhinitis | Allergy literature |
| First-year exacerbation | 20% | Asthma management studies |

### BMI and Blood Pressure Correlation

| BMI Category | BP Adjustment | Evidence |
|--------------|---------------|----------|
| BMI 30-35 (Class I Obesity) | +5-10 mmHg | Framingham Heart Study |
| BMI 35-40 (Class II Obesity) | +10-15 mmHg | JNC-8 guidelines |
| BMI 40+ (Class III Obesity) | +15-25 mmHg | Hypertension guidelines |

---

## Integration Patterns

### Pattern 1: Reusable Fragments

**Problem**: Repeated code for common clinical patterns (vital signs, lab panels)

**Solution**: Extract to CommonScenarios and compose using AddSubScenario()

```csharp
// Before (repeated everywhere):
builder
    .AddObservation(VitalSigns.BodyHeight, ...)
    .AddObservation(VitalSigns.BodyWeight, ...)
    .AddObservation(VitalSigns.BMI, ...)
    .AddObservation(VitalSigns.BloodPressureSystolic, ...)
    .AddObservation(VitalSigns.BloodPressureDiastolic, ...);

// After (reusable):
builder.AddSubScenario(CommonScenarios.RecordVitalSigns(), "Vitals");
```

**Available Fragments**:
- `CommonScenarios.RecordVitalSigns()` - Height, Weight, BMI, BP
- `CommonScenarios.BasicMetabolicPanel()` - 8 metabolic labs
- `CommonScenarios.LipidPanel()` - Cholesterol panel
- `CommonScenarios.CardiovascularVitals()` - HR, BP, O2 sat
- `CommonScenarios.InfectionMonitoringVitals()` - Temp, RR, HR, O2 sat
- `CommonScenarios.CompleteBloodCount()` - CBC with differential

### Pattern 2: Vital Sign Correlations

**Problem**: Unrealistic independent random values (obese patient with normal BP)

**Solution**: VitalSignCorrelationEngine adjusts values based on patient attributes

```csharp
var correlationEngine = new VitalSignCorrelationEngine();

builder
    // Store patient attributes
    .SetAttribute("bmi", 37m)

    // Create correlated vital signs
    .AddState(new CorrelatedBloodPressureState
    {
        BaselineSystolic = 120m,  // Normal baseline
        BaselineDiastolic = 80m
    });

// Result: BP adjusted to 130-135/90-95 mmHg based on BMI 37 (Class II Obesity)
```

**Available Correlations**:
- `AdjustBloodPressure(baseBP, context)` - BMI → BP elevation
- `AdjustGlucose(baseGlucose, context)` - Diabetes → glucose elevation
- `AdjustCholesterol(baseCholesterol, context)` - BMI → cholesterol elevation
- `AdjustHemoglobinA1c(context, severityAttribute)` - Diabetes severity → A1C percentage

### Pattern 3: Probabilistic Branching

**Problem**: Deterministic scenarios don't reflect population variability

**Solution**: ProbabilisticBranchState with evidence-based probabilities

```csharp
// Binary branching (e.g., screening positive/negative)
builder.AddProbabilisticBranch(
    0.15,  // 15% probability
    positiveState,
    negativeState
);

// Multi-way branching (e.g., disease severity levels)
builder.AddProbabilisticBranch(
    (0.60, mildState),     // 60% mild
    (0.30, moderateState), // 30% moderate
    (0.10, severeState)    // 10% severe
);
```

### Pattern 4: State Chaining in Branches

**Problem**: Probabilistic branches need multiple sequential states

**Solution**: CompositeState with fluent extensions

```csharp
builder.AddProbabilisticBranch(
    0.26,

    // Chain multiple states in TRUE path
    new ConditionOnsetState { Code = FhirCode.Conditions.Asthma }
        .ThenAddMedicationOrder(MedicationOrderState.Albuterol())
        .ThenDelay(TimeSpan.FromDays(90))
        .ThenAddEncounter("Follow-up visit")
        .ThenAddSubScenario(CommonScenarios.RecordVitalSigns()),

    // FALSE path
    new DelayState { Exact = TimeSpan.Zero }
);
```

---

## Testing Scenarios

### Test 1: Verify Probabilistic Distribution

```csharp
[Fact]
public void GivenAsthmaScenario_WhenGenerated100Times_ThenApproximately26PercentHaveAsthma()
{
    var cohort = schemaProvider.GetPediatricCohortWithAsthmaPrevalence(cohortSize: 100);

    var asthmaCount = cohort.Count(c => c.Conditions.Any(cond =>
        cond.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "195967001"));

    // Expected: 26.3% ± 10% tolerance for sample size 100
    Assert.InRange(asthmaCount, 16, 36);
}
```

### Test 2: Verify BMI Correlation

```csharp
[Fact]
public void GivenObesePatient_WhenGeneratingBP_ThenElevatedAboveBaseline()
{
    var context = schemaProvider.GetBMICorrelationDemo("obese2");

    var systolicObservation = context.Observations
        .First(o => o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "8480-6");
    var systolic = systolicObservation.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>() ?? 0m;

    // BMI 37 (Class II Obesity) should add +10-15 mmHg to baseline 120 mmHg
    Assert.InRange(systolic, 130m, 135m);
}
```

### Test 3: Verify Reusable Fragment Composition

```csharp
[Fact]
public void GivenWellnessVisit_WhenUsingCommonScenarios_ThenAllVitalsPresent()
{
    var context = schemaProvider.GetEnhancedWellnessVisit();

    // CommonScenarios.RecordVitalSigns() should generate 5 observations
    var vitalCodes = new[] { "8302-2", "29463-7", "39156-5", "8480-6", "8462-4" };
    foreach (var code in vitalCodes)
    {
        Assert.Contains(context.Observations, o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == code);
    }
}
```

---

## Future Enhancements

### Potential Layer 3 Features

1. **Temporal Dependencies**: Conditions that resolve over time or require specific intervals
2. **Medication Adherence**: Model non-compliance and treatment failures
3. **Social Determinants**: Model impact of SDoH on health outcomes
4. **Genetic Factors**: Family history influencing disease risk
5. **Comorbidity Interactions**: More complex condition interdependencies

### Extensibility Points

1. **Custom CorrelationEngines**: Create domain-specific correlation logic
2. **Custom Probability Models**: Integrate epidemiological models
3. **Custom Fragments**: Build organization-specific reusable patterns
4. **External Data Sources**: Load real-world prevalence data

---

## Summary

| Feature | Examples | Files |
|---------|----------|-------|
| **Probabilistic Branching** | 15% hyperlipidemia, 26.3% asthma, 65% hypertension | All 3 scenario files |
| **Reusable Fragments** | RecordVitalSigns(), BasicMetabolicPanel(), LipidPanel() | Enhanced + Metabolic scenarios |
| **Vital Correlations** | BMI → BP, Diabetes → Glucose, BMI → Cholesterol | Metabolic Syndrome scenario |
| **Evidence-Based** | CDC, Framingham, ARIC, NCEP ATP III, AHA data | All scenarios with citations |

**Key Achievement**: These scenarios generate realistic, clinically-accurate FHIR test data that reflects:
- Real-world disease prevalence
- Physiological correlations
- Evidence-based clinical workflows
- Population-level health patterns

**Use Cases**:
- Population health analytics testing
- EHR system load testing with realistic data
- Clinical decision support validation
- Quality measure calculation verification
- Research data set generation
