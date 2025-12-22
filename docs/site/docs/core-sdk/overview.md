---
sidebar_position: 1
title: Overview
description: Ignixa Core SDK - Reusable FHIR libraries for .NET
---

# Core SDK Overview

The Ignixa Core SDK is a collection of high-performance, reusable .NET libraries for building FHIR applications. These packages can be used independently of the Ignixa FHIR Server.

## Package Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Feature Packages                               │
├─────────────────────────────────────────────────────────────────────────┤
│  Search     Validation    FhirFakes    NarrativeGenerator   SqlOnFhir   │
│  FhirMappingLanguage      PackageManagement        Extensions.FirelySdk6│
└──────┬─────────┬──────────────┬─────────────────────────────────────────┘
       │         │              │
       ▼         ▼              ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                         Core Packages                                    │
├──────────────┬──────────────────────────┬───────────────────────────────┤
│ Specification│       Serialization      │          FhirPath             │
│  (metadata)  │         (JSON)           │        (expressions)          │
└──────┬───────┴────────────┬─────────────┴──────────────┬────────────────┘
       │                    │                            │
       └────────────────────┼────────────────────────────┘
                            ▼
              ┌────────────────────────────┐
              │    Ignixa.Abstractions     │
              │  (IElement, ISourceNode)   │
              └────────────────────────────┘
```

**Dependency Relationships** (from .csproj files):

| Package | Depends On |
|---------|------------|
| **Abstractions** | *(foundation - no internal deps)* |
| **Serialization** | Abstractions |
| **FhirPath** | Abstractions |
| **Specification** | Serialization, Abstractions |
| **Search** | FhirPath, Specification, Serialization |
| **Validation** | FhirPath, Specification, Serialization |
| **FhirFakes** | Specification, Serialization |
| **NarrativeGenerator** | Abstractions, FhirPath, Serialization |
| **SqlOnFhir** | Abstractions, FhirPath, Serialization |
| **FhirMappingLanguage** | Abstractions, FhirPath, Serialization |
| **PackageManagement** | Abstractions |
| **Extensions.FirelySdk6** | Abstractions, Serialization |

## Available Packages

### Foundation

| Package | Description |
|---------|-------------|
| **Ignixa.Abstractions** | Core interfaces (`IElement`, `ISourceNavigator`, `IType`) |
| **Ignixa.Specification** | FHIR structure definitions for STU3/R4/R4B/R5/R6 |

### Data Processing

| Package | Description |
|---------|-------------|
| **Ignixa.Serialization** | High-performance JSON serialization with typed models |
| **Ignixa.Search** | Search parameter definitions and indexing |
| **Ignixa.Validation** | Schema-based validation engine |

### Advanced Features

| Package | Description |
|---------|-------------|
| **Ignixa.FhirPath** | Compiled FHIRPath expression engine |
| **Ignixa.FhirMappingLanguage** | FHIR Mapping Language (FML) evaluator |
| **Ignixa.NarrativeGenerator** | FHIR narrative generation (HTML, Markdown, Compact) |
| **Ignixa.SqlOnFhir** | SQL on FHIR v2 ViewDefinition evaluator |
| **Ignixa.PackageManagement** | FHIR package loading from NPM registry |

### Testing & Development

| Package | Description |
|---------|-------------|
| **Ignixa.FhirFakes** | Synthetic FHIR data generator with clinical scenarios |

### Interoperability

| Package | Description |
|---------|-------------|
| **Ignixa.Extensions.FirelySdk6** | Bidirectional Firely SDK 6.x integration |

## Quick Start

```bash
# Core packages
dotnet add package Ignixa.Abstractions
dotnet add package Ignixa.Serialization
dotnet add package Ignixa.Specification

# FHIRPath evaluation
dotnet add package Ignixa.FhirPath

# Validation
dotnet add package Ignixa.Validation

# Test data generation
dotnet add package Ignixa.FhirFakes
```

## Basic Usage

### Parse FHIR JSON

```csharp
using Ignixa.Serialization;
using Ignixa.Specification;

var json = """
{
  "resourceType": "Patient",
  "id": "123",
  "name": [{ "family": "Smith", "given": ["John"] }]
}
""";

// Parse to ISourceNavigator
var sourceNode = JsonSourceNodeFactory.Parse(json);

// Navigate the structure
var resourceType = sourceNode.ResourceType; // "Patient"
var id = sourceNode["id"].Text;             // "123"
var familyName = sourceNode["name"][0]["family"].Text; // "Smith"
```

### Evaluate FHIRPath

```csharp
using Ignixa.FhirPath.Evaluation;
using Ignixa.Specification;

var schemaProvider = FhirVersion.R4.GetSchemaProvider();
var element = sourceNode.ToElement(schemaProvider);

// Simple path
var names = element.Select("name.given");

// Boolean check
var isActive = element.IsTrue("active = true");

// Scalar value
var birthDate = element.Scalar("birthDate")?.ToString();
```

### Validate Resources

```csharp
using Ignixa.Validation;
using Ignixa.Validation.Schema;

var schemaProvider = FhirVersion.R4.GetSchemaProvider();
var schemaResolver = new StructureDefinitionSchemaResolver(schemaProvider);
var cachedResolver = new CachedValidationSchemaResolver(schemaResolver);

// Get validation schema
var schema = cachedResolver.GetSchema("Patient");

// Validate
var element = sourceNode.ToElement(schemaProvider);
var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
var state = new ValidationState();
var result = schema.Validate(element, settings, state);

if (!result.IsValid)
{
    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"{issue.Severity}: {issue.Message}");
    }
}
```

### Generate Test Data

```csharp
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.Specification;

var schemaProvider = FhirVersion.R4.GetSchemaProvider();

// Use predefined clinical scenarios
var diabeticPatient = schemaProvider.GetDiabeticPatient(age: 52);
var bundle = diabeticPatient.ToBundle();

// Or build custom scenarios
var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(p => p.WithAge(35).WithGender(g => g.Female))
    .AddEncounter("Annual checkup")
    .AddComprehensiveMetabolicPanel()
    .Build();
```

## Key Design Principles

### 1. ISourceNavigator Abstraction

All packages work with `ISourceNavigator`, a lightweight abstraction over FHIR JSON:

```csharp
public interface ISourceNavigator
{
    string Name { get; }
    string Text { get; }
    string Location { get; }
    string ResourceType { get; }
    IEnumerable<ISourceNavigator> Children(string? name = null);
}
```

### 2. IElement for Typed Operations

Convert to `IElement` for FHIRPath evaluation and validation:

```csharp
var element = sourceNode.ToElement(schemaProvider);

// IElement has type information
var instanceType = element.InstanceType;  // "Patient"
var typeInfo = element.Type;              // Type metadata
```

### 3. Compiled Expressions

FHIRPath expressions are automatically parsed, compiled, and cached:

```csharp
// First call: parse + compile + cache
var result = element.Select("name.given.first()");

// Subsequent calls: uses cached compiled delegate
var result2 = element.Select("name.given.first()");
```

## FHIR Version Support

| Package | STU3 | R4 | R4B | R5 | R6 |
|---------|:----:|:--:|:---:|:--:|:--:|
| Abstractions | ✅ | ✅ | ✅ | ✅ | ✅ |
| Specification | ✅ | ✅ | ✅ | ✅ | ✅ |
| Serialization | ✅ | ✅ | ✅ | ✅ | ✅ |
| FhirPath | ✅ | ✅ | ✅ | ✅ | ✅ |
| Validation | ✅ | ✅ | ✅ | ✅ | ✅ |
| Search | ✅ | ✅ | ✅ | ✅ | ✅ |

## Related Documentation

- [Abstractions](/docs/core-sdk/abstractions)
- [Serialization](/docs/core-sdk/serialization)
- [FHIRPath](/docs/core-sdk/fhirpath)
- [Validation](/docs/core-sdk/validation)
- [Search](/docs/core-sdk/search)
- [FHIR Fakes](/docs/core-sdk/fhir-fakes)
- [Package Management](/docs/core-sdk/package-management)
- [Narrative Generator](/docs/core-sdk/narrative-generator)
- [FHIR Mapping Language](/docs/core-sdk/fhir-mapping-language)
- [SQL on FHIR](/docs/core-sdk/sql-on-fhir)
- [Firely SDK Compatibility](/docs/core-sdk/firely-sdk-compatibility)
