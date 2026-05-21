# Ignixa.TestScript.FhirFakes

FhirFakes integration for the TestScript execution engine. Automatically generates FHIR fixtures using `SchemaBasedFhirResourceFaker`.

## Installation

```bash
dotnet add package Ignixa.TestScript.FhirFakes
```

## Usage

Register `FhirFakesFixtureProvider` in your fixture provider chain:

```csharp
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;

var provider = new CompositeFixtureProvider([
    new InlineFixtureProvider(),
    new FhirFakesFixtureProvider()
]);
```

Activate via extension on TestScript fixture definitions:

```json
{
  "id": "generated-patient",
  "extension": [{
    "url": "http://ignixa.io/testscript/fhirfakes",
    "valueCode": "Patient"
  }]
}
```
