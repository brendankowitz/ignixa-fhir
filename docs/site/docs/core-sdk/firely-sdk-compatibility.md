---
sidebar_position: 12
title: Firely SDK Compatibility
description: Interoperability with Firely SDK 5.x and 6.x
---

# Firely SDK Compatibility

Ignixa provides bidirectional interoperability with the Firely SDK ecosystem through dedicated extension packages.

## Installation

```bash
# For Firely SDK 6.x (Hl7.Fhir.* 6.0.0+)
dotnet add package Ignixa.Extensions.FirelySdk6

# For Firely SDK 5.x (Hl7.Fhir.* 5.0.0+)
dotnet add package Ignixa.Extensions.FirelySdk5
```

## Quick Start

```csharp
using Ignixa.Extensions.FirelySdk;
using Hl7.Fhir.ElementModel;

// Firely → Ignixa
ITypedElement firelyElement = ...;
IElement ignixaElement = firelyElement.ToIgnixaElement();

// Ignixa → Firely
IElement ignixaElement = ...;
ITypedElement firelyElement = ignixaElement.ToTypedElement();
```

## Extension Methods

### Ignixa → Firely

| Method | Description |
|--------|-------------|
| `IElement.ToTypedElement()` | Convert single element to Firely ITypedElement |
| `IEnumerable<IElement>.ToTypedElements()` | Batch conversion of elements |
| `IReadOnlyList<IElement>.ToTypedElements()` | Convert read-only list |
| `ISourceNode.ToSourceNavigator()` | Adapt Firely ISourceNode to Ignixa ISourceNavigator |
| `ISourceNode.ToElement(schema)` | Convert to schema-aware IElement |

### Firely → Ignixa

| Method | Description |
|--------|-------------|
| `ITypedElement.ToIgnixaElement()` | Convert single element to Ignixa IElement |
| `IEnumerable<ITypedElement>.ToIgnixaElements()` | Batch conversion of elements |

## Usage Patterns

### Parse with Firely, Process with Ignixa

```csharp
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Hl7.Fhir.Serialization;

// Parse JSON with Firely SDK
ISourceNode sourceNode = FhirJsonNode.Parse(jsonString);

// Convert to Ignixa (schema-aware)
IElement element = sourceNode.ToElement(schema);

// Use Ignixa FHIRPath for high-performance evaluation
var names = element.Select("name.given");
```

### Use Ignixa Data with Firely Tools

```csharp
using Ignixa.Extensions.FirelySdk;
using Hl7.FhirPath;

// Get Ignixa element
IElement ignixaElement = mySource.GetPatient("123");

// Convert to Firely
ITypedElement firelyElement = ignixaElement.ToTypedElement();

// Use Firely's FHIRPath engine
var compiler = new FhirPathCompiler();
var result = compiler.Compile("Patient.name.family")
    .Invoke(firelyElement, EvaluationContext.CreateDefault());
```

### Round-Trip Preservation

Smart unwrapping prevents double-wrapping and preserves the original object:

```csharp
ITypedElement original = ...;
IElement ignixa = original.ToIgnixaElement();
ITypedElement result = ignixa.ToTypedElement();

// result is the ORIGINAL object, not a wrapper
Assert.Same(original, result);
```

## Performance

| Operation | Behavior |
|-----------|----------|
| Child access | Lazy materialization on first access |
| Repeated filters | Cached by name |
| Streaming conversion | Memory-efficient for large trees |
| Round-trip | Returns original, no adapter chains |

## SDK Version Support

| Package | Firely SDK Version | Hl7.Fhir.* Version |
|---------|-------------------|-------------------|
| `Ignixa.Extensions.FirelySdk6` | 6.x | 6.0.0+ |
| `Ignixa.Extensions.FirelySdk5` | 5.x | 5.0.0+ |

Both packages provide:
- Full bidirectional conversion support
- Zero-copy round-trip preservation
- Lazy child materialization for memory efficiency

Use SDK 6.x for new projects. SDK 5.x is available for compatibility with existing codebases.

## Related Documentation

- [Abstractions](/docs/core-sdk/abstractions)
- [FHIRPath](/docs/core-sdk/fhirpath)
