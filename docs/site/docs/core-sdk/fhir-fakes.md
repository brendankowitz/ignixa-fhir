---
sidebar_position: 7
title: FHIR Fakes
description: Synthetic FHIR data generation
---

# Ignixa.FhirFakes

Generate realistic synthetic FHIR data for testing and development.

## Installation

```bash
dotnet add package Ignixa.FhirFakes
```

## Quick Start

```csharp
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.Specification;

var schemaProvider = FhirVersion.R4.GetSchemaProvider();

// Generate a patient with a clinical scenario
var scenario = schemaProvider.GetDiabeticPatient();

// Access generated resources
var patient = scenario.Patient;
var bundle = scenario.ToBundle();
```

## Generation Layers

FhirFakes uses a 4-layer architecture for generating realistic test data:

```
┌─────────────────────────────────────────┐
│    Layer 4: Population Generators       │
│   (PopulationGenerator - large scale)   │
├─────────────────────────────────────────┤
│    Layer 3: Scenarios & Predefined      │
│  (ScenarioBuilder, clinical journeys)   │
├─────────────────────────────────────────┤
│       Layer 2: States & Builders        │
│ (PatientBuilder, ObservationBuilder)    │
├─────────────────────────────────────────┤
│    Layer 1: Schema-Based Generation     │
│  (SchemaBasedFhirResourceFaker)         │
└─────────────────────────────────────────┘
```

### Layer 1: Schema-Based Resource Generation

Generate random resources based on FHIR schema metadata:

```csharp
using Ignixa.FhirFakes;

var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

// Generate a random Patient resource
var patient = faker.Generate("Patient");

// Generate with a tag for test isolation
faker.WithTag("test-run-123");
var taggedPatient = faker.Generate("Patient");
```

### Layer 2: States & Builders

Fluent builders for specific resource types with realistic demographics:

```csharp
using Ignixa.FhirFakes.Builders;

// Simple patient with manual demographics
var patient = PatientBuilderFactory.Create(schemaProvider)
    .WithAge(45)
    .WithGender(g => g.Male)  // Or: .WithGender("male")
    .WithGivenName("John")
    .WithFamilyName("Smith")
    .Build();

// Realistic patient from specific city (auto: race, age, gender, zip, area code, name)
var realisticPatient = PatientBuilderFactory.Create(schemaProvider)
    .FromCity(KnownCities.Boston)
    .WithAge(45)
    .WithRealisticBMI()
    .Build();
```

#### With Identifiers

```csharp
var patient = PatientBuilderFactory.Create(schemaProvider)
    .WithAge(40)
    .WithGender(g => g.Male)
    .WithTypedIdentifier(
        "12345",
        "http://terminology.hl7.org/CodeSystem/v2-0203",
        "MR",
        "Medical Record")
    .Build();
```

### Layer 3: Scenario Building

Build complete clinical scenarios with patient journeys:

```csharp
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;

var scenario = new ScenarioBuilder(schemaProvider)
    .WithName("Hypertension Screening")
    .WithPatient(p => p
        .WithAge(55)
        .WithGender(g => g.Male))
    .AddEncounter("Annual checkup")
    .AddObservation(VitalSigns.BloodPressureSystolic, 140m, "mmHg")
    .AddConditionOnset(FhirCode.Conditions.HypertensionEssential)
    .Build();

// Access resources
var patient = scenario.Patient;
var encounters = scenario.Encounters;
var observations = scenario.Observations;
var conditions = scenario.Conditions;
```

#### With Diagnostic Reports

```csharp
var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(p => p.WithAge(45).WithGender(g => g.Female))
    .AddEncounter("Wellness visit")
    .AddComprehensiveMetabolicPanel()
    .AddLipidPanel()
    .AddCompleteBloodCount()
    .Build();
```

#### Medication Orders

```csharp
using Ignixa.FhirFakes.Scenarios.States;

var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(p => p.WithAge(52))
    .AddEncounter("Diabetes follow-up")
    .AddMedicationOrder(MedicationOrderState.Metformin500mg())
    .Build();
```

### Layer 4: Population Generation

Generate large-scale populations with realistic demographics:

```csharp
using Ignixa.FhirFakes.Population;

var generator = new PopulationGenerator(schemaProvider);

// Generate 1000 patients from Massachusetts
foreach (var scenario in generator.Generate("Massachusetts", 1000))
{
    var bundle = scenario.ToBundle();
    // Post to FHIR server or save to file
}
```

#### Available States

```csharp
var generator = new PopulationGenerator(schemaProvider);

// See all available states with demographic data
foreach (var state in generator.AvailableStates)
{
    Console.WriteLine(state);
}
// Output: Arizona, California, Illinois, Massachusetts,
//         New York, Pennsylvania, Texas, Washington
```

## Predefined Scenarios

Extension methods on `IFhirSchemaProvider` for common clinical scenarios:

| Scenario | Extension Method |
|----------|------------------|
| Type 2 Diabetes | `GetDiabeticPatient()` |
| Hypertension | `GetHypertensivePatient()` |
| Pregnancy Journey | `GetPregnantPatient()` |
| Asthma (Pediatric) | `GetAsthmaticChild()` |
| Wellness Visit | `GetWellnessVisit()` |
| Emergency - Chest Pain | `GetChestPainVisit()` |
| Emergency - Abdominal Pain | `GetAbdominalPainVisit()` |
| Pediatric Ear Infection | `GetPediatricEarInfection()` |
| UTI | `GetUrinaryTractInfection()` |
| Breast Cancer | `GetBreastCancerPathway()` |
| Acute MI | `GetAcuteMyocardialInfarction()` |
| COPD | `GetCOPDManagementWithExacerbations()` |
| CKD Progression | `GetChronicKidneyDiseaseProgression()` |
| Metabolic Syndrome | `GetMetabolicSyndromeProgression()` |

### Example Usage

```csharp
using Ignixa.FhirFakes.Scenarios.Predefined;

var scenario = schemaProvider.GetDiabeticPatient(
    age: 52,
    gender: "male",
    severity: 2);

// Includes:
// - Patient with specified demographics
// - Condition: Type 2 Diabetes
// - Observations: A1C, blood glucose
// - MedicationRequests: Metformin
// - Multiple follow-up encounters
```

## Reusable Scenario Fragments

Compose scenarios from common patterns:

```csharp
using Ignixa.FhirFakes.Scenarios;

var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(p => p.WithAge(40).WithGender(g => g.Female))
    .AddEncounter("Wellness visit")
    .AddSubScenario(CommonScenarios.RecordVitalSigns())
    .AddSubScenario(CommonScenarios.BasicMetabolicPanel())
    .AddSubScenario(CommonScenarios.LipidPanel())
    .Build();
```

### Available Fragments

- `RecordVitalSigns()` - Height, weight, BMI, blood pressure
- `BasicMetabolicPanel()` - Comprehensive metabolic panel
- `CardiovascularVitals()` - Heart rate, BP, O2 saturation
- `LipidPanel()` - Cholesterol, LDL, HDL, triglycerides
- `CompleteBloodCount()` - CBC with differential

## Code Constants

The library provides SNOMED, LOINC, and RxNorm codes:

```csharp
using Ignixa.FhirFakes.Scenarios.Codes;

// Conditions (SNOMED CT)
FhirCode.Conditions.DiabetesType2
FhirCode.Conditions.Hypertension
FhirCode.Conditions.Asthma

// Vital Signs (LOINC)
VitalSigns.BloodPressureSystolic
VitalSigns.BloodPressureDiastolic
VitalSigns.BodyWeight
VitalSigns.BodyHeight
VitalSigns.BMI

// Lab Observations (LOINC)
LabObservations.Glucose
LabObservations.HemoglobinA1c
LabObservations.Cholesterol
```

## Export to NDJSON

```csharp
var generator = new PopulationGenerator(schemaProvider);

await using var writer = File.CreateText("population.ndjson");

foreach (var scenario in generator.Generate("California", 100))
{
    foreach (var resource in scenario.AllResources)
    {
        var json = resource.SerializeToString();
        await writer.WriteLineAsync(json);
    }
}
```

## Test Isolation with Tags

```csharp
var testTag = Guid.NewGuid().ToString();

var scenario = new ScenarioBuilder(schemaProvider)
    .WithTag(testTag)
    .WithPatient(p => p.WithAge(40))
    .AddEncounter("Visit")
    .Build();

var bundle = scenario.ToBundle();

// All resources in the bundle are tagged
// Search with: GET /Patient?_tag={testTag}
```

## CLI Tool

The `ignixa-fakes` tool generates FHIR test data from the command line.

### Installation

```bash
dotnet tool install --global Ignixa.FhirFakes.Cli
```

### Scenario Command

Generate predefined clinical scenarios as transaction bundles:

```bash
# Generate a diabetic patient scenario
ignixa-fakes r4 scenario DiabeticPatient --out ./output

# Generate with resolved references (batch bundle instead of transaction)
ignixa-fakes r4 scenario HypertensivePatient --out ./output --resolved-references

# Validate generated resources against schema
ignixa-fakes r4 scenario WellnessVisit --out ./output --validate

# List available scenarios
ignixa-fakes help scenarios
```

**Output**: `{version}-bundle-{scenario}-{guid}.json` (transaction or batch bundle)

### Population Command

Generate realistic patient populations:

```bash
# Generate 100 patients from Massachusetts as a single transaction bundle
ignixa-fakes r4 population --from Massachusetts --count 100 --out ./output

# Generate as separate batch bundles (one per patient)
ignixa-fakes r4 population --from Boston --count 50 --out ./output --resolved-references

# Generate as NDJSON files (one file per resource type)
ignixa-fakes r4 population --from California --count 1000 --out ./output --ndjson
```

**Output formats**:

| Option | Output Files |
|--------|--------------|
| (default) | Single `{version}-bundle-population-{state}-{count}-{guid}.json` transaction bundle |
| `--resolved-references` | Multiple `{version}-bundle-population-{state}-{count}-{n}-{guid}.json` batch bundles |
| `--ndjson` | Multiple `{version}-population-{state}-{type}-{count}-{guid}.ndjson` files per resource type |

The `--ndjson` format creates separate files for each resource type (Patient.ndjson, Observation.ndjson, Condition.ndjson, etc.), suitable for bulk import.

### Resource Command

Generate random resources based on schema:

```bash
# Generate a random Patient resource
ignixa-fakes r4 resource Patient --out ./output
```

### Command Reference

| Command | Options |
|---------|---------|
| `scenario <name>` | `--out`, `--resolved-references`, `--validate` |
| `population` | `--out`, `--from`, `--count`, `--resolved-references`, `--ndjson` |
| `resource <type>` | `--out` |
| `help scenarios` | Lists all available predefined scenarios |

## FHIR Version Support

```csharp
using Ignixa.Specification;

// R4
var r4Schema = FhirVersion.R4.GetSchemaProvider();
var scenario = new ScenarioBuilder(r4Schema)
    .WithPatient(p => p.WithAge(30))
    .Build();

// R5
var r5Schema = FhirVersion.R5.GetSchemaProvider();
var scenario = new ScenarioBuilder(r5Schema)
    .WithPatient(p => p.WithAge(30))
    .Build();
```

## Related Documentation

- [Core SDK Overview](/docs/core-sdk/overview)
- [Validation](/docs/core-sdk/validation)
