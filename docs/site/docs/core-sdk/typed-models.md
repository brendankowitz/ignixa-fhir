---
sidebar_position: 3.5
title: Typed Models
description: Firely-grade strongly-typed POCO facades over the Ignixa JSON/IElement runtime -- zero-copy, opt-in, cross-version safe.
---

# Typed Models

`Ignixa.Models.R4` and `Ignixa.Models.R5` are opt-in packages that add strongly-typed POCO facades
(`patient.Name[0].Family`, compile-time safety, IntelliSense over every element, enums for every value
set) on top of the JSON/`IElement` runtime every other Core SDK package already uses. They are **views**,
not a second source of truth: every generated type reads and writes directly against the same
`JsonObject` `Ignixa.Serialization` parses, so extensions, primitive shadow elements (`_birthDate`), and
unknown elements all survive untouched, and there is no POCO&harr;JSON serialization step to keep in sync.

This is the ergonomic layer Firely SDK is best known for, without taking the Firely dependency
(see [Firely SDK Compatibility](/docs/core-sdk/firely-sdk-compatibility) for that comparison) and without
losing the schema-driven, multi-version request path the server itself runs on. The server runs with
neither package referenced; add them only where you want typed ergonomics for a specific version.

## Why Use This Package?

- **Zero-copy**: no allocation-heavy deserialize/reserialize cycle. A typed facade is a thin wrapper
  around the exact same `JsonObject` you already have.
- **Shared base across versions**: elements that are structurally identical across R4 and R5 (e.g.
  `Coding`, `Money`) are generated **once**, in `Ignixa.Serialization` under the `Ignixa.Models`
  namespace. `Ignixa.Models.R4`/`.R5` only add the R4/R5-specific delta on top. Where a value set gains
  or loses codes between versions under the same URL (a common, easy-to-miss source of silent data
  loss), the generator detects it and correctly splits that element per-version instead of sharing it.
- **Cross-version, safely**: a single parsed node can be viewed as `Ignixa.Models.R4.Patient` and
  `Ignixa.Models.R5.Patient` simultaneously -- both are windows onto the same data. `As<T>()` guards
  against the opposite mistake: reinterpreting a version-tagged node through a facade that doesn't
  support that version throws, rather than silently misreading the wrong shape.
- **Generated, not hand-maintained**: facades are produced by the same specification-driven generator
  pipeline (`codegen/Ignixa.Specification.Generators`) that already produces the server's schema
  providers, so typed-model coverage tracks the FHIR spec directly instead of drifting from hand-written
  code.

## Installation

```bash
# Pick the version(s) you need -- both can be referenced from the same project.
dotnet add package Ignixa.Models.R4 --prerelease
dotnet add package Ignixa.Models.R5 --prerelease
```

Both packages are **Beta** stability (see [Package Stability](/docs/core-sdk/stability)) and require the
`--prerelease` flag.

## Quick Start

```csharp
using Ignixa.Abstractions;
using Ignixa.Models.R4;
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

// Parse once, view as the R4-typed facade -- zero-copy over the same backing JsonObject.
Patient patient = ResourceJsonNode.Parse(json).As<Patient>();

Console.WriteLine(patient.Name[0].Family);   // "Chalmers"
Console.WriteLine(patient.Gender);            // AdministrativeGender.Female

// Mutate through the typed facade; writes land directly on the backing JSON.
patient.Active = true;

string serialized = patient.SerializeToString();
```

### Viewing the same node across versions

Because facades are views, not owners, one parsed node can be viewed through more than one version's
lens at once:

```csharp
var resource = ResourceJsonNode.Parse(json);

Ignixa.Models.R4.Patient asR4 = resource.As<Ignixa.Models.R4.Patient>();
Ignixa.Models.R5.Patient asR5 = resource.As<Ignixa.Models.R5.Patient>();

asR4.Active = true;
Console.WriteLine(asR5.Active); // true -- same backing JsonObject, write visible through either view
```

Code written against the **shared base type** (`Ignixa.Models.Patient`) works against either version's
facade -- true cross-version code, not just cross-version data:

```csharp
using Ignixa.Models;

static string? Describe(Patient p) => p.Name.FirstOrDefault()?.Family;

Describe(asR4); // works
Describe(asR5); // also works -- same shared base type
```

### Explicit version dispatch

```csharp
// Throws InvalidOperationException on a registry miss rather than silently returning a wrong-typed facade.
ResourceJsonNode viaVersion = resource.AsVersion(FhirVersion.R4);

// Best-effort variant: returns false and leaves the node unmodified on a miss.
if (resource.TryAsVersion(FhirVersion.R4, out var versioned))
{
    // ...
}
```

### Cross-version safety guard

`As<T>()` validates a version-tagged node against the target facade's supported version set (via a
generated `CompatibleFhirVersionsAttribute` on every facade) and throws if they don't match -- e.g.
reinterpreting R4 data through an R5-only accessor:

```csharp
var r4Patient = ResourceJsonNode.Parse(json).As<Ignixa.Models.R4.Patient>(); // FhirVersion stamped R4

r4Patient.As<Ignixa.Models.R5.Patient>();
// throws InvalidCastException: "Cannot convert a R4 resource to Patient, which only supports [R5].
// Use TryAsVersion/AsVersion for safe dispatch, or As<T>(validate: false) to bypass this check."
```

The guard is permissive for untagged nodes (`FhirVersion == null`) so it never breaks code that doesn't
track version explicitly, and `As<T>(validate: false)` remains available as an expert escape hatch.

## Architecture

- **Shared base layer**: identical types live once in `Ignixa.Serialization`, namespace `Ignixa.Models`
  (e.g. `Ignixa.Models.Patient`, `Ignixa.Models.Coding`).
- **Per-version packages**: `Ignixa.Models.R4`/`.R5` subclass the shared base with the elements
  that genuinely diverge for that version (`Ignixa.Models.R4.Patient : Ignixa.Models.Patient`).
  Resources always get a per-version subclass even when nothing diverges, because the subclass is
  what carries the `[CompatibleFhirVersions]` tag that guards cross-version `As<T>()` casts — a
  `global using` alias cannot carry an attribute. Such a subclass may have no members of its own.
- **Classification, not guesswork**: a build-time classifier walks every targeted version's
  `StructureDefinition`s and compares element-level signatures -- type, cardinality, and (for
  code-bound elements) the *actual expanded code set*, not just the value-set URL -- to decide what's
  safe to share versus what must be per-version.
- **Generated, checked in**: output is committed source, not build-time codegen in your project. A
  regen-drift guard (`build/check-typed-model-regen.ps1`/`.sh`) proves the checked-in output still
  matches what the generator produces.

## Coverage Notes

Not every element gets a typed accessor. Two intentional fallbacks, both documented on the generated
member itself:

- **`Reference`-typed elements** (e.g. `Observation.subject`) are exposed as a raw `JsonNode?`/`JsonArray?`
  rather than a typed `Reference` property, since `Reference` targets are polymorphic across resource
  types.
- **Value sets that can't be expanded** (unbounded code systems, expansion failures) fall back to a plain
  `string` property instead of an enum.

Both fallbacks still round-trip byte-for-byte -- they just don't get compile-time member names.

## Integration with Other Packages

- **Ignixa.Serialization**: hosts the shared base layer and the `ResourceJsonNode`/`BaseJsonNode` runtime
  every facade builds on.
- **Ignixa.Abstractions**: supplies `FhirVersion` and other cross-cutting contracts.
- **Ignixa.Specification**: the same `StructureDefinition` data that drives the server's schema providers
  drives the typed-model generator, so coverage stays aligned with the rest of the SDK.

## FHIR Version Support

| Package | STU3 | R4 | R4B | R5 | R6 |
|---------|:----:|:--:|:---:|:--:|:--:|
| Ignixa.Models.R4 | | ✅ | | | |
| Ignixa.Models.R5 | | | | ✅ | |

Unlike the version-agnostic core packages (Abstractions, Specification, Serialization, FhirPath,
Validation, Search), typed-model packages are version-specific by design -- that's what makes the typed
surface possible. See [ADR-2609](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/features/typed-models/adr-2609-stu3-classification-group.md)
for the proposed approach to adding STU3.

## Related Documentation

- [Serialization](/docs/core-sdk/serialization) -- the runtime typed models are built on
- [Package Stability](/docs/core-sdk/stability)
- [Firely SDK Compatibility](/docs/core-sdk/firely-sdk-compatibility)
