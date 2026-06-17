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

# Generate any resource type at maximize density (all optional elements populated)
ignixa-fakes r4 resource AllergyIntolerance --out ./output --density maximize
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

### Command Reference

| Command | Options |
|---------|---------|
| `scenario <name>` | `--out`, `--resolved-references`, `--validate` |
| `population` | `--out`, `--from`, `--count`, `--resolved-references`, `--ndjson` |
| `resource <type> [stateName]` | `--out`, `--firstname`, `--surname`, `--from`, `--validate`, `--edge-cases [selectors]`, `--seed`, `--include-invalid`, `--density`, `--verbose` |
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
    Density = GenerationDensity.Maximize
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
| `Maximize` | Required elements plus every optional element populated. |

### Library Usage

```csharp
var faker = new SchemaBasedFhirResourceFaker(schemaProvider)
{
    Density = GenerationDensity.Maximize
};

var fullyPopulatedPatient = faker.Generate("Patient");
var fullyPopulatedAllergyIntolerance = faker.Generate("AllergyIntolerance");
```

Or with a seed:

```csharp
var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 42)
{
    Density = GenerationDensity.Maximize
};
```

### CLI

Pass `--density minimal|realistic|maximize` to the `resource` command:

```bash
ignixa-fakes r4 resource AllergyIntolerance --out ./output --density maximize
ignixa-fakes r4 resource Patient --out ./output --density maximize --seed 42
```

**Important:** when `--density` is `realistic` or `maximize`, the `resource` command uses the schema-based generator for any resource type and **ignores** `--firstname`, `--surname`, `--from`, and the Observation `stateName` specialisation. The filename includes the density label: `{version}-{resourcetype}-{density}-{id}.json`.

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
