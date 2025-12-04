# Ignixa.FhirFakes

A comprehensive FHIR test data generation library for modeling patient populations and medical histories.

## Installation

```bash
dotnet add package Ignixa.FhirFakes
```

## Quick Start

### Generate a Single Patient with Full Lifecycle

```csharp
using Ignixa.FhirFakes.Lifecycle;
using Ignixa.Specification.Generated;

// Create schema provider
var schemaProvider = new R4CoreSchemaProvider();

// Generate a 50-year-old with metabolic syndrome progression
var context = LifecycleExampleScenarios.GetMetabolicSyndromeLifecycle(schemaProvider);

// Access generated resources
Console.WriteLine($"Patient: {context.Patient.Id}");
Console.WriteLine($"Conditions: {context.Conditions.Count}"); // 2-4 chronic conditions
Console.WriteLine($"Medications: {context.Medications.Count}"); // 3-6 medications
Console.WriteLine($"Encounters: {context.Encounters.Count}"); // 40-45 visits over 50 years
Console.WriteLine($"Observations: {context.Observations.Count}"); // 150+ vitals and labs
```

### Generate a Population from a State

```csharp
using Ignixa.FhirFakes.Population;
using Ignixa.Specification.Generated;

var schemaProvider = new R4CoreSchemaProvider();
var generator = new PopulationGenerator(schemaProvider);

// Generate 100 patients with Massachusetts demographics
// Note: First parameter is state name, not city name
var patients = generator.Generate("Massachusetts", 100);

// Result: 100 patients with:
// - Names: Culturally appropriate (Bogus locales)
// - Ages: Sampled from Boston age distribution
// - Race: 53% White, 25% Black, 19% Hispanic, 9% Asian
// - Zip Codes: 02101-02199 (Boston area)
// - Phone Numbers: 617-xxx-xxxx or 857-xxx-xxxx
// - Full medical history: birth to current age with realistic disease onset
```

### Discover Available Cities with KnownCities

```csharp
using Ignixa.FhirFakes.Population;

// Strongly-typed access with IntelliSense
var boston = KnownCities.Boston;
var seattle = KnownCities.Seattle;
var allCities = KnownCities.All; // All 11 cities

// Inspect city demographics
Console.WriteLine($"City: {boston.Name}, {boston.State}");
Console.WriteLine($"Population: {boston.Population:N0}");
Console.WriteLine($"Race Distribution: {boston.RaceDistribution["White"]:P0} White");
Console.WriteLine($"Zip Codes: {boston.ZipCodePrefix}xx");
Console.WriteLine($"Area Codes: {string.Join(", ", boston.AreaCodes)}");
```

### Create Custom Scenarios with Probabilistic Branching

```csharp
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;

var builder = new ScenarioBuilder(schemaProvider)
    // Simple patient with basic demographics
    .WithPatient(p => p.WithAge(55).WithGender(g => g.Male))

    // Or use realistic patient from specific city (ethnically appropriate names, real demographics)
    // .WithPatient(p => p.FromCity(KnownCities.Boston).WithAge(55).WithGender(g => g.Male))

    .AddEncounter("Annual wellness visit")

    // Use reusable fragments
    .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Vitals")
    .AddSubScenario(CommonScenarios.LipidPanel(), "Cholesterol Screening")

    // Probabilistic disease onset (15% chance of elevated cholesterol)
    .AddProbabilisticBranch(
        0.15,

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

var context = builder.Build();
```

## Architecture

### Layer 1: Random Resources
- `BindingAwareGenerator`: Core resource generation
- Respects FHIR profiles and terminology bindings
- Generates valid references and complex types

### Layer 2: Clinical Scenarios
- `ScenarioBuilder`: Fluent composition API
- `ProbabilisticBranchState`: Evidence-based branching
- `VitalSignCorrelationEngine`: Physiological realism
- `CommonScenarios`: Reusable clinical fragments

### Layer 3: Patient Lifecycles
- `LifecycleSimulator`: Age-based event scheduling
- `DiseaseRiskCalculator`: Evidence-based risk modeling
- `LifecycleExampleScenarios`: Pre-built patient archetypes:
  - Healthy child (0-18 years)
  - Typical adult (0-45 years)
  - Metabolic syndrome (0-50 years)
  - Pediatric asthma (0-10 years)
  - Elderly multi-morbidity (0-80 years)

### Layer 4: Population Generation
- `PopulationGenerator`: Large-scale cohort creation
- `CityDemographics`: US Census-based distributions
- `KnownCities`: 11 major US cities with real demographics
- `EthnicNameGenerator`: Culturally appropriate names via Bogus locales

**Dependencies**:
- `Ignixa.Specification` (FHIR schema provider)
- `Ignixa.Serialization` (FHIR serialization)
- `Bogus` (name and data generation)

## Inspiration and Complementary Tools

This library was inspired by [Synthea](https://github.com/synthetichealth/synthea), the well-established synthetic patient generator developed by The MITRE Corporation. Synthea is a mature, comprehensive tool that has been instrumental in advancing healthcare interoperability and providing high-quality synthetic data to the community.

## License

See LICENSE file in repository root.
