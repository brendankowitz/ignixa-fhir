---
sidebar_position: 11
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
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using Ignixa.Serialization;
using Ignixa.Specification;

// Get a schema provider
var schemaProvider = FhirVersion.R4.GetSchemaProvider();

// Load a ViewDefinition from JSON
var viewDefJson = File.ReadAllText("patient-view.json");
var viewDefNode = JsonSourceNodeFactory.Parse(viewDefJson);
var viewDefNavigator = viewDefNode.ToSourceNavigator();

// Parse the ViewDefinition into an expression tree
var viewDefExpression = ViewDefinitionExpressionParser.Parse(viewDefNavigator);

// Create an evaluator
var evaluator = new SqlOnFhirEvaluator();

// Load and evaluate a FHIR resource
var resourceJson = File.ReadAllText("patient.json");
var resourceNode = JsonSourceNodeFactory.Parse(resourceJson);
var resourceElement = resourceNode.ToElement(schemaProvider);

// Execute against the FHIR resource
var rows = evaluator.Evaluate(viewDefNavigator, resourceElement);

// Process results
foreach (var row in rows)
{
    foreach (var (columnName, value) in row)
    {
        Console.WriteLine($"{columnName}: {value}");
    }
}
```

## CLI Tool

The `ignixa-sqlonfhir` tool converts FHIR NDJSON to Parquet or CSV using ViewDefinitions.

### Installation

```bash
# Install as a global .NET tool
dotnet tool install --global Ignixa.SqlOnFhir.Cli
```

### Usage

```bash
# Convert FHIR NDJSON to Parquet using R4
ignixa-sqlonfhir r4 convert \
  --viewdefinition patient-view.json \
  --input patients.ndjson \
  --out patients.parquet

# Convert to CSV
ignixa-sqlonfhir r4 convert \
  --viewdefinition patient-view.json \
  --input patients.ndjson \
  --out patients.csv

# Preview schema without processing
ignixa-sqlonfhir r4 preview \
  --viewdefinition patient-view.json

# Validate a ViewDefinition
ignixa-sqlonfhir r4 validate \
  --viewdefinition patient-view.json
```

### Supported FHIR Versions

- `stu3` - FHIR STU3
- `r4` - FHIR R4
- `r4b` - FHIR R4B
- `r5` - FHIR R5
- `r6` - FHIR R6

## Features

- **ViewDefinition Support**: Full SQL on FHIR v2 ViewDefinition support with compiled FHIRPath expressions
- **Multiple FHIR Versions**: Supports STU3, R4, R4B, R5, and R6
- **Multiple Output Formats**: Export to Parquet or CSV via the CLI tool
- **FHIRPath Columns**: Define columns using FHIRPath expressions with automatic caching
- **Streaming**: CLI tool processes large NDJSON datasets with minimal memory
- **Schema Extraction**: Automatically extract column schemas from ViewDefinitions

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
