# Ignixa.TestScript.FhirFakes

FhirFakes integration for the TestScript execution engine. Automatically generates FHIR fixtures using `SchemaBasedFhirResourceFaker`.

## Installation

```bash
dotnet add package Ignixa.TestScript.FhirFakes
```

## Usage

Register `FhirFakesFixtureProvider` in your fixture provider chain. It must come before `InlineFixtureProvider` — `CompositeFixtureProvider` stops at the first non-null result, and `InlineFixtureProvider` returns the skeleton `resource` object immediately without generating fake data:

```csharp
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;

var provider = new CompositeFixtureProvider([
    new FhirFakesFixtureProvider(),
    new InlineFixtureProvider()
]);
```

Activate via extension inside the `resource` object of the fixture definition:

```json
{
  "id": "generated-patient",
  "resource": {
    "resourceType": "Patient",
    "extension": [{
      "url": "http://ignixa.io/testscript/fhirfakes",
      "valueCode": "Patient"
    }]
  }
}
```
