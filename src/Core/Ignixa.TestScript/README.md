# Ignixa.TestScript

A FHIR TestScript execution engine that parses and evaluates [TestScript](https://hl7.org/fhir/testscript.html) resources against any FHIR server.

## Installation

```bash
dotnet add package Ignixa.TestScript
```

## Quick Start

```csharp
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Fixtures;

// Parse a TestScript
var result = TestScriptParser.ParseFile("tests/patient-crud.json");
if (!result.IsSuccess)
{
    foreach (var error in result.Errors)
        Console.Error.WriteLine(error.Message);
    return;
}

// Configure execution
var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
var provider = new HttpTestRequestProvider(httpClient);

// Execute
var evaluator = new TestScriptEvaluator(provider, new InlineFixtureProvider(), schemaProvider);
var report = await evaluator.ExecuteAsync(result.Value!, CancellationToken.None);

Console.WriteLine($"Result: {report.OverallOutcome}"); // Pass, Fail, or Error
```

## Architecture

The engine follows a three-phase pattern:

1. **Parse** — JSON → `TestScriptDefinition` expression tree
2. **Evaluate** — Execute operations and assertions via `ITestRequestProvider`
3. **Report** — Produce FHIR `TestReport` resource or JUnit XML

## Related Packages

- `Ignixa.TestScript.FhirFakes` — Auto-generate test fixtures
- `Ignixa.TestScript.XUnit` — Discover and run TestScripts as xUnit theories

## FhirFakes fixtures

`Ignixa.TestScript.FhirFakes` resolves TestScript fixtures from a FHIR extension on
`TestScript.fixture.resource`.

The legacy shorthand remains supported:

```json
{
  "resource": {
    "extension": [
      {
        "url": "http://ignixa.io/testscript/fhirfakes",
        "valueCode": "Patient"
      }
    ]
  }
}
```

For configurable generation, use the complex extension form. The preferred canonical URL is
`http://ignixa.io/fhir/StructureDefinition/testscript-fhirfakes`; the legacy URL is also accepted.
Nested extension values are typed FHIR primitives rather than opaque JSON.

```json
{
  "resource": {
    "extension": [
      {
        "url": "http://ignixa.io/fhir/StructureDefinition/testscript-fhirfakes",
        "extension": [
          { "url": "resourceType", "valueCode": "Patient" },
          { "url": "seed", "valueInteger": 12345 },
          { "url": "density", "valueCode": "maximum" },
          { "url": "theme", "valueCode": "cardiology" },
          { "url": "profile", "valueCanonical": "http://example.org/fhir/StructureDefinition/test-patient" },
          { "url": "tag", "valueString": "crud-test-run" },
          {
            "url": "patient",
            "extension": [
              { "url": "givenName", "valueString": "Ada" },
              { "url": "familyName", "valueString": "Lovelace" },
              { "url": "gender", "valueCode": "female" },
              { "url": "birthDate", "valueDate": "1985-12-10" },
              { "url": "city", "valueString": "London" },
              { "url": "state", "valueString": "Greater London" },
              { "url": "zipCode", "valueString": "SW1A" },
              { "url": "active", "valueBoolean": false },
              { "url": "bmi", "valueDecimal": 21.5 },
              {
                "url": "identifier",
                "extension": [
                  { "url": "system", "valueUri": "http://example.org/mrn" },
                  { "url": "value", "valueString": "MRN-123" }
                ]
              }
            ]
          },
          {
            "url": "edgeCase",
            "extension": [
              { "url": "selector", "valueCode": "unicode" },
              { "url": "selector", "valueCode": "temporal" },
              { "url": "seed", "valueInteger": 67890 }
            ]
          }
        ]
      }
    ]
  }
}
```

Supported top-level child extensions:

| Child URL | Value | Notes |
| --- | --- | --- |
| `resourceType` | `valueCode` | Required in complex form. Overrides legacy top-level `valueCode` when both are present. |
| `seed` | `valueInteger` | Reproducible generation seed. |
| `density` | `valueCode` | `minimal`, `realistic`, or `maximum`. |
| `theme` | `valueCode` or `valueCoding.code` | A `ClinicalDomain` value such as `cardiology`; hyphenated names are accepted. |
| `profile` | `valueCanonical` | Adds `meta.profile` to the generated resource. |
| `tag` | `valueString` | Adds the Ignixa test-isolation tag. |
| `patient` | nested extensions | Patient-specific options. Applied only for `resourceType = Patient`. |
| `edgeCase` | repeatable nested extensions | Selects edge-case families/categories from `EdgeCaseCatalog`. |

`patient` supports `givenName`, `familyName`, `gender`, `age`, `birthDate`, `city`, `state`,
`zipCode`, `active`, `bmi`, and repeatable `identifier` children with `system` and `value`.
