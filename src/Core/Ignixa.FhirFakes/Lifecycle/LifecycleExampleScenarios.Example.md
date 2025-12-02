# LifecycleExampleScenarios - Comprehensive Examples

This document provides detailed usage examples and expected outcomes for each lifecycle scenario in `LifecycleExampleScenarios.cs`. These scenarios demonstrate Layer 3 (Patient Lifecycles) features with realistic clinical pathways and evidence-based probabilities.

---

## Table of Contents

1. [GetHealthyChildLifecycle](#1-gethealthychildlifecycle)
2. [GetTypicalAdultLifecycle](#2-gettypicaladultlifecycle)
3. [GetMetabolicSyndromeLifecycle](#3-getmetabolicsyndromelifecycle)
4. [GetPediatricAsthmaLifecycle](#4-getpediatricasthmalifecycle)
5. [GetElderlyMultiMorbidityLifecycle](#5-getelderlyMultimorbiditylifecycle)
6. [Usage Patterns](#usage-patterns)
7. [Clinical Rationale](#clinical-rationale)

---

## 1. GetHealthyChildLifecycle

**Patient Profile**: Male child born 2005, healthy baseline (no chronic conditions)

**Simulation Duration**: Birth to age 18 (18 years)

### Expected Resource Counts

| Resource Type | Count | Details |
|--------------|-------|---------|
| Patient | 1 | Michael Johnson, male, DOB 2005-01-01 |
| Encounters | 10 | Pediatric wellness visits at ages 1, 2, 4, 6, 8, 10, 12, 14, 16, 18 |
| Immunizations | 15-20 | CDC schedule: HepB (×3), DTaP (×5), MMR (×2), Varicella (×2), annual flu |
| Observations | 30-40 | Vital signs per visit: height, weight, BMI, blood pressure |
| Conditions | 0 | Healthy baseline - no chronic conditions |
| Medications | 0 | No chronic medications |
| **Total** | **25-30** | |

### Code Example

```csharp
using Ignixa.FhirFakes.Lifecycle;
using Ignixa.Specification.Generated;

var schemaProvider = new R4CoreSchemaProvider();
var context = LifecycleExampleScenarios.GetHealthyChildLifecycle(schemaProvider);

// Verify patient demographics
Console.WriteLine($"Patient ID: {context.Patient.Id}");
Console.WriteLine($"Name: Michael Johnson");
Console.WriteLine($"Gender: {context.GetAttribute<string>("gender")}");
Console.WriteLine($"Birth Year: {context.GetAttribute<int>("birthYear")}");

// Verify wellness schedule
Console.WriteLine($"Pediatric wellness encounters: {context.Encounters.Count}");
// Expected: 10 (ages 1, 2, 4, 6, 8, 10, 12, 14, 16, 18)

// Verify immunization schedule
Console.WriteLine($"Immunizations: {context.Immunizations.Count}");
// Expected: 15-20 (varies with annual flu shots)

// Verify no chronic conditions
Console.WriteLine($"Conditions: {context.Conditions.Count}");
// Expected: 0 (healthy baseline)

Console.WriteLine($"Medications: {context.Medications.Count}");
// Expected: 0 (no chronic medications)
```

### Clinical Pathway

**Pediatric Wellness Schedule (AAP Guidelines)**:
- Age 1: First wellness visit after 12-month well-child
- Age 2: Toddler assessment, language development
- Age 4: Pre-kindergarten screening
- Age 6: School-age assessment
- Age 8: Middle childhood check
- Age 10: Pre-adolescent visit
- Age 12: Early adolescence, puberty discussion
- Age 14: Mid-adolescence, risk behavior screening
- Age 16: Late adolescence, driving safety
- Age 18: Transition to adult care discussion

**Immunization Schedule (CDC)**:
- Birth: Hepatitis B #1
- Age 1-2 months: HepB #2
- Age 2 months: DTaP #1, Hib #1, IPV #1, PCV13 #1, RV #1
- Age 4 months: DTaP #2, Hib #2, IPV #2, PCV13 #2, RV #2
- Age 6 months: DTaP #3, Hib #3, PCV13 #3, RV #3, HepB #3, annual flu
- Age 12-15 months: MMR #1, Varicella #1, Hib #4, PCV13 #4
- Age 15-18 months: DTaP #4
- Age 4-6 years: DTaP #5, MMR #2, Varicella #2, IPV #4
- Age 11-12 years: Tdap, HPV #1, MenACWY #1
- Ages 6+ months: Annual influenza vaccine

### Use Cases

1. **Baseline Testing**: Validate wellness visit generation without complicating factors
2. **Immunization Tracking**: Test CDC schedule compliance and immunization forecasting
3. **Quality Measures**: Well-child visit numerator/denominator calculations
4. **Pediatric EHR Testing**: Verify growth chart data, BMI percentiles, developmental milestones

---

## 2. GetTypicalAdultLifecycle

**Patient Profile**: Female born 1980, healthy weight (BMI 25), non-smoker, no family history

**Simulation Duration**: Birth to age 45 (45 years)

### Expected Resource Counts

| Resource Type | Count | Details |
|--------------|-------|---------|
| Patient | 1 | Jennifer Martinez, female, DOB 1980-01-01 |
| Encounters | 35-40 | Pediatric (10) + adult wellness (27) |
| Immunizations | 15-20 | Childhood + adult (flu, Tdap, COVID-19) |
| Conditions | 0-2 | Probabilistic: Type 2 Diabetes (15%), Essential Hypertension (30%) |
| Medications | 0-2 | If conditions: Metformin 500mg BID, Lisinopril 10mg QD |
| Observations | 100+ | Vitals, labs (A1c, BP) over 45 years |
| **Total** | **60-70** | |

### Code Example

```csharp
var schemaProvider = new R4CoreSchemaProvider();
var context = LifecycleExampleScenarios.GetTypicalAdultLifecycle(schemaProvider);

Console.WriteLine($"Patient: Jennifer Martinez, Age: 45");
Console.WriteLine($"Total encounters (45 years): {context.Encounters.Count}");

// Check for probabilistic conditions
var hasDiabetes = context.Conditions.Any(c =>
    c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "44054006"); // SNOMED CT Diabetes Type 2

var hasHypertension = context.Conditions.Any(c =>
    c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "59621000"); // SNOMED CT Essential Hypertension

Console.WriteLine($"Type 2 Diabetes: {(hasDiabetes ? "Yes" : "No")} (15% probability)");
Console.WriteLine($"Essential Hypertension: {(hasHypertension ? "Yes" : "No")} (30% probability)");

// Probability of at least one condition: 1 - (0.85 × 0.70) = 0.405 (40.5%)
Console.WriteLine($"Conditions: {context.Conditions.Count} (expect 0-2, ~40% chance of at least 1)");
Console.WriteLine($"Medications: {context.Medications.Count}");
```

### Clinical Pathway

**Pediatric Phase (Ages 0-18)**:
- Same as GetHealthyChildLifecycle (10 wellness visits, immunizations)

**Adult Phase (Ages 18-45)**:
- Annual wellness visits starting age 18 (27 encounters)
- Adult immunizations: annual flu, Tdap every 10 years, COVID-19 series

**Probabilistic Disease Onset**:

| Condition | Onset Ages | Probability | Risk Factors |
|-----------|-----------|-------------|--------------|
| Type 2 Diabetes | 40-65 | 15% | Age 45: 10% baseline (age 40-49), BMI 25 (normal), non-smoker, no family history |
| Essential Hypertension | 35-60 | 30% | Age 45: 29.6% baseline (NHANES), no diabetes yet, BMI 25 |

**If Diabetes Develops**:
1. Condition: Type 2 Diabetes (SNOMED 44054006), severity 2
2. Medication: Metformin 500mg BID (RxNorm 860975), chronic, reason = diabetes

**If Hypertension Develops**:
1. Condition: Essential Hypertension (SNOMED 59621000), severity 2
2. Medication: Lisinopril 10mg QD (RxNorm 314076), chronic, reason = hypertension

### Use Cases

1. **Adult Care Pathway**: Validate transition from pediatric to adult care
2. **Chronic Disease Management**: Test diabetes/hypertension workflows
3. **Quality Measures**: HEDIS diabetes A1c <8%, BP control <140/90
4. **Population Health**: Risk stratification for adult-onset chronic diseases

---

## 3. GetMetabolicSyndromeLifecycle

**Patient Profile**: Male born 1975, obesity (BMI 35), non-smoker, family history of diabetes

**Simulation Duration**: Birth to age 50 (50 years)

### Expected Resource Counts

| Resource Type | Count | Details |
|--------------|-------|---------|
| Patient | 1 | Robert Thompson, male, DOB 1975-01-01 |
| Encounters | 40-45 | Pediatric (10) + adult (30) + chronic disease management |
| Immunizations | 15-20 | Childhood + adult schedules |
| Conditions | 2-4 | Obesity (100%), Type 2 Diabetes (60%), Essential Hypertension (45-87%), Hyperlipidemia (50%) |
| Medications | 3-6 | Metformin, Lisinopril, Atorvastatin, possibly Aspirin 81mg |
| Observations | 150+ | A1c, BP, lipids, BMI tracking over decades |
| **Total** | **70-80** | |

### Code Example

```csharp
var schemaProvider = new R4CoreSchemaProvider();
var context = LifecycleExampleScenarios.GetMetabolicSyndromeLifecycle(schemaProvider);

// Calculate expected risks using DiseaseRiskCalculator
var riskCalc = new DiseaseRiskCalculator();
var diabetesRisk = riskCalc.CalculateDiabetesRisk(age: 50, smoker: false, bmi: 35m, familyHistory: true);
var hyperRisk = riskCalc.CalculateHypertensionRisk(age: 50, bmi: 35m, hasDiabetes: false);

Console.WriteLine($"Patient: Robert Thompson, Age: 50, BMI: 35 (Class II Obesity)");
Console.WriteLine($"Calculated Diabetes Risk: {diabetesRisk:P1}");
// Expected: 15% (age 50-59) × 2.0 (obesity) × 2.0 (family history) = 60%

Console.WriteLine($"Calculated Hypertension Risk (without diabetes): {hyperRisk:P1}");
// Expected: 29.6% + 15% (obesity) = 44.6%

// If diabetes develops, hypertension risk increases dramatically
var hyperRiskWithDM = riskCalc.CalculateHypertensionRisk(age: 50, bmi: 35m, hasDiabetes: true);
Console.WriteLine($"Calculated Hypertension Risk (with diabetes): {hyperRiskWithDM:P1}");
// Expected: 29.6% + 15% (obesity) + 42.3% (diabetes) = 86.9%

Console.WriteLine($"\nActual Results:");
Console.WriteLine($"Conditions: {context.Conditions.Count} (expect 2-4)");
Console.WriteLine($"Medications: {context.Medications.Count} (expect 3-6)");
```

### Clinical Pathway

**Disease Cascade Timeline**:

| Age Range | Event | Probability | Management |
|-----------|-------|-------------|------------|
| 30-40 | Obesity documented | 100% | BMI 35 documented, lifestyle counseling |
| 40-60 | Type 2 Diabetes onset | 60% | Metformin 500mg BID, A1c monitoring |
| 40-60 | Essential Hypertension | 44.6% (no DM) → 86.9% (with DM) | Lisinopril 10mg QD, BP monitoring |
| 45-60 | Hyperlipidemia | 50% | Atorvastatin 20mg QD, lipid panel |
| 50+ | ASCVD prevention | If DM or HTN | Aspirin 81mg QD for cardiac protection |

**Risk Factor Analysis**:

**Type 2 Diabetes Risk at Age 50**:
```
Base Risk (age 50-59): 15%
× 2.0 (BMI ≥ 30 obesity multiplier)
× 2.0 (family history multiplier)
= 60% probability
```

**Essential Hypertension Risk at Age 50**:
```
Base Risk: 29.6% (NHANES baseline)
+ 15% (BMI ≥ 30 obesity adjustment)
= 44.6% without diabetes

If diabetes develops:
  29.6% + 15% + 42.3% (diabetes comorbidity)
  = 86.9% probability
```

### Use Cases

1. **Complex Chronic Disease Management**: Test multi-condition workflows
2. **Risk Stratification**: Validate DiseaseRiskCalculator integration
3. **Quality Measures**: Diabetes control (A1c <8%), BP control, statin therapy
4. **Population Health**: High-risk patient identification and care coordination
5. **Multi-Morbidity Studies**: Test care plans for patients with 3+ conditions

---

## 4. GetPediatricAsthmaLifecycle

**Patient Profile**: Female child born 2015, atopic march progression (allergic rhinitis → asthma)

**Simulation Duration**: Birth to age 10 (10 years)

### Expected Resource Counts

| Resource Type | Count | Details |
|--------------|-------|---------|
| Patient | 1 | Emma Davis, female, DOB 2015-01-01 |
| Encounters | 10 | Pediatric wellness visits ages 1-10 |
| Immunizations | 10-12 | CDC schedule ages 0-10 |
| Conditions | 1-2 | Allergic Rhinitis (100%), Asthma (47.3% with atopy) |
| Medications | 0-3 | If asthma: Albuterol PRN, Fluticasone 50mcg BID, possibly Montelukast |
| Observations | 30-40 | Vitals, peak flow (if asthma) |
| **Total** | **30-35** | |

### Code Example

```csharp
var schemaProvider = new R4CoreSchemaProvider();
var context = LifecycleExampleScenarios.GetPediatricAsthmaLifecycle(schemaProvider);

// Calculate asthma risk using DiseaseRiskCalculator
var riskCalc = new DiseaseRiskCalculator();
var baseAsthmaRisk = riskCalc.CalculateAsthmaRisk(age: 8, hasAllergies: false);
var atopicAsthmaRisk = riskCalc.CalculateAsthmaRisk(age: 8, hasAllergies: true);

Console.WriteLine($"Patient: Emma Davis, Age: 10");
Console.WriteLine($"Baseline asthma risk (age 8): {baseAsthmaRisk:P1}");
// Expected: 26.3% (CDC NHIS 2021 pediatric baseline)

Console.WriteLine($"Atopic asthma risk (with allergies): {atopicAsthmaRisk:P1}");
// Expected: 26.3% × 1.8 = 47.3%

// Check for atopic march
bool hasAllergicRhinitis = context.Conditions.Any(c =>
    c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "61582004"); // SNOMED Allergic Rhinitis

bool hasAsthma = context.Conditions.Any(c =>
    c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "195967001"); // SNOMED Asthma

Console.WriteLine($"\nAtopic March Progression:");
Console.WriteLine($"Allergic Rhinitis (ages 2-4): {(hasAllergicRhinitis ? "Yes" : "No")} (100% in this scenario)");
Console.WriteLine($"Asthma (ages 4-10): {(hasAsthma ? "Yes" : "No")} (47.3% probability with atopy)");

Console.WriteLine($"\nConditions: {context.Conditions.Count} (expect 1-2)");
Console.WriteLine($"Medications: {context.Medications.Count} (0-3 depending on asthma)");
```

### Clinical Pathway

**Atopic March Sequence** (Natural History of Allergic Disease):

| Age | Event | Probability | Management |
|-----|-------|-------------|------------|
| 2-4 | Allergic Rhinitis onset | 100% (deterministic in this scenario) | Antihistamines (not always prescribed) |
| 4-10 | Asthma onset (if atopic) | 26.3% baseline × 1.8 (atopy) = 47.3% | NAEPP step therapy |

**NAEPP Asthma Step Therapy (if asthma develops)**:

1. **Step 1: Rescue Therapy**
   - Medication: Albuterol 0.083mg/mL inhalation solution PRN
   - Use: Acute symptom relief, bronchodilator

2. **Step 2: Daily Controller**
   - Add: Fluticasone propionate 50mcg inhaler BID
   - Rationale: Low-dose inhaled corticosteroid (ICS) for persistent asthma

3. **Step 3: Additional Controller** (if poorly controlled)
   - Add: Montelukast 10mg tablet QD
   - Rationale: Leukotriene receptor antagonist (LTRA) for better control

**Monitoring**:
- Peak expiratory flow rate (PEFR) at each visit
- Asthma control test (ACT) score
- Controller medication adherence
- Rescue inhaler use frequency (>2×/week indicates poor control)

### Use Cases

1. **Pediatric Chronic Disease Management**: Test asthma care plans
2. **Step Therapy Validation**: Verify medication escalation logic
3. **Atopic Disease Modeling**: Test allergic disease progression patterns
4. **Quality Measures**: Asthma medication ratio (AMR), controller adherence
5. **Clinical Decision Support**: Asthma action plan generation

---

## 5. GetElderlyMultiMorbidityLifecycle

**Patient Profile**: Male born 1945, cumulative disease burden typical in elderly, polypharmacy

**Simulation Duration**: Birth to age 80 (80 years)

### Expected Resource Counts

| Resource Type | Count | Details |
|--------------|-------|---------|
| Patient | 1 | William Anderson, male, DOB 1945-01-01 |
| Encounters | 70-80 | Full lifecycle: pediatric (10) + adult (62) + chronic disease management |
| Immunizations | 15-20 | Childhood + adult + geriatric (pneumonia, shingles) |
| Conditions | 4-6 | Type 2 Diabetes (15%), Essential Hypertension (30-50%), Hyperlipidemia (40%), Cancer (35%), Stroke (18%) |
| Medications | 6-10 | Polypharmacy: Metformin, Lisinopril, Atorvastatin, Aspirin 81mg, Clopidogrel (if stroke) |
| Observations | 200+ | 80 years of vitals, labs, functional assessments |
| **Total** | **120-150** | |

### Code Example

```csharp
var schemaProvider = new R4CoreSchemaProvider();
var context = LifecycleExampleScenarios.GetElderlyMultiMorbidityLifecycle(schemaProvider);

// Calculate age-stratified risks
var riskCalc = new DiseaseRiskCalculator();

Console.WriteLine($"Patient: William Anderson, Age: 80");
Console.WriteLine($"Lifecycle span: 80 years (1945-2025)");

// Age 60 risks
var diabetesRisk60 = riskCalc.CalculateDiabetesRisk(age: 60, smoker: false, bmi: 27m, familyHistory: false);
var cancerRisk60 = riskCalc.CalculateCancerRisk(age: 60, smoker: false, familyHistory: false);
Console.WriteLine($"\nAge 60 Risks:");
Console.WriteLine($"  Diabetes: {diabetesRisk60:P1} (baseline 20% for age 60-69)");
Console.WriteLine($"  Cancer: {cancerRisk60:P1} (20% baseline for age 60-69)");

// Age 80 risks
var diabetesRisk80 = riskCalc.CalculateDiabetesRisk(age: 80, smoker: false, bmi: 27m, familyHistory: false);
var cancerRisk80 = riskCalc.CalculateCancerRisk(age: 80, smoker: false, familyHistory: false);
var strokeRisk80 = riskCalc.CalculateStrokeRisk(age: 80, hasHypertension: true, hasDiabetes: true, smoker: false);
Console.WriteLine($"\nAge 80 Risks:");
Console.WriteLine($"  Diabetes: {diabetesRisk80:P1} (baseline 25% for age 70+)");
Console.WriteLine($"  Cancer: {cancerRisk80:P1} (35% baseline for age 70+)");
Console.WriteLine($"  Stroke (with HTN+DM): {strokeRisk80:P1} (18% + 8% HTN + 5% DM = 31%)");

Console.WriteLine($"\nActual Results:");
Console.WriteLine($"Conditions: {context.Conditions.Count} (expect 4-6)");
Console.WriteLine($"Medications: {context.Medications.Count} (expect 6-10, polypharmacy typical)");
Console.WriteLine($"Total encounters over 80 years: {context.Encounters.Count}");
Console.WriteLine($"Total immunizations: {context.Immunizations.Count}");
```

### Clinical Pathway

**Disease Cascade Timeline** (80 Years):

| Age Range | Event | Probability | Management |
|-----------|-------|-------------|------------|
| 0-18 | Pediatric wellness + immunizations | Deterministic | 10 wellness visits, CDC schedule |
| 18-50 | Adult wellness (annual) | Deterministic | 32 annual wellness visits |
| 50-65 | Type 2 Diabetes onset | 15% | Metformin 500mg BID, A1c monitoring |
| 55-70 | Essential Hypertension onset | 30-50% (higher if DM) | Lisinopril 10mg QD, BP monitoring |
| 60-75 | Hyperlipidemia | 40% | Atorvastatin 20mg QD + Aspirin 81mg QD |
| 60-80 | Cancer | 20% (age 60-69) → 35% (age 70+) | Diagnosis, possible treatment |
| 65-80 | Stroke | 10% (age 65-74) → 18% (age 75+) + HTN/DM | Emergency visit, Clopidogrel 75mg QD |

**Age-Stratified Risk Increases**:

**Type 2 Diabetes** (baseline, BMI 27):
- Age 50-59: 15%
- Age 60-69: 20%
- Age 70+: 25%

**Cancer** (baseline, non-smoker, no family history):
- Age 50-59: 10%
- Age 60-69: 20%
- Age 70+: 35%

**Stroke** (baseline, then add risk factors):
- Age 65-74: 10% + 8% (HTN) + 5% (DM) = 23%
- Age 75+: 18% + 8% (HTN) + 5% (DM) = 31%

**Polypharmacy at Age 80** (if all conditions develop):
1. Metformin 500mg BID (diabetes)
2. Lisinopril 10mg QD (hypertension)
3. Atorvastatin 20mg QD (hyperlipidemia/ASCVD)
4. Aspirin 81mg QD (cardiac protection)
5. Clopidogrel 75mg QD (stroke secondary prevention)
6. Additional PRN medications (pain, sleep, etc.)

**Geriatric Preventive Care**:
- Age 50: Shingles vaccine (Shingrix)
- Age 65: Pneumococcal vaccine (PPSV23 + PCV13)
- Annual: Flu shot, functional assessment (ADL/IADL)
- Screenings: Colonoscopy (ages 50-75), fall risk, cognitive assessment

### Use Cases

1. **Geriatric Care Pathway**: Validate elderly-specific workflows
2. **Multi-Morbidity Care Coordination**: Test complex care plans with 4-6 conditions
3. **Polypharmacy Management**: Drug interaction checking, Beers Criteria validation
4. **Longitudinal EHR Simulation**: Test 80-year data lifecycle, archival, retrieval
5. **Quality Measures**: HEDIS Star Ratings (diabetes, hypertension, statin use)
6. **Population Health**: High-risk elderly stratification, care gap analysis

---

## Usage Patterns

### Basic Usage

```csharp
using Ignixa.FhirFakes.Lifecycle;
using Ignixa.Specification.Generated;

// 1. Create schema provider
var schemaProvider = new R4CoreSchemaProvider();

// 2. Generate lifecycle scenario
var context = LifecycleExampleScenarios.GetHealthyChildLifecycle(schemaProvider);

// 3. Access generated resources
Console.WriteLine($"Patient: {context.Patient.Id}");
Console.WriteLine($"Encounters: {context.Encounters.Count}");
Console.WriteLine($"Conditions: {context.Conditions.Count}");
Console.WriteLine($"Medications: {context.Medications.Count}");
```

### Advanced Usage - Risk Calculation

```csharp
using Ignixa.FhirFakes.Lifecycle;

// Calculate risks before generating lifecycle
var riskCalc = new DiseaseRiskCalculator();

// Example: 55-year-old with obesity, smoker, family history
var diabetesRisk = riskCalc.CalculateDiabetesRisk(
    age: 55,
    smoker: true,
    bmi: 32m,
    familyHistory: true);

Console.WriteLine($"Calculated Diabetes Risk: {diabetesRisk:P1}");
// Expected: 15% (age 50-59) × 2.0 (obesity) × 1.5 (smoking) × 2.0 (family) = 90%

// Generate lifecycle with calculated risks
var context = LifecycleExampleScenarios.GetMetabolicSyndromeLifecycle(schemaProvider);
```

### Batch Generation for Population Health

```csharp
// Generate 1000 patients for population health analytics
var patients = new List<ScenarioContext>();
var schemaProvider = new R4CoreSchemaProvider();

for (int i = 0; i < 1000; i++)
{
    var context = i % 5 switch
    {
        0 => LifecycleExampleScenarios.GetHealthyChildLifecycle(schemaProvider),
        1 => LifecycleExampleScenarios.GetTypicalAdultLifecycle(schemaProvider),
        2 => LifecycleExampleScenarios.GetMetabolicSyndromeLifecycle(schemaProvider),
        3 => LifecycleExampleScenarios.GetPediatricAsthmaLifecycle(schemaProvider),
        _ => LifecycleExampleScenarios.GetElderlyMultiMorbidityLifecycle(schemaProvider)
    };

    patients.Add(context);
}

// Analyze population statistics
var avgConditions = patients.Average(p => p.Conditions.Count);
var avgMedications = patients.Average(p => p.Medications.Count);

Console.WriteLine($"Population: {patients.Count} patients");
Console.WriteLine($"Average conditions per patient: {avgConditions:F2}");
Console.WriteLine($"Average medications per patient: {avgMedications:F2}");
```

---

## Clinical Rationale

### Evidence-Based Risk Modeling

All probabilistic conditions use **DiseaseRiskCalculator** with evidence from:

1. **CDC National Health Interview Survey (NHIS)**: Age-stratified prevalence data
2. **NHANES (National Health and Nutrition Examination Survey)**: Hypertension baseline 29.6%
3. **Framingham Heart Study**: Stroke risk profiles
4. **NCI SEER Program**: Cancer incidence by age
5. **American Diabetes Association**: Diabetes risk factors and prevalence

### Risk Factor Multipliers

| Risk Factor | Condition | Multiplier | Source |
|------------|-----------|------------|---------|
| BMI ≥ 30 (Obesity) | Type 2 Diabetes | ×2.0 | CDC Diabetes Statistics |
| BMI ≥ 30 (Obesity) | Hypertension | +15% | NHANES |
| Smoking | Type 2 Diabetes | ×1.5 | ADA Risk Factors |
| Smoking | Cancer | ×2.5 | Surgeon General Report |
| Family History | Type 2 Diabetes | ×2.0 | Genetic Studies |
| Family History | Cancer | ×1.8 | NCI Risk Assessment |
| Allergies (Atopy) | Asthma | ×1.8 | Atopic March Studies |
| Hypertension | Stroke | +8% | Framingham |
| Diabetes | Stroke | +5% | Framingham |
| Diabetes | Hypertension | +42.3% | Clinical Comorbidity Studies |

### Age-Dependent Risks

**Type 2 Diabetes** (baseline percentages):
- Age <30: 1%
- Age 30-39: 5%
- Age 40-49: 10%
- Age 50-59: 15%
- Age 60-69: 20%
- Age 70+: 25%

**Cancer** (baseline percentages):
- Age <30: 0.5%
- Age 30-39: 1.5%
- Age 40-49: 4%
- Age 50-59: 10%
- Age 60-69: 20%
- Age 70+: 35%

**Stroke** (baseline percentages):
- Age <45: 0.5%
- Age 45-54: 2%
- Age 55-64: 5%
- Age 65-74: 10%
- Age 75+: 18%

### Preventive Care Schedules

**Pediatric Wellness** (AAP Bright Futures):
- Ages 1, 2, 4, 6, 8, 10, 12, 14, 16, 18 years

**Adult Wellness** (USPSTF):
- Annual visits starting age 18

**Immunizations** (CDC ACIP):
- Childhood: HepB, DTaP, Hib, IPV, PCV13, RV, MMR, Varicella
- Adolescent: Tdap, HPV, MenACWY
- Adult: Annual flu, Tdap every 10 years, COVID-19 series
- Geriatric: Pneumococcal (age 65), Shingles (age 50)

---

## Summary

These lifecycle scenarios provide realistic patient journeys for:

1. **Testing**: Validate FHIR workflows with clinically accurate data
2. **Demonstration**: Showcase probabilistic disease modeling capabilities
3. **Analytics**: Generate populations for quality measure testing
4. **Education**: Learn evidence-based risk calculation and clinical pathways

Each scenario balances clinical accuracy with computational efficiency, using evidence-based probabilities from authoritative sources (CDC, NHANES, Framingham, NCI SEER).
