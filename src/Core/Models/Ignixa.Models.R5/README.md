# Ignixa.Models.R5

FHIR R5 strongly-typed POCO facades over the Ignixa element/JSON runtime. Opt-in: subclasses the shared
`Ignixa.Models` base layer in `Ignixa.Serialization`, so it adds Firely-grade ergonomics
(`patient.Name[0].Family`, compile-time safety, IntelliSense over every element, enums for every value
set) without becoming a second source of truth — every generated type is a zero-copy view over the same
`JsonObject`/`IElement` runtime the rest of the SDK already uses.

## Why Use This Package?

- **Zero-copy**: typed facades read and write directly against the backing JSON; there is no
  POCO&harr;JSON serialization step, so extensions, primitive shadow elements (`_birthDate`), and
  unknown elements all survive untouched.
- **Shared base, R5-specific deltas**: types identical across versions (e.g. `Coding`) live once in
  `Ignixa.Serialization`; R5-specific shape lives here. You get the base type's ergonomics for free and
  only pull in this package for the R5-specific delta.
- **Cross-version safe by construction**: `As<T>()` validates that a version-tagged node is only
  reinterpreted through a facade that actually supports that version, so R5 data can't silently be
  misread through another version's shaped accessor.
- **Opt-in, never on the core request path**: the server runs without this package. Add it only where
  you want typed R5 ergonomics.

## Installation

```bash
dotnet add package Ignixa.Models.R5 --prerelease
```

## Quick Start

```csharp
using Ignixa.Abstractions;
using Ignixa.Models.R5;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

const string json = """
{
  "resourceType": "Patient",
  "id": "example",
  "gender": "female",
  "birthDate": "1974-12-25",
  "name": [ { "family": "Chalmers", "given": [ "Jean" ] } ]
}
""";

// Parse once, view as the R5-typed facade -- zero-copy over the same backing JsonObject.
Patient patient = ResourceJsonNode.Parse(json).As<Patient>();

Console.WriteLine(patient.Name[0].Family);       // "Chalmers"
Console.WriteLine(patient.Gender);                // AdministrativeGender.Female

// Mutate through the typed facade; writes land directly on the backing JSON.
patient.Active = true;

string serialized = patient.SerializeToString();
```

### Cross-version dispatch

```csharp
using Ignixa.Abstractions;

// Explicit, version-aware dispatch -- throws InvalidOperationException on a registry miss rather than
// silently returning a wrong-typed facade.
ResourceJsonNode viaR5 = resource.AsVersion(FhirVersion.R5);

// Or the best-effort variant:
if (resource.TryAsVersion(FhirVersion.R5, out var versioned))
{
    // ...
}
```

## Integration with Other Packages

- **Ignixa.Serialization**: provides the shared `Ignixa.Models` base layer this package subclasses, plus
  the `ResourceJsonNode`/`BaseJsonNode` runtime every facade is built on.
- **Ignixa.Models.R4**: the R4 counterpart. Both can be referenced from the same project — a single
  parsed node can be viewed as `Ignixa.Models.R4.Patient` and `Ignixa.Models.R5.Patient`
  simultaneously, since neither owns the underlying data.
- **Ignixa.Abstractions**: supplies `FhirVersion` and other cross-cutting contracts.

## License

MIT License - see LICENSE file in repository root
