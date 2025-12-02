# VitalSignCorrelationEngine Usage Examples

This document demonstrates how to use the `VitalSignCorrelationEngine` class to create physiologically realistic correlations in FHIR test data.

## Overview

The `VitalSignCorrelationEngine` provides methods to adjust vital signs and lab values based on patient attributes and conditions:

- **BMI → Blood Pressure**: Higher BMI increases systolic/diastolic pressure
- **BMI → Cholesterol**: Obesity elevates total cholesterol and LDL
- **Diabetes → Glucose**: Diabetic patients have elevated fasting glucose (140-200 mg/dL)
- **Diabetes Severity → A1C**: Uncontrolled diabetes results in higher A1C percentages

## Basic Usage

### 1. Calculate and Store BMI

```csharp
var correlationEngine = new VitalSignCorrelationEngine();
var context = new ScenarioContext();

// Patient is 175cm tall and weighs 95kg
correlationEngine.CalculateAndStoreBMI(context, heightCm: 175m, weightKg: 95m);

// BMI is now stored in context: 95 / (1.75^2) = 31.0 (Class I Obesity)
var bmi = context.GetAttribute<decimal>("bmi"); // 31.0
```

### 2. Adjust Blood Pressure Based on BMI

```csharp
var correlationEngine = new VitalSignCorrelationEngine();

// Baseline blood pressure for normal BMI patient
var baseSystolic = 120m;

// Patient has BMI stored in context (e.g., BMI = 38)
context.SetAttribute("bmi", 38m);

// Adjust based on BMI (Class II Obesity: +10-15 mmHg)
var adjustedSystolic = correlationEngine.AdjustBloodPressure(baseSystolic, context);
// Result: 130-135 mmHg (120 + 10-15 adjustment)
```

### 3. Adjust Glucose Based on Diabetes Status

```csharp
var correlationEngine = new VitalSignCorrelationEngine();

// Baseline glucose for normal patient
var baseGlucose = 90m; // mg/dL

// Patient WITHOUT diabetes
var glucoseNormal = correlationEngine.AdjustGlucose(baseGlucose, contextWithoutDiabetes);
// Result: 90 mg/dL (no adjustment)

// Patient WITH diabetes (has Condition with SNOMED code 44054006)
var glucoseDiabetic = correlationEngine.AdjustGlucose(baseGlucose, contextWithDiabetes);
// Result: 140-200 mg/dL (diabetic range)
```

### 4. Adjust Cholesterol Based on BMI

```csharp
var correlationEngine = new VitalSignCorrelationEngine();

// Normal total cholesterol baseline
var baseCholesterol = 180m; // mg/dL

// Patient with BMI 42 (Class III Obesity)
context.SetAttribute("bmi", 42m);

var adjustedCholesterol = correlationEngine.AdjustCholesterol(baseCholesterol, context);
// Result: 210-230 mg/dL (180 + 30-50 adjustment)
```

### 5. Calculate A1C Based on Diabetes Severity

```csharp
var correlationEngine = new VitalSignCorrelationEngine();

// Patient with moderate diabetes (severity 2)
context.SetAttribute("diabetes_condition_severity", 2);

var a1c = correlationEngine.AdjustHemoglobinA1c(context, "diabetes_condition_severity");
// Result: 7.5-8.5% (moderate control)

// Patient with severe diabetes (severity 4)
context.SetAttribute("diabetes_condition_severity", 4);

var a1cSevere = correlationEngine.AdjustHemoglobinA1c(context, "diabetes_condition_severity");
// Result: 10.0-11.5% (very poor control)
```

## Integration with ScenarioBuilder

### Example: Obese Patient with Hypertension

```csharp
public static ScenarioContext GetObeseHypertensivePatient(this IFhirSchemaProvider schemaProvider)
{
    var correlationEngine = new VitalSignCorrelationEngine();

    var context = new ScenarioBuilder(schemaProvider)
        .WithName("Obese Patient with Hypertension")
        .WithPatient(age: 55, gender: "male")
        .AddEncounter("Annual wellness visit")

        // Record height and weight
        .AddObservation(ObservationState.BodyHeight(value: 175m))
        .AddObservation(ObservationState.BodyWeight(value: 110m))
        .Build();

    // Calculate BMI and store in context
    correlationEngine.CalculateAndStoreBMI(context, heightCm: 175m, weightKg: 110m);
    // BMI = 35.9 (Class II Obesity)

    // Now create blood pressure observation with BMI-adjusted values
    var baseSystolic = 120m;
    var baseDiastolic = 80m;

    var adjustedSystolic = correlationEngine.AdjustBloodPressure(baseSystolic, context);
    // Result: 130-135 mmHg (+10-15 for BMI 35.9)

    var adjustedDiastolic = correlationEngine.AdjustBloodPressure(baseDiastolic, context);
    // Result: 90-95 mmHg

    // Add blood pressure observation with realistic values
    var bpState = ObservationState.BloodPressure(
        systolic: adjustedSystolic,
        diastolic: adjustedDiastolic);
    bpState.Execute(context, new SchemaBasedFhirResourceFaker(schemaProvider));

    return context;
}
```

### Example: Diabetic Patient with Obesity

```csharp
public static ScenarioContext GetDiabeticObesePatient(this IFhirSchemaProvider schemaProvider)
{
    var correlationEngine = new VitalSignCorrelationEngine();

    var context = new ScenarioBuilder(schemaProvider)
        .WithName("Type 2 Diabetes with Obesity")
        .WithPatient(age: 62, gender: "female")
        .AddEncounter("Routine checkup")

        // Vital signs
        .AddObservation(ObservationState.BodyHeight(value: 160m))
        .AddObservation(ObservationState.BodyWeight(value: 85m))
        .Build();

    // Calculate BMI (33.2 - Class I Obesity)
    correlationEngine.CalculateAndStoreBMI(context, heightCm: 160m, weightKg: 85m);

    // Add diabetes condition
    var conditionState = new ConditionOnsetState
    {
        Code = FhirCode.Conditions.DiabetesType2,
        Severity = 2
    };
    conditionState.Execute(context, new SchemaBasedFhirResourceFaker(schemaProvider));

    // Adjust glucose for diabetes (140-200 mg/dL)
    var baseGlucose = 90m;
    var adjustedGlucose = correlationEngine.AdjustGlucose(baseGlucose, context);
    // Result: 140-200 mg/dL (diabetic range)

    // Adjust blood pressure for BMI
    var baseSystolic = 120m;
    var adjustedSystolic = correlationEngine.AdjustBloodPressure(baseSystolic, context);
    // Result: 125-130 mmHg (+5-10 for BMI 33.2)

    // Adjust cholesterol for BMI
    var baseCholesterol = 180m;
    var adjustedCholesterol = correlationEngine.AdjustCholesterol(baseCholesterol, context);
    // Result: 190-205 mg/dL (+10-25 for BMI 33.2)

    return context;
}
```

## Advanced Usage: Custom Observation States

You can integrate the correlation engine into custom `ObservationState` implementations:

```csharp
public static class CorrelatedObservationStates
{
    /// <summary>
    /// Creates a BMI-adjusted blood pressure observation.
    /// </summary>
    public static ObservationState BloodPressureWithBMICorrelation() => new()
    {
        Code = FhirCode.Observations.BloodPressurePanel,
        Components =
        [
            new ObservationComponent
            {
                Code = FhirCode.Observations.BloodPressureSystolic,
                Unit = "mmHg",
                UnitCode = "mm[Hg]",
                ValueFromContext = ctx =>
                {
                    var correlationEngine = new VitalSignCorrelationEngine();
                    var baseSystolic = 120m;
                    return correlationEngine.AdjustBloodPressure(baseSystolic, ctx);
                }
            },
            new ObservationComponent
            {
                Code = FhirCode.Observations.BloodPressureDiastolic,
                Unit = "mmHg",
                UnitCode = "mm[Hg]",
                ValueFromContext = ctx =>
                {
                    var correlationEngine = new VitalSignCorrelationEngine();
                    var baseDiastolic = 80m;
                    return correlationEngine.AdjustBloodPressure(baseDiastolic, ctx);
                }
            }
        ]
    };

    /// <summary>
    /// Creates a diabetes-aware glucose observation.
    /// </summary>
    public static ObservationState BloodGlucoseWithDiabetesCorrelation() => new()
    {
        Code = FhirCode.Observations.BloodGlucose,
        Unit = "mg/dL",
        UnitCode = "mg/dL",
        ValueFromContext = ctx =>
        {
            var correlationEngine = new VitalSignCorrelationEngine();
            var baseGlucose = new Faker().Random.Decimal(80m, 100m);
            return correlationEngine.AdjustGlucose(baseGlucose, ctx);
        }
    };
}
```

## Clinical Ranges Reference

### BMI Categories
- Underweight: < 18.5
- Normal: 18.5 - 24.9
- Overweight: 25.0 - 29.9
- Obese Class I: 30.0 - 34.9
- Obese Class II: 35.0 - 39.9
- Obese Class III: ≥ 40.0

### Blood Pressure Categories
- Normal: < 120/80 mmHg
- Elevated: 120-129/<80 mmHg
- Hypertension Stage 1: 130-139/80-89 mmHg
- Hypertension Stage 2: ≥ 140/90 mmHg

### Blood Glucose (Fasting)
- Normal: 70-100 mg/dL
- Prediabetes: 100-125 mg/dL
- Diabetes: ≥ 126 mg/dL

### Hemoglobin A1C
- Normal: < 5.7%
- Prediabetes: 5.7-6.4%
- Diabetes (controlled): 6.5-7.0%
- Diabetes (moderate): 7.0-8.5%
- Diabetes (poor): > 8.5%

### Total Cholesterol
- Desirable: < 200 mg/dL
- Borderline high: 200-239 mg/dL
- High: ≥ 240 mg/dL

## Testing

To test correlation accuracy:

```csharp
[Fact]
public void GivenObese_Patient_WhenAdjustingBP_ThenIncreases()
{
    var engine = new VitalSignCorrelationEngine();
    var context = new ScenarioContext();

    // BMI 42 (Class III Obesity)
    context.SetAttribute("bmi", 42m);

    var baseBP = 120m;
    var adjustedBP = engine.AdjustBloodPressure(baseBP, context);

    // Should be elevated by 15-25 mmHg
    Assert.InRange(adjustedBP, 135m, 145m);
}

[Fact]
public void GivenDiabetes_WhenAdjustingGlucose_ThenInDiabeticRange()
{
    var engine = new VitalSignCorrelationEngine();
    var context = CreateContextWithDiabetes();

    var baseGlucose = 90m;
    var adjustedGlucose = engine.AdjustGlucose(baseGlucose, context);

    // Should be in diabetic range
    Assert.InRange(adjustedGlucose, 140m, 200m);
}
```

## References

- ADR-faker-layered-architecture.md (lines 190-243)
- Framingham Heart Study: BMI and cardiovascular risk
- American Diabetes Association (ADA) diagnostic criteria
- American Heart Association blood pressure guidelines
