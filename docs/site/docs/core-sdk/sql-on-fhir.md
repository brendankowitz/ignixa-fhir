---
sidebar_position: 10
title: SQL on FHIR
description: Transform FHIR data to tabular formats using SQL on FHIR v2
---

# SQL on FHIR

The `Ignixa.SqlOnFhir` package implements the [SQL on FHIR v2 specification](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/) for projecting FHIR resources into tabular formats.

## Installation

```bash
dotnet add package Ignixa.SqlOnFhir
```

## Quick Start

```csharp
using Ignixa.SqlOnFhir;

// Load a ViewDefinition
var viewDefinition = ViewDefinition.Parse(viewJson);

// Create an executor
var executor = new ViewExecutor();

// Execute against FHIR resources
var rows = executor.Execute(viewDefinition, fhirResources);

// Export to Parquet
await executor.ExportToParquetAsync(viewDefinition, resources, "output.parquet");
```

## CLI Tool

A CLI tool is available for batch transformations:

```bash
dotnet tool install --global Ignixa.SqlOnFhir.Cli

# Transform FHIR NDJSON to Parquet
ignixa-sqlonfhir transform \
  --input patients.ndjson \
  --view patient-view.json \
  --output patients.parquet
```

## Features

- **ViewDefinition Support**: Full SQL on FHIR v2 ViewDefinition support
- **Multiple Outputs**: Export to Parquet, CSV, or in-memory tables
- **FHIRPath Columns**: Define columns using FHIRPath expressions
- **Streaming**: Process large datasets with minimal memory

## ViewDefinition Example

```json
{
  "resourceType": "ViewDefinition",
  "name": "patient_demographics",
  "resource": "Patient",
  "select": [
    { "column": [{ "name": "id", "path": "id" }] },
    { "column": [{ "name": "family_name", "path": "name.first().family" }] },
    { "column": [{ "name": "birth_date", "path": "birthDate" }] }
  ]
}
```

## Related Documentation

- [SQL on FHIR v2 Specification](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/)
