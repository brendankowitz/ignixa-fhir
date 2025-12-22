---
sidebar_position: 1
title: Overview
description: Ignixa Core SDK - Reusable FHIR libraries for .NET
---

# Core SDK Overview

The Ignixa Core SDK is a collection of high-performance, reusable .NET libraries for building FHIR applications. These packages can be used independently of the Ignixa FHIR Server.

## Package Overview

```
Ignixa.Abstractions (Foundation)
        ↓
Ignixa.Specification (FHIR Metadata)
        ↓
Ignixa.Serialization (JSON)
        ↓
┌───────┬────────┬──────────┬────────────────────┐
│       │        │          │                    │
↓       ↓        ↓          ↓                    ↓
FhirPath  Search  Validation  NarrativeGenerator  ...
```

## Available Packages

### Foundation

| Package | Description | NuGet |
|---------|-------------|-------|
| **Ignixa.Abstractions** | Core interfaces (`IElement`, `ISourceNode`, `IType`) | [![NuGet](https://img.shields.io/nuget/v/Ignixa.Abstractions)](https://www.nuget.org/packages/Ignixa.Abstractions) |
| **Ignixa.Specification** | FHIR structure definitions for R4/R4B/R5/R6/STU3 | [![NuGet](https://img.shields.io/nuget/v/Ignixa.Specification)](https://www.nuget.org/packages/Ignixa.Specification) |

### Data Processing

| Package | Description | NuGet |
|---------|-------------|-------|
| **Ignixa.Serialization** | High-performance JSON serialization | [![NuGet](https://img.shields.io/nuget/v/Ignixa.Serialization)](https://www.nuget.org/packages/Ignixa.Serialization) |
| **Ignixa.Search** | Search parameter definitions and indexing | [![NuGet](https://img.shields.io/nuget/v/Ignixa.Search)](https://www.nuget.org/packages/Ignixa.Search) |
| **Ignixa.Validation** | Three-tier validation engine | [![NuGet](https://img.shields.io/nuget/v/Ignixa.Validation)](https://www.nuget.org/packages/Ignixa.Validation) |

### Advanced Features

| Package | Description | NuGet |
|---------|-------------|-------|
| **Ignixa.FhirPath** | Compiled FHIRPath expression engine | [![NuGet](https://img.shields.io/nuget/v/Ignixa.FhirPath)](https://www.nuget.org/packages/Ignixa.FhirPath) |
| **Ignixa.FhirMappingLanguage** | FHIR Mapping Language parser | [![NuGet](https://img.shields.io/nuget/v/Ignixa.FhirMappingLanguage)](https://www.nuget.org/packages/Ignixa.FhirMappingLanguage) |
| **Ignixa.NarrativeGenerator** | FHIR narrative generation | [![NuGet](https://img.shields.io/nuget/v/Ignixa.NarrativeGenerator)](https://www.nuget.org/packages/Ignixa.NarrativeGenerator) |
| **Ignixa.SqlOnFhir** | SQL on FHIR v2 implementation | [![NuGet](https://img.shields.io/nuget/v/Ignixa.SqlOnFhir)](https://www.nuget.org/packages/Ignixa.SqlOnFhir) |
| **Ignixa.PackageManagement** | FHIR package management | [![NuGet](https://img.shields.io/nuget/v/Ignixa.PackageManagement)](https://www.nuget.org/packages/Ignixa.PackageManagement) |

### Testing & Development

| Package | Description | NuGet |
|---------|-------------|-------|
| **Ignixa.FhirFakes** | Synthetic FHIR data generator | [![NuGet](https://img.shields.io/nuget/v/Ignixa.FhirFakes)](https://www.nuget.org/packages/Ignixa.FhirFakes) |

### Extensions

| Package | Description | NuGet |
|---------|-------------|-------|
| **Ignixa.Extensions.FirelySdk5** | Firely SDK 5.x integration | [![NuGet](https://img.shields.io/nuget/v/Ignixa.Extensions.FirelySdk5)](https://www.nuget.org/packages/Ignixa.Extensions.FirelySdk5) |
| **Ignixa.Extensions.FirelySdk6** | Firely SDK 6.x integration | [![NuGet](https://img.shields.io/nuget/v/Ignixa.Extensions.FirelySdk6)](https://www.nuget.org/packages/Ignixa.Extensions.FirelySdk6) |

## Quick Start

Install the packages you need:

```bash
# Basic FHIR processing
dotnet add package Ignixa.Abstractions
dotnet add package Ignixa.Serialization

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
using Ignixa.Abstractions;

var json = """
{
  "resourceType": "Patient",
  "id": "123",
  "name": [{ "family": "Smith", "given": ["John"] }]
}
""";

// Parse to ISourceNode
var sourceNode = JsonSourceNavigator.Parse(json);

// Navigate the structure
var resourceType = sourceNode.Name; // "Patient"
var id = sourceNode["id"].Text; // "123"
var familyName = sourceNode["name"][0]["family"].Text; // "Smith"
```

### Evaluate FHIRPath

```csharp
using Ignixa.FhirPath.Evaluation;

var element = sourceNode.ToElement(schema);

// Simple path
var names = element.Select("name.given");

// Complex expression
var activePatients = element.IsTrue("active = true");

// Scalar value
var birthDate = element.Scalar("birthDate")?.ToString();
```

### Validate Resources

```csharp
using Ignixa.Validation;

var validator = new FhirValidator(ValidationLevel.Spec);
var outcome = await validator.ValidateAsync(sourceNode);

if (!outcome.Success)
{
    foreach (var issue in outcome.Issues)
    {
        Console.WriteLine($"{issue.Severity}: {issue.Diagnostics}");
    }
}
```

### Generate Test Data

```csharp
using Ignixa.FhirFakes;

var faker = new FhirFaker();

// Generate a single patient
var patient = faker.Generate<Patient>();

// Generate a population
var population = faker.GeneratePopulation(count: 1000);
```

## Key Design Principles

### 1. ISourceNode Abstraction

All packages work with `ISourceNode`, a lightweight abstraction over FHIR data:

```csharp
public interface ISourceNode
{
    string Name { get; }
    string? Text { get; }
    IEnumerable<ISourceNode> Children(string? name = null);
}
```

### 2. Zero-Copy Serialization

The serialization layer minimizes allocations:

```csharp
// Streaming serialization
await using var writer = new FhirJsonWriter(stream);
await writer.WriteAsync(sourceNode);
```

### 3. Compiled Expressions

FHIRPath expressions are compiled and cached:

```csharp
// First call: parse + compile
var result = element.Select("name.given.first()");

// Subsequent calls: cached delegate
var result2 = element.Select("name.given.first()");
```

## FHIR Version Support

| Package | R4 | R4B | R5 | R6 | STU3 |
|---------|:--:|:---:|:--:|:--:|:----:|
| Abstractions | ✅ | ✅ | ✅ | ✅ | ✅ |
| Specification | ✅ | ✅ | ✅ | ✅ | ✅ |
| Serialization | ✅ | ✅ | ✅ | ✅ | ✅ |
| FhirPath | ✅ | ✅ | ✅ | ✅ | ✅ |
| Validation | ✅ | ✅ | ✅ | 🚧 | ✅ |
| Search | ✅ | ✅ | ✅ | 🚧 | ✅ |

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
