---
sidebar_position: 9
title: FHIR Mapping Language
description: Parse and execute FHIR Mapping Language (FML) maps
---

# FHIR Mapping Language

The `Ignixa.FhirMappingLanguage` package provides a native .NET implementation of the FHIR Mapping Language (FML) for transforming FHIR resources.

## Installation

```bash
dotnet add package Ignixa.FhirMappingLanguage
```

## Quick Start

```csharp
using Ignixa.FhirMappingLanguage;

// Parse a StructureMap
var parser = new FmlParser();
var structureMap = parser.Parse(fmlSource);

// Execute the transformation
var engine = new FmlEngine();
var result = await engine.TransformAsync(sourceResource, structureMap);
```

## Features

- **FML Parser**: Parse FHIR Mapping Language source into StructureMap resources
- **Transform Engine**: Execute StructureMap transformations
- **FHIRPath Integration**: Full FHIRPath support in mapping expressions
- **Bidirectional Maps**: Support for reversible transformations

## Example FML

```fml
map "http://example.org/PatientToContact" = "PatientToContact"

uses "http://hl7.org/fhir/StructureDefinition/Patient" as source
uses "http://hl7.org/fhir/StructureDefinition/ContactPoint" as target

group PatientToContact(source src : Patient, target tgt : ContactPoint) {
  src.telecom first as phone -> tgt.value = phone.value;
  src.telecom first as phone -> tgt.system = phone.system;
}
```

## Related Documentation

- [FHIR Mapping Language Specification](https://hl7.org/fhir/mapping-language.html)
