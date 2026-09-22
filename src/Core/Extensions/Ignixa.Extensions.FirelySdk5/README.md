# Ignixa.Extensions.FirelySdk5

Firely SDK 5.x interoperability shims for bidirectional conversion between Ignixa types and Firely SDK types.

## Overview

This package provides **legacy support** for users still on **Firely SDK 5.x** (5.11.4). It contains the same adapters as `Ignixa.Extensions.FirelySdk6`, but targets Firely SDK 5.x package versions.

**If you're using Firely SDK 6.0+**, use [`Ignixa.Extensions.FirelySdk6`](../Ignixa.Extensions.FirelySdk6/) instead.

## Version Compatibility

| Package | Firely SDK Version | Target Framework |
|---------|-------------------|------------------|
| `Ignixa.Extensions.FirelySdk5` | 5.11.4 | net9.0 |
| `Ignixa.Extensions.FirelySdk6` | 6.0.0 | net9.0 |

## Why Two Packages?

Firely SDK 6.0 introduced breaking changes in internal architecture, but the **core interfaces** (`ITypedElement`, `ISourceNode`, `IElementDefinitionSummary`) remained **stable**. This allows us to:

- ✅ **Share 100% of source code** between SDK 5.x and 6.x versions (via file linking)
- ✅ **Zero code duplication** - fixes apply to both versions automatically
- ✅ **Drop-in replacement** - same namespace (`Ignixa.Extensions.FirelySdk`)

## SDK Version and FHIRPath Seam

This package ships against **5.11.4**, the lowest supported 5.x version and the same version the
Microsoft FHIR server runs. Later 5.x releases carry a known bug, and a higher dependency floor would
force that consumer's Firely SDK up past the version its own characterization tests pin.

This is also the **5.11.4** FHIRPath seam pinned by ADR 2608: the parity tests and benchmark
link-compile the adapters at 5.11.4 to compare the engine that the migration replaces. Package floor
and seam-parity measurement now agree, so the adapter is measured on the engine it ships against. Do
not raise this floor without re-checking the consumer that the seam exists to serve.

## Installation

```bash
dotnet add package Ignixa.Extensions.FirelySdk5
```

## Usage

All usage examples from `Ignixa.Extensions.FirelySdk6` apply identically:

### Ignixa → Firely SDK

```csharp
using Ignixa.Extensions.FirelySdk;

// Convert Ignixa IElement to Firely ITypedElement
IElement ignixaElement = ...;
ITypedElement firelyElement = ignixaElement.ToTypedElement();

// Use with Firely SDK tools (FhirPath, Validator, etc.)
var navigator = firelyElement.ToFhirPathNavigator();
var result = navigator.Scalar("Patient.name.family");
```

### Firely SDK → Ignixa

```csharp
using Ignixa.Extensions.FirelySdk;

// Convert Firely ITypedElement to Ignixa IElement
ITypedElement firelyElement = ...;
IElement ignixaElement = firelyElement.ToIgnixaElement();

// Convert Firely ISourceNode to Ignixa IElement (schema-aware)
ISourceNode sourceNode = FhirJsonNode.Parse(json);
IElement element = sourceNode.ToElement(schema);
```

## Breaking Changes from SDK 5.x → 6.x

The Firely SDK 6.0 release introduced these breaking changes:

- **Framework support**: Minimum target changed from `netstandard2.0` to `netstandard2.1`
- **Internal architecture**: POCO parsers refactored, FhirPath engine moved to `IScopedNode`
- **Removed interfaces**: `IDeepCopyable`, `IBaseElementNavigator`

**None of these affect the adapter code**, which only uses stable interfaces.

## Migration Path

When you're ready to upgrade to Firely SDK 6.x:

1. Update your project's Firely SDK package references to 6.0.0+
2. Replace `Ignixa.Extensions.FirelySdk5` with `Ignixa.Extensions.FirelySdk6`
3. No code changes required (same namespace and API)

```diff
- <PackageReference Include="Hl7.Fhir.Base" Version="5.11.4" />
+ <PackageReference Include="Hl7.Fhir.Base" Version="6.0.0" />

- <PackageReference Include="Ignixa.Extensions.FirelySdk5" Version="x.y.z" />
+ <PackageReference Include="Ignixa.Extensions.FirelySdk6" Version="x.y.z" />
```

## License

MIT License - See [LICENSE](../../../../LICENSE) for details.
