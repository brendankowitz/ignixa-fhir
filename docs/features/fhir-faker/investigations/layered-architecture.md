# Investigation: FHIR Faker Layered Architecture

**Feature**: fhir-faker
**Status**: Proposed
**Created**: December 2, 2025

---

## Context

We need a comprehensive FHIR test data generation system that can produce everything from simple valid resources to realistic population-level datasets. Analysis of Synthea reveals a clear layered architecture that we should adopt.

---

## Decision

Implement a 4-layer architecture with clear separation of concerns:

```
Layer 4: Population Generation (Demographics, Distributions)
         ↓ generates multiple patients
Layer 3: Patient Lifecycles (Probabilistic, Multi-Scenario, Age Progression)
         ↓ orchestrates scenarios over lifetime
Layer 2: Scenarios (States, Behaviors, Conditional Logic)
         ↓ sequences events
Layer 1: Random Valid Resources (Schema-driven, Binding-aware)
```

Each layer builds on the one below it, with clear interfaces and responsibilities.

---

## Layer Definitions

### Layer 1: Random Valid Resource ✅ **100% COMPLETE**

**Responsibility**: Generate a single, syntactically valid FHIR resource

**Implementation**: `SchemaBasedFhirResourceFaker`

**Features:**
- Schema-driven generation using `IFhirSchemaProvider`
- Binding-aware code selection (uses FHIR value sets)
- Version-aware field naming (STU3 vs R4+)
- 184+ predefined FHIR codes in `/Codes`
- Supports 9 resource types

**API:**
```csharp
var faker = new SchemaBasedFhirResourceFaker(schemaProvider);
var patient = faker.Generate("Patient");
var observation = faker.Generate("Observation");
```

**Quality**: ⭐⭐⭐⭐⭐ Excellent

**Status**: Production-ready, no major gaps

---

### Layer 2: Scenario (States, Behaviors, Logic) ✅ **75% COMPLETE**

**Responsibility**: Sequence of clinical events with conditional logic and temporal progression

**Implementation**: `ScenarioBuilder`, `ScenarioContext`, State classes

**Features We Have:**
- ✅ Fluent scenario builder API
- ✅ 15 state types:
  - InitialState, DelayState, EncounterState, ConditionOnsetState, ConditionEndState
  - ObservationState, DiagnosticReportState, ImmunizationState
  - AllergyIntoleranceState, ProcedureState, MedicationOrderState
  - SetAttributeState, GuardState, TerminalState
  - ConditionalMedicationEscalationState
- ✅ 7 predefined scenarios (Diabetes, Hypertension, Pregnancy, Asthma, Wellness, UTI, Ear Infection)
- ✅ Temporal sequencing (Delay states)
- ✅ Attribute-based logic (SetAttribute, GetAttribute)
- ✅ Conditional logic (GuardState)
- ✅ Resource lifecycle (ConditionEnd, Terminal)

**API:**
```csharp
var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(age: 45, gender: "male")
    .AddEncounter("Annual wellness visit")
    .AddObservation(VitalSigns.BloodPressure)
    .AddComprehensiveMetabolicPanel()
    .AddGuard(GuardState.MinimumAge(45))
    .AddColonoscopy()
    .Build();
```

**Quality**: ⭐⭐⭐⭐☆ Very Good

**Missing (25%):**

#### 1. Probabilistic Transitions (HIGH PRIORITY)
**What**: Weighted random branching based on real epidemiological data

**Synthea Pattern**:
```json
{
  "distributed_transition": [
    {"distribution": 0.086, "transition": "Appendicitis"},  // 8.6% get appendicitis
    {"distribution": 0.914, "transition": "Healthy"}        // 91.4% stay healthy
  ]
}
```

**Our Implementation**:
```csharp
public class ProbabilisticBranchState : ScenarioState
{
    public List<(double Probability, ScenarioState State)> Branches { get; set; }

    public override void Execute(ScenarioContext context)
    {
        var random = Random.Shared.NextDouble();
        var cumulative = 0.0;

        foreach (var (probability, state) in Branches)
        {
            cumulative += probability;
            if (random <= cumulative)
            {
                state.Execute(context);
                return;
            }
        }
    }
}

// Usage
.AddProbabilisticBranch(
    (0.60, DiabetesScenario()),   // 60% develop diabetes
    (0.40, NoCondition())          // 40% stay healthy
)
```

**Effort**: 1 week

---

#### 2. CallSubmodule / Reusable Scenario Fragments (MEDIUM PRIORITY)
**What**: Composable, reusable scenario fragments

**Synthea Pattern**:
```json
{
  "type": "CallSubmodule",
  "submodule": "encounter/vitals"  // Reused by 20+ scenarios
}
```

**Our Implementation**:
```csharp
public class CallSubScenarioState : ScenarioState
{
    public Func<ScenarioBuilder, ScenarioBuilder> SubScenario { get; set; }

    public override void Execute(ScenarioContext context)
    {
        var builder = new ScenarioBuilder(context);
        SubScenario(builder).ExecuteStates(context);
    }
}

// Define reusable fragments
public static class CommonScenarios
{
    public static Action<ScenarioBuilder> RecordVitalSigns() => builder => builder
        .AddObservation(VitalSigns.Height)
        .AddObservation(VitalSigns.Weight)
        .AddObservation(VitalSigns.BMI)
        .AddObservation(VitalSigns.BloodPressure);

    public static Action<ScenarioBuilder> BasicMetabolicPanel() => builder => builder
        .AddDiagnosticReport(DiagnosticReports.ComprehensiveMetabolicPanel);
}

// Usage
.AddSubScenario(CommonScenarios.RecordVitalSigns())
.AddSubScenario(CommonScenarios.BasicMetabolicPanel())
```

**Effort**: 1 week

---

#### 3. Value Correlations (HIGH PRIORITY)
**What**: Physiologically realistic correlations between vitals/labs

**Synthea Pattern** (`bmi_correlations.json`):
```
BMI 30-35: Systolic BP +5-10 mmHg
BMI 35-40: Systolic BP +10-15 mmHg, Cholesterol +20-40 mg/dL
BMI 40+: Systolic BP +15-25 mmHg

Diabetes: Glucose 140-200 mg/dL, A1C 6.5-9.0%
```

**Our Implementation**:
```csharp
public class VitalSignCorrelationEngine
{
    public decimal AdjustBloodPressure(decimal baseBP, ScenarioContext context)
    {
        var bmi = context.GetAttribute<decimal>("bmi");

        var adjustment = bmi switch
        {
            >= 40 => Random.Shared.Next(15, 26),
            >= 35 => Random.Shared.Next(10, 16),
            >= 30 => Random.Shared.Next(5, 11),
            _ => 0
        };

        return baseBP + adjustment;
    }

    public decimal AdjustGlucose(decimal baseGlucose, ScenarioContext context)
    {
        var hasDiabetes = context.Conditions.Any(c => c.Code == "44054006");  // Diabetes

        return hasDiabetes
            ? Random.Shared.Next(140, 201)  // Diabetic range
            : baseGlucose;                   // Normal range
    }
}

// Usage in ObservationState
public override void Execute(ScenarioContext context)
{
    var correlationEngine = new VitalSignCorrelationEngine();
    var baseBP = 120m;
    var adjustedBP = correlationEngine.AdjustBloodPressure(baseBP, context);

    // Create observation with adjusted value
}
```

**Effort**: 1 week

---

#### 4. Data-Driven Configuration (MEDIUM PRIORITY)
**What**: Externalize schedules, protocols, formularies to JSON/CSV files

**Synthea Pattern**:
- `immunization_schedule.json` - CDC vaccine schedule
- `bmi_correlations.json` - BMI effect on vitals
- `costs/medications.csv` - 600+ medications with costs

**Our Implementation**:
```csharp
public class ImmunizationSchedule
{
    public static ImmunizationSchedule LoadFromCdc()
    {
        var json = File.ReadAllText("Data/immunization_schedule.json");
        return JsonSerializer.Deserialize<ImmunizationSchedule>(json);
    }

    public bool IsDue(string vaccineName, int patientAgeMonths, DateTime currentDate)
    {
        var vaccine = _schedules[vaccineName];
        return currentDate.Year >= vaccine.FirstAvailable
            && vaccine.AtMonths.Contains(patientAgeMonths);
    }
}

// Data file: Data/immunization_schedule.json
{
  "hepb": {
    "code": {"system": "CVX", "code": "08"},
    "at_months": [0, 1, 6],
    "first_available": 1981
  }
}
```

**Effort**: 3 days

---

### Layer 3: Patient Lifecycles ❌ **5% COMPLETE**

**Responsibility**: Full patient journey from birth to death with age-appropriate events

**What Synthea Does**:
```
Birth (age 0)
  → Newborn immunizations
  → Wellness visits (ages 1, 2, 4, 6, 8, 10, 12, 14, 16, 18)
  → Adult wellness visits (annual)
  → Probabilistic conditions:
    - Asthma: 26.3% onset ages 1-17, 42.3% ages 18-44
    - Diabetes: Increases with age, peaks 35-65
    - Hypertension: 29.6% baseline + age factor
  → Disease progression over decades
  → End-of-life conditions
  → Death (mean age: 78.5 years)
```

**What We Have:**
- ✅ Single scenario execution
- ✅ Age parameter in patient generation

**What We Need:**

#### 1. Lifecycle Orchestrator (CRITICAL)
**What**: Runs multiple scenarios over patient lifetime

**Implementation**:
```csharp
public class PatientLifecycleGenerator
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly List<ILifecycleEvent> _events = [];

    public PatientLifecycleGenerator WithBirthYear(int year)
    {
        _birthYear = year;
        return this;
    }

    public PatientLifecycleGenerator AddWellnessSchedule(bool pediatric, bool adult)
    {
        if (pediatric)
            _events.Add(new PediatricWellnessSchedule());  // Ages 1,2,4,6,8,10,12,14,16,18
        if (adult)
            _events.Add(new AdultWellnessSchedule());      // Annual starting age 18

        return this;
    }

    public PatientLifecycleGenerator AddImmunizationSchedule()
    {
        _events.Add(new ImmunizationScheduleEvent());
        return this;
    }

    public PatientLifecycleGenerator AddProbabilisticCondition(
        string conditionName,
        Range onsetAges,
        double probability)
    {
        _events.Add(new ProbabilisticConditionOnset(conditionName, onsetAges, probability));
        return this;
    }

    public ScenarioContext SimulateUntilAge(int targetAge)
    {
        var context = new ScenarioContext();
        context.Patient = GeneratePatient(_birthYear);

        for (int age = 0; age <= targetAge; age++)
        {
            context.CurrentDate = new DateTime(_birthYear + age, 1, 1);
            context.PatientAge = age;

            // Execute all age-appropriate events
            foreach (var evt in _events.Where(e => e.IsApplicable(age)))
            {
                evt.Execute(context, _schemaProvider);
            }
        }

        return context;
    }
}

// Usage
var lifecycle = new PatientLifecycleGenerator(schemaProvider)
    .WithBirthYear(1980)
    .WithGender("male")
    .AddWellnessSchedule(pediatric: true, adult: true)
    .AddImmunizationSchedule()
    .AddProbabilisticCondition("Asthma", onsetAges: 1..17, probability: 0.263)
    .AddProbabilisticCondition("Diabetes", onsetAges: 35..65, probability: 0.15)
    .SimulateUntilAge(78)
    .Generate();
```

**Effort**: 2 weeks

---

#### 2. Lifecycle Events Interface
**What**: Standard interface for age-based events

**Implementation**:
```csharp
public interface ILifecycleEvent
{
    bool IsApplicable(int patientAge);
    void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider);
}

public class PediatricWellnessSchedule : ILifecycleEvent
{
    private readonly int[] _visitAges = [1, 2, 4, 6, 8, 10, 12, 14, 16, 18];

    public bool IsApplicable(int patientAge) => _visitAges.Contains(patientAge);

    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        schemaProvider.GetWellnessVisit(age: patientAge).ExecuteOn(context);
    }
}

public class ProbabilisticConditionOnset : ILifecycleEvent
{
    private readonly string _conditionName;
    private readonly Range _onsetAges;
    private readonly double _probability;
    private bool _hasOccurred;

    public bool IsApplicable(int patientAge)
        => !_hasOccurred && _onsetAges.Start.Value <= patientAge && patientAge <= _onsetAges.End.Value;

    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        if (Random.Shared.NextDouble() <= _probability)
        {
            // Add condition scenario
            _hasOccurred = true;
        }
    }
}
```

**Effort**: 1 week

---

#### 3. Age Progression & Risk Calculation
**What**: Calculate age-dependent disease risk

**Implementation**:
```csharp
public class DiseaseRiskCalculator
{
    public double CalculateDiabetesRisk(int age, bool smoker, decimal bmi, bool familyHistory)
    {
        var baseRisk = age switch
        {
            < 30 => 0.01,
            < 40 => 0.05,
            < 50 => 0.10,
            < 60 => 0.15,
            < 70 => 0.20,
            _ => 0.25
        };

        if (bmi >= 30) baseRisk *= 2.0;  // Obesity doubles risk
        if (smoker) baseRisk *= 1.5;
        if (familyHistory) baseRisk *= 2.0;

        return Math.Min(baseRisk, 1.0);  // Cap at 100%
    }

    public double CalculateHypertensionRisk(int age, decimal bmi, bool hasDiabetes)
    {
        var baseRisk = 0.296;  // 29.6% baseline

        if (age >= 60) baseRisk += 0.20;
        if (bmi >= 30) baseRisk += 0.15;
        if (hasDiabetes) baseRisk += 0.423;  // 42.3% increase if diabetic

        return Math.Min(baseRisk, 1.0);
    }
}
```

**Effort**: 3 days

---

### Layer 4: Population Generation ❌ **0% COMPLETE**

**Responsibility**: Generate thousands of patients with realistic demographic distributions

**What Synthea Does**:
```
GeneratePopulation(state: "Massachusetts", size: 10000)
  → Load demographics.csv (29,000 US cities)
  → For each patient 1..10000:
    1. Select city from MA (weighted by population)
       Boston: 35% probability
       Worcester: 10% probability
       Springfield: 8% probability

    2. Generate demographics matching city distribution:
       Race: Boston is 53% White, 25% Black, 19% Hispanic, 9% Asian
       Age: Sample from city age distribution
       Income: Sample from city income brackets
       Education: Sample from city education levels

    3. Generate name matching race/ethnicity:
       Hispanic → Rodriguez, Garcia, Martinez
       Asian → Chen, Patel, Kim

    4. Geographic consistency:
       Address in Boston → Zip 02101-02298
       Zip 02101 → Area code 617 or 857
       Area code 617 → Timezone EST

    5. Run lifecycle simulation (Layer 3)

    6. Output all resources

  → Verify: Generated population matches MA census data
```

**What We Need:**

#### 1. Demographics Data Loader
**Implementation**:
```csharp
public class DemographicsDataProvider
{
    private readonly List<CityDemographics> _cities;

    public static DemographicsDataProvider LoadFromCsv(string filePath)
    {
        // Parse demographics.csv (29,000 cities)
        return new DemographicsDataProvider(cities);
    }

    public CityDemographics SelectCity(string state)
    {
        var citiesInState = _cities.Where(c => c.State == state).ToList();
        var totalPopulation = citiesInState.Sum(c => c.Population);

        // Weighted random selection
        var random = Random.Shared.Next(0, totalPopulation);
        var cumulative = 0;

        foreach (var city in citiesInState)
        {
            cumulative += city.Population;
            if (random < cumulative)
                return city;
        }

        return citiesInState.Last();
    }
}

public record CityDemographics(
    string Name,
    string State,
    int Population,
    Dictionary<string, double> RaceDistribution,  // "White": 0.579, "Black": 0.398
    Dictionary<string, double> AgeDistribution,   // "0-10": 0.12, "10-15": 0.08
    Dictionary<string, double> IncomeDistribution // "<10k": 0.193, "10-15k": 0.14
);
```

**Effort**: 1 week

---

#### 2. Population Generator
**Implementation**:
```csharp
public class PopulationGenerator
{
    public List<ScenarioContext> Generate(string state, int size)
    {
        var demographics = DemographicsDataProvider.LoadFromCsv("data/demographics.csv");
        var patients = new List<ScenarioContext>();

        for (int i = 0; i < size; i++)
        {
            var city = demographics.SelectCity(state);

            // Sample demographics
            var race = SampleFromDistribution(city.RaceDistribution);
            var ageGroup = SampleFromDistribution(city.AgeDistribution);
            var age = RandomAgeInGroup(ageGroup);
            var gender = Random.Shared.NextDouble() < city.MaleRatio ? "male" : "female";
            var name = GenerateNameForRace(race, gender);
            var address = GenerateAddressInCity(city);

            // Generate lifecycle
            var lifecycle = new PatientLifecycleGenerator()
                .WithBirthYear(DateTime.Now.Year - age)
                .WithGender(gender)
                .WithRace(race)
                .WithAddress(address)
                .AddWellnessSchedule(pediatric: true, adult: true)
                .AddImmunizationSchedule()
                .AddProbabilisticConditions()
                .SimulateUntilAge(age)
                .Generate();

            patients.Add(lifecycle);
        }

        return patients;
    }
}

// Usage
var population = new PopulationGenerator()
    .ForState("Massachusetts")
    .WithSize(10000)
    .Generate();

// Export
population.ExportToNdJson("output/ma_population.ndjson");
```

**Effort**: 2 weeks

---

#### 3. Name Generation by Ethnicity
**Implementation**:
```csharp
public class EthnicNameGenerator
{
    private readonly Dictionary<string, (string[] First, string[] Last)> _namesByRace = new()
    {
        ["White"] = (
            ["James", "John", "Robert", "Michael", "William"],
            ["Smith", "Johnson", "Williams", "Brown", "Jones"]
        ),
        ["Hispanic"] = (
            ["Jose", "Luis", "Carlos", "Juan", "Miguel"],
            ["Rodriguez", "Garcia", "Martinez", "Hernandez", "Lopez"]
        ),
        ["Asian"] = (
            ["Wei", "Ming", "Raj", "Yuki", "Min"],
            ["Chen", "Patel", "Kim", "Lee", "Wang"]
        ),
        ["Black"] = (
            ["Jamal", "Tyrone", "Marcus", "DeAndre", "Malik"],
            ["Washington", "Jefferson", "Jackson", "Robinson", "Harris"]
        )
    };

    public (string First, string Last) GenerateName(string race, string gender)
    {
        var (firstNames, lastNames) = _namesByRace[race];
        return (
            Random.Shared.PickRandom(firstNames),
            Random.Shared.PickRandom(lastNames)
        );
    }
}
```

**Effort**: 3 days

---

## Implementation Roadmap

### Phase 1: Complete Layer 2 (2-3 weeks)
**Goal**: Make scenarios realistic with correlations and probability

**Tasks**:
1. Week 1: Probabilistic transitions
   - `ProbabilisticBranchState`
   - Disease prevalence data
   - Age-dependent onset

2. Week 2: Value correlations
   - `VitalSignCorrelationEngine`
   - BMI → BP/Cholesterol
   - Diabetes → Glucose/A1C

3. Week 3: Reusable fragments
   - `CallSubScenarioState`
   - Common scenario fragments
   - Data-driven configuration

**Deliverables**:
- ✅ Probabilistic disease onset
- ✅ Realistic vital sign correlations
- ✅ Reusable scenario components

---

### Phase 2: Build Layer 3 (3-4 weeks)
**Goal**: Full patient lifecycles from birth to death

**Tasks**:
1. Week 1-2: Lifecycle orchestrator
   - `PatientLifecycleGenerator`
   - `ILifecycleEvent` interface
   - Age progression engine

2. Week 3: Lifecycle events
   - `PediatricWellnessSchedule`
   - `AdultWellnessSchedule`
   - `ImmunizationScheduleEvent`
   - `ProbabilisticConditionOnset`

3. Week 4: Risk calculation
   - `DiseaseRiskCalculator`
   - Age-dependent risks
   - Multi-factor risk models

**Deliverables**:
- ✅ Generate full patient lifecycles
- ✅ Age-appropriate events
- ✅ Realistic disease progression

---

### Phase 3: Build Layer 4 (4-5 weeks)
**Goal**: Generate realistic populations

**Tasks**:
1. Week 1-2: Demographics data
   - Parse demographics.csv
   - `DemographicsDataProvider`
   - City selection algorithm

2. Week 3: Population generator
   - `PopulationGenerator`
   - Demographic sampling
   - Batch lifecycle generation

3. Week 4: Geographic consistency
   - `EthnicNameGenerator`
   - Address → Zip → Area code
   - Timezone mapping

4. Week 5: Export & validation
   - NDJSON export
   - FHIR Bundle export
   - Population statistics validation

**Deliverables**:
- ✅ Generate 1,000+ patient populations
- ✅ Realistic demographic distributions
- ✅ Geographic consistency
- ✅ Export to standard formats

---

## Total Timeline

| Phase | Duration | Cumulative |
|-------|----------|------------|
| Phase 1: Complete Layer 2 | 2-3 weeks | 3 weeks |
| Phase 2: Build Layer 3 | 3-4 weeks | 7 weeks |
| Phase 3: Build Layer 4 | 4-5 weeks | 12 weeks |
| **Total** | **9-12 weeks** | **~3 months** |

---

## Success Metrics

### Layer 2 Success:
- ✅ Scenarios use realistic disease prevalence (e.g., 8.6% appendicitis risk)
- ✅ Vital signs correlate with BMI/conditions
- ✅ 20+ reusable scenario fragments
- ✅ Probabilistic branching in 50% of scenarios

### Layer 3 Success:
- ✅ Generate complete 0-78 year lifecycles
- ✅ Wellness visits at correct ages
- ✅ Age-appropriate immunizations
- ✅ Realistic disease progression over decades

### Layer 4 Success:
- ✅ Generate 10,000-patient populations in < 10 minutes
- ✅ Demographics match census data within 5% error
- ✅ Geographic consistency (address/zip/phone/timezone)
- ✅ Export to FHIR bundles, NDJSON

---

## Alternatives Considered

### Alternative 1: Build Layer 4 First
**Pros**: Most impressive output (populations)
**Cons**: Can't generate realistic data without Layer 2/3
**Decision**: Rejected - build foundation first

### Alternative 2: Skip Layer 3 (Lifecycles)
**Pros**: Faster to population generation
**Cons**: Each patient would be a single snapshot, not a journey
**Decision**: Rejected - lifecycles are valuable

### Alternative 3: Use Synthea Directly
**Pros**: Already built
**Cons**: Java-based, doesn't integrate with our .NET stack
**Decision**: Rejected - build C#-native version

---

## Consequences

### Positive:
- Clear architectural layers with separation of concerns
- Incremental delivery - each phase delivers value
- Can reuse Synthea's patterns and data
- Version-agnostic FHIR generation
- C#-native, .NET 9+ optimized

### Negative:
- 3-month timeline to full implementation
- Need to source demographics data (29,000 cities)
- Complexity increases with each layer
- Will need ongoing maintenance as FHIR versions evolve

### Neutral:
- Different paradigm than Synthea (C# fluent API vs JSON state machines)
- Need to make architectural decisions Synthea didn't document

---

## References

- Synthea source code: `old-src/Synthea/`
- Synthea deep-dive analysis: `docs/investigations/synthea-deep-dive.md`
- Gap analysis: `docs/investigations/faker-gap-analysis.md`
- FHIR version compatibility: `docs/investigations/fhir-version-compatibility.md`

---

## Appendix: Code Examples

### Example 1: Layer 2 - Probabilistic Scenario
```csharp
var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(age: 10, gender: "male")
    .AddProbabilisticBranch(
        (0.263, builder => builder  // 26.3% get asthma
            .AddConditionOnset(FhirCode.Conditions.Asthma)
            .AddMedicationOrder(FhirCode.Medications.Albuterol)),
        (0.737, builder => builder  // 73.7% stay healthy
            .Complete())
    )
    .Build();
```

### Example 2: Layer 3 - Full Lifecycle
```csharp
var lifecycle = new PatientLifecycleGenerator(schemaProvider)
    .WithBirthYear(1980)
    .WithGender("female")
    .WithRiskFactors(smoker: false, bmi: 28)

    // Deterministic events
    .AddWellnessSchedule(pediatric: true, adult: true)
    .AddImmunizationSchedule()

    // Probabilistic conditions
    .AddProbabilisticCondition("Asthma", onsetAges: 1..17, probability: 0.263)
    .AddProbabilisticCondition("Diabetes", onsetAges: 35..65, probability: age =>
        new DiseaseRiskCalculator().CalculateDiabetesRisk(age, false, 28, false))

    .SimulateUntilAge(45)
    .Generate();

// Result: 45 years of encounters, conditions, observations, medications
```

### Example 3: Layer 4 - Population
```csharp
var population = new PopulationGenerator(schemaProvider)
    .WithDemographicsData("data/demographics.csv")
    .ForState("Massachusetts")
    .WithSize(10000)
    .Generate();

// Statistics
var demographics = population.AnalyzeDemographics();
Console.WriteLine($"Race distribution: {demographics.RaceDistribution}");
Console.WriteLine($"Age distribution: {demographics.AgeDistribution}");
Console.WriteLine($"Total resources: {population.Sum(p => p.AllResources.Count)}");

// Export
population.ExportToNdJson("output/ma_10k.ndjson");
population.ExportToBundles("output/bundles/", patientsPerBundle: 100);
```

---

**Next Action**: Begin Phase 1 implementation (Complete Layer 2)
