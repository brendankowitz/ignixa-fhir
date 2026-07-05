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

#### Additional Resource Builders

Beyond `PatientBuilder` and `ObservationBuilder`, the library ships fluent builders for 13 more
resource types. Every builder follows the same shape: a static `Create(schemaProvider)` factory,
chainable `With*`/`Add*` methods, and a `Build()` that returns a `ResourceJsonNode`.

| Builder | Purpose | Notes |
|---------|---------|-------|
| `PractitionerBuilder` | Practitioners with name, NPI, and specialty qualifications | `Create(schemaProvider)` |
| `PractitionerRoleBuilder` | Links a practitioner to an organization with roles, specialties, locations | `Create(schemaProvider)` |
| `OrganizationBuilder` | Organizations with NPI/Tax ID, address, type, telecom; has `Hospital()`/`Clinic()`/`InsuranceCompany()` presets | `Create(schemaProvider)` |
| `OrganizationAffiliationBuilder` | Affiliations between organizations (networks, roles, specialties, locations, services) | `Create(schemaProvider)` |
| `LocationBuilder` | Locations, including building/floor/room hierarchies via `WithPartOf` | `Create(schemaProvider)` |
| `HealthcareServiceBuilder` | Services offered by an organization, with categories, types, and locations | `Create(schemaProvider)` |
| `CareTeamBuilder` | Care teams with participants and roles | `Create(schemaProvider)` |
| `GroupBuilder` | Actual (member-list) or descriptive groups of patients/practitioners/devices | `Create(schemaProvider)` |
| `DiagnosticReportBuilder` | Diagnostic report findings referencing one or more Observations | `Create(schemaProvider)` |
| `MedicationRequestBuilder` | Medication orders, coded or by reference, with requester and timing | `Create(schemaProvider)` |
| `MedicationDispenseBuilder` | Dispense records linked to an authorizing prescription and performer | `Create(schemaProvider)` |
| `RiskAssessmentBuilder` | Risk predictions with a `probability` value, for testing FHIR number search parameters | `Create(schemaProvider)` |
| `ValueSetBuilder` | Minimal ValueSet resources (url, name, status, version) | `Create(schemaProvider)` |

```csharp
using Ignixa.FhirFakes.Builders;

var practitioner = PractitionerBuilder.Create(schemaProvider)
    .WithName("Alice", "Anderson")
    .WithNpi("1234567890")
    .WithSpecialty("207Q00000X", system: "http://nucc.org/provider-taxonomy", display: "Family Medicine")
    .Build();

var organization = OrganizationBuilder.Create(schemaProvider)
    .WithName("Boston Medical Center")
    .WithAddress("725 Albany St", "Boston", "MA", "02118")
    .WithType("prov", display: "Healthcare Provider")
    .Build();

var request = MedicationRequestBuilder.Create(schemaProvider)
    .WithStatus("active")
    .WithIntent("order")
    .WithSubject(patient.Id!)
    .WithMedicationCodeableConcept("860975", "http://www.nlm.nih.gov/research/umls/rxnorm", "Metformin 500mg")
    .WithRequester(practitioner.Id!)
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

## Patient Lifecycle Generation

`PatientLifecycleGenerator` (Layer 3) simulates a single patient's life year-by-year from birth to a
target age, executing wellness visits, immunizations, and probabilistic condition onset as it goes.
It's the per-patient engine that `PopulationGenerator` (Layer 4) drives once for every generated patient.

```csharp
using Ignixa.FhirFakes.Lifecycle;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;

var lifecycle = new PatientLifecycleGenerator(schemaProvider)
    .WithBirthYear(1980)
    .WithGender("female")
    .AddWellnessSchedule(pediatric: true, adult: true)
    .AddImmunizationSchedule()
    .AddProbabilisticCondition(
        "Type 2 Diabetes",
        onsetAges: 40..65,
        probability: 0.15,
        scenarioFactory: sp => new ScenarioBuilder(sp)
            .AddConditionOnset(FhirCode.Conditions.DiabetesType2, severity: 2)
            .AddMedicationOrder(FhirCode.Medications.Metformin500mg, isChronic: true, frequency: "BID", reasonCode: FhirCode.Conditions.DiabetesType2));

ScenarioContext context = lifecycle.SimulateUntilAge(45);

var patient = context.Patient;
var conditions = context.Conditions; // 0 or 1 entries — probabilistic, checked once per applicable age
```

**Entry points**: `WithBirthYear`/`WithGender`/`WithGivenName`/`WithFamilyName`/`WithZipCode`/`WithAreaCode`
configure the patient generated at age 0 (delegating to `PatientBuilder` internally).
`AddWellnessSchedule(pediatric, adult)` and `AddImmunizationSchedule()` add deterministic, age-gated
events (`PediatricWellnessSchedule`, `AdultWellnessSchedule`, `ImmunizationScheduleEvent`).
`AddProbabilisticCondition(conditionName, onsetAges, probability, scenarioFactory)` registers a
condition that's rolled once per applicable age within the given `Range` and, if it hits, invokes the
`scenarioFactory` to add the clinical resources (condition, medications, etc.) at that point in the
timeline. `AddEvent(ILifecycleEvent)` accepts a custom event for anything not covered by the above.
`SimulateUntilAge(targetAge)` runs the simulation and returns the accumulated `ScenarioContext`.

`DiseaseRiskCalculator` supplies evidence-based probabilities (CDC, NHANES, Framingham, SEER) so a
condition's `probability` argument doesn't have to be a hand-picked constant:

```csharp
var riskCalc = new DiseaseRiskCalculator();
var diabetesRisk = riskCalc.CalculateDiabetesRisk(age: 50, smoker: false, bmi: 35m, familyHistory: true);
// CalculateHypertensionRisk, CalculateAsthmaRisk, CalculateCancerRisk, and CalculateStrokeRisk
// follow the same pattern — age plus risk factors in, a capped 0.0-1.0 probability out.
```

`LifecycleExampleScenarios` bundles five ready-made lifecycles built from these two types —
`GetHealthyChildLifecycle`, `GetTypicalAdultLifecycle`, `GetMetabolicSyndromeLifecycle`,
`GetPediatricAsthmaLifecycle`, and `GetElderlyMultiMorbidityLifecycle` — usable directly or as templates
for a custom lifecycle:

```csharp
using Ignixa.FhirFakes.Lifecycle;

var context = LifecycleExampleScenarios.GetTypicalAdultLifecycle(schemaProvider);
Console.WriteLine($"Conditions: {context.Conditions.Count}, Medications: {context.Medications.Count}");
```

**Relationship to Population Generation**: `PopulationGenerator.Generate(state, populationSize)` samples
a city and patient demographics via `PatientBuilderFactory.Create(schemaProvider).FromCity(city)` for
each patient, then feeds that same birth year, gender, name, zip, and area code into a fresh
`PatientLifecycleGenerator`, adds age-appropriate wellness/immunization schedules plus age/BMI/smoking/
family-history-stratified probabilistic conditions (via `DiseaseRiskCalculator`), and simulates each
patient up to their sampled current age. See [Layer 4: Population Generation](#layer-4-population-generation) above.

## Patient Profiles

`Builders/Profiles/` implements country-specific FHIR patient profiles — the extensions, identifiers,
and name-generation locale a `Patient` resource needs to conform to a given national base profile (US
Core, AU Base, UK Core). `PatientBuilder.FromCity(city)` auto-selects the right profile for a city's
country; `PatientBuilder.WithProfile(profile)` selects one explicitly.

```csharp
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;

// Auto-selected: FromCity resolves the country's profile via CityDemographics.GetProfile()
var ukPatient = PatientBuilderFactory.Create(schemaProvider)
    .FromCity(KnownCities.London)
    .WithAge(45)
    .Build();
// ukPatient carries a UK Core Ethnic Category extension and an NHS Number identifier
// (with a verification-status extension), generated from the London ethnic-category distribution.

// Explicit profile selection, with a required profile attribute supplied manually
var usPatient = PatientBuilderFactory.Create(schemaProvider)
    .WithProfile(PatientProfileFactory.USCore)
    .WithAge(40)
    .WithAttribute(USCorePatientProfile.UsCoreRaceAttribute, USCorePatientProfile.Race.Black)
    .Build();
```

**`IPatientProfile`** is the contract every profile implements: `NameGenerationStrategy` (locale-aware
name generation), `ProfileUrl` and `CountryCode`, `RequiredAttributes` (attribute keys
`DemographicsDataProvider` must sample), `BuildExtensions`/`BuildIdentifiers` (the profile-specific
FHIR extensions and identifiers to add), `ValidateAttributes`, and `SampleProfileAttributes` (draws
profile attributes from a city's demographic distribution using a seeded `Bogus.Randomizer`).

`PatientProfileFactory` centralizes lookup: `GetProfile(countryCode)` (falls back to
`DefaultPatientProfile` for an unrecognized or missing code), the `USCore`/`AUBase`/`UKCore`/`Default`
static accessors used above, and `RegisterProfile(countryCode, profile)` for adding a custom profile.

| Profile | Country | Identifier | Key extension |
|---------|---------|------------|----------------|
| `USCorePatientProfile` | US | — | Race (`us-core-race`) and ethnicity (`us-core-ethnicity`) |
| `AUBasePatientProfile` | AU | — | Indigenous Status (ABS-coded) |
| `UKCorePatientProfile` | GB (alias `UK`) | NHS Number (Modulus-11 check digit, with verification-status extension) | Ethnic Category (ONS 2011 census codes) |
| `DefaultPatientProfile` | fallback | — | none — used when no country-specific profile is registered |

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

### Programmatic Discovery

`ScenarioCatalog` and `ObservationStateCatalog` discover predefined scenarios and observation states by
reflection, so a UI (or any other consumer) can enumerate them without hard-coding extension method names:

```csharp
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;

// List all discovered scenarios with their metadata
foreach (var scenario in ScenarioCatalog.GetAll())
{
    Console.WriteLine($"{scenario.Id}: {scenario.Title} ({scenario.Category})");
    // scenario.Domain is the ClinicalDomain (e.g. Endocrinology), or null if undeclared
    foreach (var parameter in scenario.Parameters)
    {
        Console.WriteLine($"  {parameter.Name}: {parameter.Type.Name} (min {parameter.Min}, max {parameter.Max})");
    }
}

// Find one by id (case-insensitive) and invoke it with parameter overrides
var found = ScenarioCatalog.Find("DiabeticPatient");
if (found is not null)
{
    var overrides = new Dictionary<string, object?> { ["age"] = 60, ["severity"] = 3 };
    var context = ScenarioCatalog.Invoke(found, schemaProvider, overrides);
}

// Discover observation states the same way
foreach (var name in ObservationStateCatalog.GetNames())
{
    if (ObservationStateCatalog.TryCreate(name, out var state))
    {
        // use state
    }
}
```

`ScenarioCatalog.Invoke` throws `ScenarioInvocationException` (wrapping the original exception) if the
scenario's factory method itself throws, and `ArgumentException` if a parameter override's type doesn't
match the parameter's declared CLR type. A `[Scenario]`-annotated factory method can declare an explicit
`Id` (to survive a method rename without breaking a published id) and a `Domain` (`ClinicalDomain`, e.g.
`Cardiology`, `Endocrinology`) distinct from the free-text `Category` UI grouping label.

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

## Workflow Scenario Packs

`ScenarioBuilder` is deliberately one-scenario-one-patient. A workflow scenario pack sits one layer
above it: it composes several `ScenarioContext`s (and non-patient resources like practitioners) into a
single `ResourceGraph`, optionally links them together with a post-processing enricher, and returns a
bundle-ready result. Use this layer when a fixture needs more than one patient in the same graph — e.g.
a practitioner's daily appointment schedule linking multiple patients, encounters, and a shared
practitioner roster.

### Building a graph: `WorkflowGraphBuilder`

`WorkflowGraphBuilder` is a fluent wrapper over `ResourceGraph`. It lets you register
`IResourceGraphEnricher` factories while the graph is still being assembled, then applies them, in
registration order, once at `Build()` time:

```csharp
using Ignixa.FhirFakes.Workflow;

var workflowGraph = new WorkflowGraphBuilder();

// Add resources/scenarios as they're generated
workflowGraph.AddScenario(practitionerContext);
workflowGraph.AddScenario(patientEncounterContext);

// Register an enricher factory — invoked at Build() time, not here, so it can read
// graph state (e.g. every practitioner/patient added so far) that doesn't exist yet
workflowGraph.WithEnrichers(graph => new AppointmentSchedulingEnricher(practitioners, appointmentSubjects, scheduleDate));

var graph = workflowGraph.Build(new ResourceGraphEnrichmentContext
{
    SchemaProvider = schemaProvider,
    Faker = faker,
    Clock = TimeProvider.System,
});
```

An `IResourceGraphEnricher` mutates a `ResourceGraph` in place — adding workflow-only resources
(appointments, lists, document references) and cross-referencing them into resources already in the
graph. Implementations should be stateless with respect to execution so one configured instance is safe
to reuse.

### Discovering and invoking packs: `WorkflowScenarioCatalog`

Predefined workflow packs are public static methods on public types in a `*.Workflow.Predefined`
namespace, discovered by reflection — the same convention `ScenarioCatalog` uses for single-patient
scenarios, but returning `WorkflowScenarioResult` instead of `ScenarioContext`:

```csharp
using Ignixa.FhirFakes.Workflow;

foreach (var pack in WorkflowScenarioCatalog.GetAll())
{
    Console.WriteLine($"{pack.Id}: {pack.Title} ({pack.Category})");
}

var found = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule");
if (found is not null)
{
    var overrides = new Dictionary<string, object?> { ["practitionerCount"] = 2, ["appointmentCount"] = 20 };
    var result = WorkflowScenarioCatalog.Invoke(found, schemaProvider, new WorkflowScenarioOptions { Seed = 42 }, overrides);

    // result.Graph is the assembled ResourceGraph; result.Manifest describes what was generated
    Console.WriteLine(result.Manifest.ResourceCountsByType["Appointment"]);
}
```

**Determinism caveat:** `WorkflowScenarioOptions.Seed` only reproduces the `PatientBuilder`-generated
demographics of each patient in the pack — names, birthdates, and other sampled attributes come out
identical across runs with the same seed. It does **not** make the whole generated graph
byte-reproducible: every `ScenarioState.Execute()` call mints a fresh resource id via `Guid.NewGuid()`,
and `PractitionerState` draws names from its own unseeded `Bogus.Faker`, independent of the pack's seed.
Don't rely on a workflow pack producing byte-identical output run-to-run — only patient demographics are
stable today.

`WorkflowScenarioCatalog.Invoke` throws `ScenarioInvocationException` (wrapping the original exception)
if the pack's factory method itself throws.

#### Registering private workflow packs

A downstream consumer can ship its own workflow packs without forking scenario-discovery logic by
registering its assembly:

```csharp
WorkflowScenarioCatalog.RegisterAssembly(typeof(MyCompany.Fixtures.Workflow.Predefined.MyPackScenario).Assembly);

// Now discoverable through the same catalog as built-in packs
var pack = WorkflowScenarioCatalog.Find("MyPack");
```

Registration is idempotent and additive. The namespace convention is matched by suffix
(`*.Workflow.Predefined`), not by owning assembly, so a private pack's namespace does not need to live
under `Ignixa.FhirFakes` — only its last two segments need to be `Workflow.Predefined`.

### Composing bundles: `ResourceBundleComposer`

Once a `ResourceGraph` is assembled, `ResourceBundleComposer` — the same shared composer
`ScenarioContext.ToBundle()`/`ToBatchBundle()` use — turns it into a transaction or batch `Bundle`,
identically shaped to a scenario's output:

```csharp
using Ignixa.FhirFakes;

var transactionBundle = ResourceBundleComposer.ToTransactionBundle(result.Graph.AllResources);
// urn:uuid fullUrls + POST requests — the server assigns ids and resolves cross-references.

var batchBundle = ResourceBundleComposer.ToBatchBundle(result.Graph.AllResources);
// ResourceType/id fullUrls + PUT requests — for resources that already carry their final ids.
```

See [Workflow Command](#workflow-command) in the CLI Tool section below for command-line usage.

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

# Override a scenario parameter (repeatable), e.g. patient age and severity
ignixa-fakes r4 scenario DiabeticPatient --out ./output --param age=60 --param severity=3

# List available scenarios
ignixa-fakes help scenarios
```

`--param name=value` is repeatable and matches by parameter name (case-insensitive); the raw string is
parsed into the parameter's declared CLR type (int, decimal, bool, string, or enum) using invariant
culture. An unknown parameter name or an unparseable value exits with code `2`.

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

Generate individual resources based on schema. This command now supports edge-case perturbation, seeded reproducibility, and density control.

```bash
# Generate a random Patient resource
ignixa-fakes r4 resource Patient --out ./output

# Generate with explicit demographics
ignixa-fakes r4 resource Patient --out ./output --firstname Jane --surname Doe --from Boston

# Generate a seeded, reproducible Patient
ignixa-fakes r4 resource Patient --out ./output --seed 42

# Generate with all edge-case families applied and validate the result
ignixa-fakes r4 resource Patient --out ./output --edge-cases --seed 42 --validate

# Apply only the unicode and temporal families
ignixa-fakes r4 resource Patient --out ./output --edge-cases unicode,temporal --seed 42

# Apply a single category
ignixa-fakes r4 resource Patient --out ./output --edge-cases unicode.rtl --seed 42

# Include non-validity-preserving strategies (MayViolate / AlwaysInvalid) for negative testing
ignixa-fakes r4 resource Patient --out ./output --edge-cases --include-invalid --validate

# Generate an Observation in a specific clinical state
ignixa-fakes r4 resource Observation BloodGlucose --out ./output

# Generate any resource type at maximum density (all optional elements populated)
ignixa-fakes r4 resource AllergyIntolerance --out ./output --density maximum

# Generate at maximum density with a clinical theme so coded fields agree with each other
ignixa-fakes r4 resource Procedure --out ./output --density maximum --theme orthopedic-surgery

# Omit --theme for a random (but still coherent) theme per resource
ignixa-fakes r4 resource Procedure --out ./output --density maximum

# Disable theming entirely (pre-theming behavior: every coded field picked independently)
ignixa-fakes r4 resource Procedure --out ./output --density maximum --theme none
```

**Exit codes for scripting / CI:**

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | Runtime error (generation or I/O failure, or `--validate` failed without `--include-invalid`) |
| `2` | Usage error (invalid arguments, unknown `--edge-cases` selector, unknown `--density` value, unsupported resource type) |

When `--edge-cases` is specified without `--seed`, the CLI prints the auto-generated seed so you can replay the run:

```
Seed: 1234567890  (pass --seed 1234567890 to replay)
```

**Output files:**

For Patient and Observation (minimal density), the output filename is `{version}-patient-{id}.json` or `{version}-observation-{stateName}-{id}.json`. For non-minimal density or other resource types, the filename is `{version}-{resourcetype}-{density}-{id}.json`.

When edge cases are applied, a sidecar `.manifest.json` file is written alongside the resource file (see [Edge-Case Manifest](#edge-case-manifest)).

### Workflow Command

Generate a predefined [workflow scenario pack](#workflow-scenario-packs) as a transaction or batch
bundle plus a manifest — the same output shape as the `scenario` command:

```bash
# Generate the built-in daily-schedule workflow pack (transaction bundle, default)
ignixa-fakes r4 workflow DailyAppointmentSchedule --out ./output --seed 42

# Override pack parameters
ignixa-fakes r4 workflow DailyAppointmentSchedule --out ./output --param practitionerCount=3 --param appointmentCount=30

# Batch bundle (resolved ResourceType/id references) instead of transaction
ignixa-fakes r4 workflow DailyAppointmentSchedule --out ./output --resolved-references

# Tag every generated resource for test isolation, and validate the output
ignixa-fakes r4 workflow DailyAppointmentSchedule --out ./output --tag my-test-run --validate
```

**Output**: one `{version}-workflow-{scenario}-{guid}.json` bundle file, plus a
`{version}-workflow-{scenario}-{guid}-manifest.json` describing the scenario id, seed, primary resource
type, and per-type resource counts.

### Command Reference

| Command | Options |
|---------|---------|
| `scenario <name>` | `--out`, `--resolved-references`, `--validate`, `--param name=value` (repeatable) |
| `population` | `--out`, `--from`, `--count`, `--resolved-references`, `--ndjson` |
| `resource <type> [stateName]` | `--out`, `--firstname`, `--surname`, `--from`, `--validate`, `--edge-cases [selectors]`, `--seed`, `--include-invalid`, `--density`, `--theme`, `--verbose` |
| `workflow <name>` | `--out`, `--seed`, `--tag`, `--resolved-references`, `--validate`, `--param name=value` (repeatable) |
| `help scenarios` | Lists all available predefined scenarios |

## Deterministic / Reproducible Generation

All three generation surfaces support seeded, byte-reproducible output.

**Determinism contract:** the same seed plus the same configuration produces byte-identical JSON on every run, with one exception — `meta.lastUpdated` is stamped with the wall-clock time by the schema-based generator and therefore differs between runs even with an identical seed.

### PatientBuilder

Call `WithSeed(int)` on the builder, or pass `seed` to the factory:

```csharp
using Ignixa.FhirFakes.Builders;

// Via factory (recommended)
var patient = PatientBuilderFactory.Create(schemaProvider, seed: 42)
    .WithAge(35)
    .WithGender(g => g.Female)
    .Build();

// Via builder method
var patient = PatientBuilderFactory.Create(schemaProvider)
    .WithSeed(42)
    .WithAge(35)
    .Build();
```

`WithSeed(int)` sets the underlying `Bogus.Randomizer` for names, addresses, phone numbers, BMI, and the default id. When combined with `WithEdgeCases`, the edge-case pipeline derives its seed from the same base value unless you override it explicitly.

### SchemaBasedFhirResourceFaker

Pass a seed to the constructor. The seed is propagated to any `PatientBuilder` created internally (`CreatePatient`, `CreateSeattlePatient`):

```csharp
var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 42)
{
    Density = GenerationDensity.Maximum
};

var patient = faker.Generate("Patient");
var observation = faker.Generate("Observation");
```

### CLI

Pass `--seed` to the `resource` command. When `--edge-cases` is active and no `--seed` is provided, a seed is drawn at runtime and printed to stdout for replay:

```bash
# Explicit seed — fully reproducible
ignixa-fakes r4 resource Patient --out ./output --seed 42

# Auto seed with edge cases — printed to stdout
ignixa-fakes r4 resource Patient --out ./output --edge-cases
# Seed: 1234567890  (pass --seed 1234567890 to replay)
```

---

## Edge-Case / Fuzz Data Generation

Edge-case generation produces *valid-but-hostile* FHIR resources that stress parsers, validators, rendering layers, and data pipelines without requiring a separate fuzzing harness. It is layered over the existing realistic generators as a seeded decorator pass and is entirely opt-in.

### Concept

After a resource is fully constructed, the `EdgeCasePipeline` walks the schema-typed element tree and applies one eligible strategy per leaf. Targeting is schema-driven: the pipeline knows each leaf's FHIR type (`string`, `date`, `dateTime`, etc.) and whether it carries a required binding. This means:

- Free-text string and markdown fields receive unicode and string-boundary mutations.
- Date and dateTime fields receive temporal mutations.
- Bound codes, system URIs, reference values, and ids are never mutated.

### Edge-Case Catalog

The default catalog ships three families containing 15 strategies. Select by family name or individual category name (case-insensitive).

**`unicode` family** — mutates unbound `string` / `markdown` leaves:

| Category | Description | `ValidityIntent` |
|---|---|---|
| `unicode.cjk` | Replaces text with CJK (Chinese/Japanese/Korean) characters | `PreservesValidity` |
| `unicode.rtl` | Replaces text with right-to-left script (Arabic / Hebrew) | `PreservesValidity` |
| `unicode.combining` | Appends combining diacritical marks to each base character | `PreservesValidity` |
| `unicode.emoji` | Injects emoji (including ZWJ sequences and surrogate pairs) | `PreservesValidity` |
| `unicode.zero-width` | Injects zero-width characters (U+200B, U+200C, U+200D, U+FEFF) between code points | `PreservesValidity` |
| `unicode.multi-script-long` | Replaces text with a long (~40-fragment) string mixing Latin, CJK, RTL, Cyrillic, and emoji | `PreservesValidity` |

**`temporal` family** — mutates `date` / `dateTime` leaves:

| Category | Description | `ValidityIntent` |
|---|---|---|
| `temporal.leap-year` | Sets the date to Feb 29 of a leap year | `PreservesValidity` |
| `temporal.year-boundary` | Sets the date to Dec 31 or Jan 1 | `PreservesValidity` |
| `temporal.far-past` | Sets the date to a far-past but spec-valid date (e.g., `0001-01-01`) | `PreservesValidity` |
| `temporal.far-future` | Sets the date to a far-future but spec-valid date (e.g., `9999-12-31`) | `PreservesValidity` |
| `temporal.partial-precision` | Reduces date to year-only (`yyyy`) or year-month (`yyyy-MM`) precision | `PreservesValidity` |

**`string` family** (`StringBoundary`) — mutates unbound `string` / `markdown` leaves:

| Category | Description | `ValidityIntent` |
|---|---|---|
| `string.max-length` | Replaces text with a 4096-character ASCII string | `PreservesValidity` |
| `string.injection-like` | Replaces text with SQL/HTML/template-injection-resembling payloads (robustness testing, not a security feature) | `PreservesValidity` |
| `string.control-chars` | Injects C0 control characters — these are disallowed by the FHIR `string` grammar | `MayViolate` |
| `string.whitespace-only` | Sets text to whitespace-only — may fail profile validation | `MayViolate` |
| `string.empty-present` | Sets text to empty string — unconditionally invalid per FHIR spec | `AlwaysInvalid` |

**Validity by default.** The pipeline applies only `PreservesValidity` strategies unless `--include-invalid` (CLI) is set or `includeNonValidityPreserving: true` is passed directly to `EdgeCasePipeline.Apply`. Opting in enables `MayViolate` and `AlwaysInvalid` strategies for negative testing.

**`string.injection-like` note:** the payloads (SQL fragments, HTML, template expressions) are plain FHIR `string` values. They test that downstream renderers and storage layers handle hostile content correctly. This is a correctness-robustness feature, not a security testing tool.

**Families not yet implemented:** `Cardinality` and `Structural` are defined in `EdgeCaseFamily` and reserved for future strategies.

### Library Usage

```csharp
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.EdgeCases;

// Apply all strategies with an auto-derived seed (derived from WithSeed if set)
var builder = PatientBuilderFactory.Create(schemaProvider, seed: 42)
    .WithAge(45)
    .WithEdgeCases();

var patient = builder.Build();
var manifest = builder.LastEdgeCaseManifest; // non-null after Build()

// Apply only the unicode family
var builder2 = PatientBuilderFactory.Create(schemaProvider, seed: 42)
    .WithEdgeCases(selectors: ["unicode"]);

// Apply a specific category with an explicit edge-case seed
var builder3 = PatientBuilderFactory.Create(schemaProvider)
    .WithEdgeCases(seed: 99, selectors: ["unicode.rtl", "temporal"]);

// Include non-validity-preserving strategies (for negative testing)
// Use EdgeCasePipeline directly or the CLI --include-invalid flag;
// PatientBuilder.WithEdgeCases does not expose this flag — the pipeline default
// is PreservesValidity-only. Pass includeNonValidityPreserving to EdgeCasePipeline
// directly when you need that behaviour in code.
```

`WithEdgeCases(int? seed = null, IEnumerable<string>? selectors = null)` parameters:

| Parameter | Behaviour when omitted |
|-----------|----------------------|
| `seed` | Derived from `WithSeed` if set; otherwise drawn from the builder's randomizer |
| `selectors` | All registered strategies are applied |

After calling `Build()`, read the manifest from `PatientBuilder.LastEdgeCaseManifest`.

### Edge-Case Manifest

Every resource generated with edge cases emits a `MutationManifest`. The CLI writes it as a sidecar file alongside the resource (e.g., `r4-patient-{id}.manifest.json`). In code, it is available as `PatientBuilder.LastEdgeCaseManifest`.

Manifest JSON structure:

```json
{
  "resourceId": "a1b2c3d4-...",
  "seed": 1234567890,
  "mutations": [
    {
      "category": "unicode.cjk",
      "path": "name[0].family",
      "before": "Smith",
      "after": "山田太郎",
      "description": "Replaced free-text with CJK characters"
    },
    {
      "category": "temporal.leap-year",
      "path": "birthDate",
      "before": "1979-03-15",
      "after": "2000-02-29",
      "description": "Set date to Feb 29 of a leap year"
    }
  ]
}
```

The manifest is a replay record. To reproduce the exact output, re-run `EdgeCasePipeline` with the same `seed` against the same input resource and strategy set.

### Extending the Catalog

Register custom strategies against the catalog before passing it to the pipeline:

```csharp
using Ignixa.FhirFakes.EdgeCases;

var catalog = EdgeCaseCatalog.CreateDefault();
catalog.Register(new MyCustomStrategy());

var strategies = catalog.Resolve(selectors: null); // all strategies including custom
var pipeline = new EdgeCasePipeline(seed: 42, schemaProvider);
var manifest = pipeline.Apply(resource, strategies);
```

---

## Generation Density

`GenerationDensity` controls which elements `SchemaBasedFhirResourceFaker.Generate` emits. It is a generation concern separate from the edge-case catalog.

| Value | Behaviour |
|-------|-----------|
| `Minimal` | Required elements only. This is the default. |
| `Realistic` | Currently behaves identically to `Minimal` (reserved for future realistic optional-field selection). |
| `Maximum` | Required elements plus every optional element populated. |

### Library Usage

```csharp
var faker = new SchemaBasedFhirResourceFaker(schemaProvider)
{
    Density = GenerationDensity.Maximum
};

var fullyPopulatedPatient = faker.Generate("Patient");
var fullyPopulatedAllergyIntolerance = faker.Generate("AllergyIntolerance");
```

Or with a seed:

```csharp
var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 42)
{
    Density = GenerationDensity.Maximum
};
```

### CLI

Pass `--density minimal|realistic|maximum` to the `resource` command:

```bash
ignixa-fakes r4 resource AllergyIntolerance --out ./output --density maximum
ignixa-fakes r4 resource Patient --out ./output --density maximum --seed 42
```

**Important:** when `--density` is `realistic` or `maximum`, the `resource` command uses the schema-based generator for any resource type and **ignores** `--firstname`, `--surname`, `--from`, and the Observation `stateName` specialisation. The filename includes the density label: `{version}-{resourcetype}-{density}-{id}.json`.

### Theme-Consistent Generation

Without a shared theme, sibling coded fields on one resource are picked independently and can be
clinically incoherent (e.g. a `Procedure` with an unrelated `category`, `code`, and `bodySite`). `Theme`
(a `ClinicalDomain`, e.g. `Cardiology`, `Endocrinology`, `OrthopedicSurgery`) keeps coded picks on one
resource drawn from the same clinical specialty wherever the curated code pools have a tagged match,
falling back to the full pool otherwise.

**Density and Theme are orthogonal.** Density controls *which* elements are generated (required-only at
`Minimal`, required plus every optional at `Maximum`); Theme controls the *clinical coherence* of
whichever coded elements do get generated. Theme therefore applies to any coded element that gets
populated at **any** density — including required coded elements at `Minimal`. It is not a
`Maximum`-density-only feature.

```csharp
var faker = new SchemaBasedFhirResourceFaker(schemaProvider)
{
    Density = GenerationDensity.Maximum,
    Theme = ClinicalDomain.OrthopedicSurgery   // optional — omit to auto-pick one random theme per Generate() call
};

var procedure = faker.Generate("Procedure");
```

| `Theme` value | Behaviour |
|---|---|
| unset (`null`, the default) | A random `ClinicalDomain` is picked once per `Generate()` call and used for every themed pick on that resource. |
| `ClinicalDomain.Unspecified` | Theming disabled — every coded field is picked independently from the full pool (pre-theming behaviour). |
| Any other `ClinicalDomain` | Every themed pick on the resource is drawn from that domain's tagged codes, falling back to the full pool if a value set has no tagged match. |

Only the curated `Conditions`, `Medications`, `Observations`, and `Procedures` code pools are tagged in
this initial pass — value sets resolved via `IValueSetProvider` (not the curated pools) are unaffected.

### CLI

```bash
ignixa-fakes r4 resource Procedure --out ./output --density maximum --theme orthopedic-surgery
ignixa-fakes r4 resource Procedure --out ./output --density maximum --theme none
```

`--theme` accepts a `ClinicalDomain` name in kebab-case or PascalCase (case-insensitive), or `none` to
disable theming. Omit it for a random (but still coherent) theme per resource.

---

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
