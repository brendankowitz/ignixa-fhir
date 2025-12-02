# Ignixa Core Libraries

[![NuGet](https://img.shields.io/badge/NuGet-Ignixa-004880?logo=nuget)](https://www.nuget.org/packages?q=Ignixa)

This folder contains the **Core** libraries that are published as NuGet packages. These packages are designed to be reusable across different FHIR implementations and can be used independently of the Ignixa FHIR Server.

## Available Packages

### Foundation

| Package | Description | Documentation |
|---------|-------------|---------------|
| **Ignixa.Abstractions** | Core abstractions and interfaces for FHIR data structures (`IElement`, `ISourceNode`, `IType`) | [README](Ignixa.Abstractions/README.md) |
| **Ignixa.Specification** | FHIR structure definitions and auto-generated providers for R4/R4B/R5/R6/STU3 | [README](Ignixa.Specification/README.md) |

### Data Processing

| Package | Description | Documentation |
|---------|-------------|---------------|
| **Ignixa.Serialization** | High-performance JSON serialization with `JsonNode` and `ISourceNavigator` support | [README](Ignixa.Serialization/README.md) |
| **Ignixa.Search** | Search parameter definitions, indexing, and search value extraction | [README](Ignixa.Search/README.md) |
| **Ignixa.Validation** | Three-tier validation system (Fast/Spec/Profile) with OperationOutcome support | [README](Ignixa.Validation/README.md) |

### Testing & Development

| Package | Description | Documentation |
|---------|-------------|---------------|
| **Ignixa.FhirFakes** | 4-layer FHIR test data generation library with realistic patient populations, demographics, and clinical scenarios | [README](Ignixa.FhirFakes/README.md) |

### Advanced Features

| Package | Description | Documentation |
|---------|-------------|---------------|
| **Ignixa.FhirPath** | FHIRPath expression parser and evaluator with compiled delegate optimization | [README](Ignixa.FhirPath/README.md) |
| **Ignixa.FhirMappingLanguage** | FHIR Mapping Language (FML) parser and StructureMap support | [README](Ignixa.FhirMappingLanguage/README.md) |
| **Ignixa.SqlOnFhir** | SQL on FHIR v2 implementation for tabular queries | [README](Ignixa.SqlOnFhir/README.md) |
| **Ignixa.PackageManagement** | FHIR package manager for loading and caching packages | [README](Ignixa.PackageManagement/README.md) |

### Extensions

| Package | Description | Documentation |
|---------|-------------|---------------|
| **Ignixa.Extensions.FirelySdk6** | Integration extensions for Firely SDK 6.x | [README](Extensions/Ignixa.Extensions.FirelySdk6/README.md) |

## Installation

Install any package using the .NET CLI:

```bash
# Install individual packages
dotnet add package Ignixa.Abstractions
dotnet add package Ignixa.Serialization
dotnet add package Ignixa.FhirPath
dotnet add package Ignixa.Validation
dotnet add package Ignixa.FhirFakes
# ... etc
```

## Package Dependencies

The packages are designed with minimal dependencies:

```
Ignixa.Abstractions (foundation)
    ↓
Ignixa.Specification (FHIR metadata)
    ↓
Ignixa.Serialization (JSON/XML)
    ↓
Ignixa.FhirPath, Ignixa.Search, Ignixa.Validation (features)
```

## Publishing

These packages are automatically published to NuGet.org when changes are pushed to the `main` branch that affect the `/src/Core/**` folder. See [`.github/workflows/nuget-publish.yml`](../../.github/workflows/nuget-publish.yml) for CI/CD configuration.

## Versioning

All packages follow **Semantic Versioning** (SemVer) and use GitVersion for automatic versioning based on git history.
