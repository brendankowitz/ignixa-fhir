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

Or install the CLI tool:

```bash
dotnet tool install --global Ignixa.FhirFakes.Cli
```

## Quick Start

```csharp
using Ignixa.FhirFakes;

var faker = new FhirFaker();

// Generate a single patient
var patient = faker.GeneratePatient();

// Generate with relationships
var bundle = faker.GeneratePatientBundle(
    includeConditions: true,
    includeObservations: true,
    includeMedications: true
);
```

## CLI Usage

```bash
# Generate 100 patients
ignixa-fakes patient --count 100 --output patients.ndjson

# Generate a complete population
ignixa-fakes population --patients 1000 --output ./data

# Generate with specific scenarios
ignixa-fakes scenario diabetic-patient --count 50 --output diabetic.ndjson
```

## Generation Layers

FhirFakes uses a 4-layer generation architecture:

```
┌─────────────────────────────────────────┐
│        Layer 4: Clinical Scenarios       │
│    (Diabetic patient, ICU admission)     │
├─────────────────────────────────────────┤
│         Layer 3: Relationships           │
│   (Patient → Observations → Providers)   │
├─────────────────────────────────────────┤
│         Layer 2: Demographics            │
│     (Names, addresses, identifiers)      │
├─────────────────────────────────────────┤
│          Layer 1: Schema-based           │
│    (FHIR structure compliance)           │
└─────────────────────────────────────────┘
```

## Patient Generation

### Basic Patient

```csharp
var patient = faker.GeneratePatient();
```

### With Options

```csharp
var options = new PatientOptions
{
    MinAge = 18,
    MaxAge = 65,
    Gender = AdministrativeGender.Female,
    AddressCountry = "US",
    IncludeIdentifiers = true,
    IdentifierSystems = ["http://hospital.org/mrn", "http://hl7.org/fhir/sid/us-ssn"]
};

var patient = faker.GeneratePatient(options);
```

### Demographics

```csharp
var demographics = new DemographicOptions
{
    Locale = "en-US",
    NameStyle = NameStyle.Western,
    AddressStyle = AddressStyle.USPostal
};

faker.Configure(demographics);
```

## Observation Generation

### Single Observation

```csharp
var observation = faker.GenerateObservation(
    code: "29463-7", // Body Weight (LOINC)
    system: "http://loinc.org",
    patientId: "Patient/123"
);
```

### Vital Signs Bundle

```csharp
var vitals = faker.GenerateVitalSigns(
    patientId: "Patient/123",
    encounterId: "Encounter/456",
    timestamp: DateTime.UtcNow
);

// Includes: heart rate, blood pressure, temperature, respiratory rate, O2 sat
```

### Lab Results

```csharp
var labs = faker.GenerateLabPanel(
    panel: LabPanel.BasicMetabolicPanel,
    patientId: "Patient/123"
);
```

## Clinical Scenarios

### Diabetic Patient

```csharp
var scenario = faker.GenerateScenario(ClinicalScenario.DiabeticPatient);

// Includes:
// - Patient with diabetes-related demographics
// - Condition: Type 2 Diabetes
// - Observations: A1C, glucose
// - MedicationRequests: metformin, insulin
// - CarePlan: diabetes management
```

### Available Scenarios

| Scenario | Description |
|----------|-------------|
| `HealthyAdult` | Routine wellness visit |
| `DiabeticPatient` | Type 2 diabetes management |
| `PregnancyJourney` | Prenatal through delivery |
| `CardiacPatient` | Heart disease management |
| `PediatricWellVisit` | Child wellness checkup |
| `EmergencyVisit` | ER encounter |
| `CancerPatient` | Oncology treatment |

### Custom Scenarios

```csharp
var customScenario = new ScenarioBuilder()
    .WithPatient(p => p.Age(45).Gender(Gender.Male))
    .WithCondition("I10", "http://hl7.org/fhir/sid/icd-10-cm") // Hypertension
    .WithMedication("197361", "http://www.nlm.nih.gov/research/umls/rxnorm") // Lisinopril
    .WithObservations(obs => obs
        .BloodPressure(130, 85)
        .HeartRate(72))
    .Build();

var bundle = faker.GenerateFromScenario(customScenario);
```

## Population Generation

### Generate Population

```csharp
var population = faker.GeneratePopulation(
    patientCount: 1000,
    options: new PopulationOptions
    {
        AgeDistribution = AgeDistribution.USCensus,
        GenderRatio = 0.51, // 51% female
        IncludeClinicalData = true,
        ChronicDiseasePrevalence = 0.30 // 30% with chronic conditions
    }
);
```

### Export to Files

```csharp
await faker.ExportPopulation(
    population,
    outputPath: "./generated-data",
    format: ExportFormat.Ndjson,
    splitByResourceType: true
);

// Creates:
// ./generated-data/Patient.ndjson
// ./generated-data/Observation.ndjson
// ./generated-data/Condition.ndjson
// ...
```

## Seeding & Reproducibility

### Deterministic Generation

```csharp
var faker = new FhirFaker(seed: 42);

// Same seed = same data
var patient1 = faker.GeneratePatient(); // Always same patient
```

### Named Seeds

```csharp
var faker = new FhirFaker("test-scenario-1");
```

## FHIR Version Support

```csharp
// Generate for specific FHIR version
var faker = new FhirFaker(FhirVersion.R4);
var faker5 = new FhirFaker(FhirVersion.R5);
```

## Integration with Testing

### xUnit Example

```csharp
public class PatientTests
{
    private readonly FhirFaker _faker = new(seed: 12345);

    [Fact]
    public void PatientSearch_ByName_ReturnsResults()
    {
        // Arrange
        var patient = _faker.GeneratePatient();
        await _repository.CreateAsync(patient);

        // Act
        var results = await _searchService.Search("Patient", $"name={patient.Name}");

        // Assert
        results.ShouldContain(p => p.Id == patient.Id);
    }
}
```

## Related Documentation

- [Core SDK Overview](/docs/core-sdk/overview)
- [Validation](/docs/core-sdk/validation)
