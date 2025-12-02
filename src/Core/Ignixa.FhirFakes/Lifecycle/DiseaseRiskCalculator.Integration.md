# DiseaseRiskCalculator Integration with ProbabilisticConditionOnset

## Overview

This document demonstrates how `DiseaseRiskCalculator` integrates with `ProbabilisticConditionOnset` to create realistic, age-dependent disease onset patterns in patient lifecycle simulations.

## Architecture

```
DiseaseRiskCalculator (Layer 3)
    ↓ Calculates risk based on patient attributes
ProbabilisticConditionOnset (Layer 3)
    ↓ Uses calculated risk as probability
PatientLifecycleGenerator (Layer 3)
    ↓ Orchestrates lifecycle events
ScenarioBuilder (Layer 2)
    ↓ Generates FHIR resources
```

## Integration Pattern

### Basic Usage

```csharp
var calculator = new DiseaseRiskCalculator();

// Calculate diabetes risk for a specific patient profile
var diabetesRisk = calculator.CalculateDiabetesRisk(
    age: 50,
    smoker: true,
    bmi: 32m,
    familyHistory: true
); // Returns: 0.60 (60% risk with multiple factors)

// Create a probabilistic event with calculated risk
var diabetesOnset = new ProbabilisticConditionOnset(
    conditionName: "Type2Diabetes",
    onsetAges: 40..70,
    probability: diabetesRisk,  // Use calculated risk
    scenarioFactory: sp => new ScenarioBuilder(sp)
        .AddConditionOnset(FhirCode.Conditions.Type2Diabetes)
        .AddMedicationOrder(FhirCode.Medications.Metformin)
        .AddObservation(FhirCode.Observations.HbA1c, value: 7.5m)
);

// Add to lifecycle generator
lifecycleGenerator.AddEvent(diabetesOnset);
```

### Advanced: Dynamic Risk Calculation

For more complex scenarios where risk changes based on patient state:

```csharp
public class AdaptiveDiseaseRiskEvent : ILifecycleEvent
{
    private readonly DiseaseRiskCalculator _calculator = new();
    private bool _hasOccurred;

    public string Name => "AdaptiveDiabetesRisk";

    public bool IsApplicable(int patientAge) => !_hasOccurred && patientAge >= 30;

    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        // Read current patient attributes from context
        var age = context.CurrentAge;
        var bmi = context.GetAttribute<decimal>("current_bmi");
        var isSmoker = context.GetAttribute<bool>("is_smoker");
        var familyHistory = context.GetAttribute<bool>("diabetes_family_history");

        // Calculate dynamic risk based on current state
        var currentRisk = _calculator.CalculateDiabetesRisk(age, isSmoker, bmi, familyHistory);

        // Probability check
        if (Random.Shared.NextDouble() < currentRisk)
        {
            _hasOccurred = true;

            // Generate condition onset scenario
            var scenarioBuilder = new ScenarioBuilder(schemaProvider)
                .AddConditionOnset(FhirCode.Conditions.Type2Diabetes)
                .AddMedicationOrder(FhirCode.Medications.Metformin);

            var states = scenarioBuilder.GetStates();
            var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

            foreach (var state in states)
            {
                state.Execute(context, faker);
            }

            // Mark that patient now has diabetes (affects other risk calculations)
            context.SetAttribute("has_diabetes", true);
        }
    }
}
```

## Complete Example: Multi-Disease Lifecycle

```csharp
public class RealisticPatientLifecycle
{
    private readonly DiseaseRiskCalculator _calculator = new();
    private readonly PatientLifecycleGenerator _generator = new();

    public void ConfigureLifecycle(
        int patientAge,
        decimal bmi,
        bool isSmoker,
        bool hasFamilyHistoryDiabetes,
        bool hasFamilyHistoryCancer,
        bool hasAllergies)
    {
        // 1. Calculate all disease risks
        var diabetesRisk = _calculator.CalculateDiabetesRisk(
            patientAge, isSmoker, bmi, hasFamilyHistoryDiabetes);

        var hypertensionRisk = _calculator.CalculateHypertensionRisk(
            patientAge, bmi, hasDiabetes: false); // Will recalculate if diabetes occurs

        var asthmaRisk = _calculator.CalculateAsthmaRisk(
            patientAge, hasAllergies);

        var cancerRisk = _calculator.CalculateCancerRisk(
            patientAge, isSmoker, hasFamilyHistoryCancer);

        var strokeRisk = _calculator.CalculateStrokeRisk(
            patientAge,
            hasHypertension: false,  // Will recalculate if hypertension occurs
            hasDiabetes: false,      // Will recalculate if diabetes occurs
            isSmoker);

        // 2. Create probabilistic events with calculated risks
        _generator.AddEvent(new ProbabilisticConditionOnset(
            "Type2Diabetes",
            onsetAges: 30..80,
            probability: diabetesRisk,
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset("E11.9", "Type 2 Diabetes Mellitus")
                .AddMedicationOrder("metformin", "Metformin 500mg")
                .AddObservation("4548-4", "HbA1c", value: 7.5m)));

        _generator.AddEvent(new ProbabilisticConditionOnset(
            "Hypertension",
            onsetAges: 30..80,
            probability: hypertensionRisk,
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset("I10", "Essential Hypertension")
                .AddMedicationOrder("lisinopril", "Lisinopril 10mg")
                .AddObservation("85354-9", "Blood Pressure", systolic: 145m, diastolic: 95m)));

        _generator.AddEvent(new ProbabilisticConditionOnset(
            "Asthma",
            onsetAges: 1..65,
            probability: asthmaRisk,
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset("J45.909", "Asthma, Unspecified")
                .AddMedicationOrder("albuterol", "Albuterol Inhaler")));

        _generator.AddEvent(new ProbabilisticConditionOnset(
            "Cancer",
            onsetAges: 40..90,
            probability: cancerRisk,
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset("C50.919", "Malignant Neoplasm")
                .AddProcedure("77427", "Radiation Treatment")));

        _generator.AddEvent(new ProbabilisticConditionOnset(
            "Stroke",
            onsetAges: 50..90,
            probability: strokeRisk,
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset("I63.9", "Cerebral Infarction")
                .AddEncounter("emergency", "Emergency Department Visit")
                .AddProcedure("70450", "CT Head without Contrast")));

        // 3. Add wellness visits
        _generator.AddEvent(new AdultWellnessSchedule());

        // 4. Add immunizations
        _generator.AddEvent(new ImmunizationScheduleEvent());
    }

    public List<ResourceJsonNode> GenerateLifecycle(
        DateOnly birthDate,
        DateOnly endDate,
        IFhirSchemaProvider schemaProvider)
    {
        return _generator.Generate(birthDate, endDate, schemaProvider);
    }
}
```

## Cross-Disease Comorbidity Modeling

### Pattern: Cascading Risk Updates

```csharp
public class CascadingDiseaseRiskLifecycle
{
    private readonly DiseaseRiskCalculator _calculator = new();
    private readonly PatientLifecycleGenerator _generator = new();

    public void ConfigureComorbidityAwareLifecycle(
        int patientAge,
        decimal bmi,
        bool isSmoker)
    {
        // Phase 1: Primary conditions (diabetes, obesity-related)
        var diabetesRisk = _calculator.CalculateDiabetesRisk(
            patientAge, isSmoker, bmi, familyHistory: false);

        _generator.AddEvent(new ProbabilisticConditionOnset(
            "Diabetes",
            onsetAges: 30..70,
            probability: diabetesRisk,
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset("E11.9", "Type 2 Diabetes")
                .OnComplete(context => {
                    // Mark diabetes occurrence
                    context.SetAttribute("has_diabetes", true);

                    // Recalculate hypertension risk with diabetes
                    var updatedHtnRisk = _calculator.CalculateHypertensionRisk(
                        context.CurrentAge, bmi, hasDiabetes: true);

                    // Add new hypertension event with updated risk
                    _generator.AddEvent(new ProbabilisticConditionOnset(
                        "Hypertension",
                        onsetAges: context.CurrentAge..80,
                        probability: updatedHtnRisk,
                        scenarioFactory: sp2 => new ScenarioBuilder(sp2)
                            .AddConditionOnset("I10", "Hypertension")
                            .OnComplete(ctx2 => {
                                ctx2.SetAttribute("has_hypertension", true);

                                // Recalculate stroke risk with both diabetes and hypertension
                                var updatedStrokeRisk = _calculator.CalculateStrokeRisk(
                                    ctx2.CurrentAge,
                                    hasHypertension: true,
                                    hasDiabetes: true,
                                    isSmoker);

                                // Add stroke event with significantly increased risk
                                _generator.AddEvent(new ProbabilisticConditionOnset(
                                    "Stroke",
                                    onsetAges: ctx2.CurrentAge..90,
                                    probability: updatedStrokeRisk,
                                    scenarioFactory: sp3 => new ScenarioBuilder(sp3)
                                        .AddConditionOnset("I63.9", "Stroke")));
                            })));
                })));
    }
}
```

## Real-World Example: 50-Year-Old Patient

```csharp
// Patient profile
var age = 50;
var bmi = 32m;  // Obese
var isSmoker = true;
var diabetesFamilyHistory = true;
var cancerFamilyHistory = false;
var hasAllergies = false;

var calculator = new DiseaseRiskCalculator();

// Calculate all risks
var risks = new Dictionary<string, double>
{
    ["Diabetes"] = calculator.CalculateDiabetesRisk(age, isSmoker, bmi, diabetesFamilyHistory),
    // Result: 0.15 * 2.0 (obesity) * 1.5 (smoking) * 2.0 (family) = 0.90 (90%)

    ["Hypertension"] = calculator.CalculateHypertensionRisk(age, bmi, hasDiabetes: false),
    // Result: 0.296 + 0.15 (obesity) = 0.446 (44.6%)

    ["Asthma"] = calculator.CalculateAsthmaRisk(age, hasAllergies),
    // Result: 0.351 (35.1% baseline for middle age)

    ["Cancer"] = calculator.CalculateCancerRisk(age, isSmoker, cancerFamilyHistory),
    // Result: 0.04 * 2.5 (smoking) = 0.10 (10%)

    ["Stroke"] = calculator.CalculateStrokeRisk(age, hasHypertension: false, hasDiabetes: false, isSmoker)
    // Result: 0.02 + 0.04 (smoking) = 0.06 (6%)
};

// Expected outcomes over 20-year simulation:
// - Diabetes: 90% chance → Likely to occur
// - Hypertension: 44.6% chance → Moderate probability
// - Stroke: 6% chance initially, BUT if diabetes + hypertension occur, risk becomes:
//   → 0.05 (age 60-64) + 0.08 (hypertension) + 0.05 (diabetes) + 0.04 (smoking) = 0.22 (22%)
```

## Testing Strategy

```csharp
[Fact]
public void GivenHighRiskPatient_WhenSimulatingLifecycle_ThenDiabetesOccursWithHighProbability()
{
    // Arrange
    var calculator = new DiseaseRiskCalculator();
    var risk = calculator.CalculateDiabetesRisk(age: 60, smoker: true, bmi: 35m, familyHistory: true);

    // Assert risk is high
    risk.Should().BeGreaterThan(0.80, "high-risk profile should yield >80% probability");

    // Arrange lifecycle
    var generator = new PatientLifecycleGenerator();
    generator.AddEvent(new ProbabilisticConditionOnset(
        "Diabetes",
        onsetAges: 50..70,
        probability: risk,
        scenarioFactory: sp => new ScenarioBuilder(sp)
            .AddConditionOnset("E11.9", "Type 2 Diabetes")));

    // Act - simulate 100 patients to test probability distribution
    var diabetesCount = 0;
    for (int i = 0; i < 100; i++)
    {
        var resources = generator.Generate(
            birthDate: new DateOnly(1960, 1, 1),
            endDate: new DateOnly(2025, 1, 1),
            schemaProvider);

        if (resources.Any(r => r.ResourceType == "Condition" &&
            r.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "E11.9"))
        {
            diabetesCount++;
        }
    }

    // Assert - should occur in ~80-100 out of 100 patients (high risk)
    diabetesCount.Should().BeInRange(75, 100, "high-risk patients should develop diabetes in majority of simulations");
}
```

## Performance Considerations

### Caching Calculated Risks

For large-scale simulations (1000+ patients):

```csharp
public class CachedDiseaseRiskCalculator
{
    private readonly DiseaseRiskCalculator _calculator = new();
    private readonly Dictionary<string, double> _riskCache = new();

    public double GetDiabetesRisk(int age, bool smoker, decimal bmi, bool familyHistory)
    {
        var key = $"diabetes_{age}_{smoker}_{bmi}_{familyHistory}";

        if (!_riskCache.TryGetValue(key, out var risk))
        {
            risk = _calculator.CalculateDiabetesRisk(age, smoker, bmi, familyHistory);
            _riskCache[key] = risk;
        }

        return risk;
    }

    // ... similar methods for other diseases
}
```

## Summary

The `DiseaseRiskCalculator` provides:

1. **Evidence-based probabilities** for 5+ major diseases
2. **Age-stratified risk models** matching real-world epidemiology
3. **Risk multipliers** for lifestyle and genetic factors
4. **Comorbidity awareness** (diabetes increases hypertension/stroke risk)
5. **Seamless integration** with `ProbabilisticConditionOnset` and lifecycle system

This enables realistic patient simulations where disease onset patterns match clinical literature and population health data.
