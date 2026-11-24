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

The `ignixa-sqlonfhir` tool converts FHIR NDJSON to Parquet, CSV, or NDJSON using SQL on FHIR ViewDefinitions. It supports single-file processing and batch directory processing.

### Installation

```bash
# Install as a global .NET tool
dotnet tool install --global Ignixa.SqlOnFhir.Cli
```

### Usage

```bash
# Convert one NDJSON file using R4
ignixa-sqlonfhir r4 run \
  --views patient-view.json \
  --input patients.ndjson \
  --out patients.parquet

# Convert to CSV or NDJSON by changing the output extension
ignixa-sqlonfhir r4 run --views patient-view.json --input patients.ndjson --out patients.csv
ignixa-sqlonfhir r4 run --views patient-view.json --input patients.ndjson --out patients.ndjson

# Batch process ViewDefinitions and resource NDJSON directories
ignixa-sqlonfhir r4 run \
  --views views \
  --input fhir-ndjson \
  --out output \
  --format parquet \
  --pattern "**/*.json" \
  --input-pattern "*{resource}*.ndjson" \
  --stats-out output/stats.json

# Preview schema only, or include sample rows with --input
ignixa-sqlonfhir r4 preview --views patient-view.json
ignixa-sqlonfhir r4 preview --views patient-view.json --input patients.ndjson --rows 10

# Validate one ViewDefinition or a directory
ignixa-sqlonfhir r4 validate --views patient-view.json
ignixa-sqlonfhir r4 validate --views views --pattern "**/*.json"
```

Runtime FHIRPath variables are available for `run` and `preview`:

```bash
ignixa-sqlonfhir r4 run \
  --views patient-view.json \
  --input patients.ndjson \
  --out patients.csv \
  --var cohortId=research-2026 \
  --var effectiveDate=2026-01-01
```

### Supported FHIR Versions

- `stu3` - FHIR STU3
- `r4` - FHIR R4
- `r4b` - FHIR R4B
- `r5` - FHIR R5
- `r6` - FHIR R6

## Features

- **ViewDefinition Support**: SQL on FHIR v2 / 2.1.0-pre ViewDefinition support with compiled FHIRPath expressions
- **Multiple FHIR Versions**: Supports STU3, R4, R4B, R5, and R6
- **Multiple Output Formats**: Export to Parquet, CSV, or NDJSON via the CLI tool
- **Batch CLI Processing**: Run, preview, or validate ViewDefinitions from directories with resource-aware NDJSON matching
- **Runtime Variables**: Override ViewDefinition constants through repeatable `--var name=value` CLI arguments
- **FHIRPath Columns**: Define columns using FHIRPath expressions with automatic caching
- **`%rowIndex`**: 0-based row index environment variable available inside `forEach`, `forEachOrNull`, and `repeat` iterations
- **Column Tags**: Implementation metadata (`ansi/type`, custom tags) attached per column and exposed in schema output
- **Streaming**: CLI tool processes large NDJSON datasets with minimal memory
- **Schema Extraction**: Automatically extract column schemas (including tags) from ViewDefinitions

## ViewDefinition Example

```json
{
  "resourceType": "ViewDefinition",
  "name": "patient_names",
  "resource": "Patient",
  "fhirVersion": ["4.0.1"],
  "select": [
    { "column": [{ "name": "id", "path": "id" }] },
    {
      "forEach": "name",
      "column": [
        {
          "name": "row_index",
          "path": "%rowIndex",
          "type": "integer",
          "tag": [{ "name": "ansi/type", "value": "INTEGER" }]
        },
        { "name": "family", "path": "family", "type": "string" }
      ]
    }
  ]
}
```

### Keep a collection in one Parquet column

To keep one row per Patient, select the repeating element in a string column with `collection: true` instead of using `forEach`:

```json
{
  "column": [
    { "name": "identifiers", "path": "identifier", "type": "string", "collection": true }
  ]
}
```

The Parquet cell contains JSON text. Parse it once to get an ordered array of FHIR objects, such as `[{"system":"urn:example:mrn","value":"001"},{"system":"urn:example:member","value":"A7"}]`. A Patient with no identifiers produces `[]`. Selecting `name` the same way keeps each HumanName's `given` array inside the object. Every `collection: true` column is written as a Parquet string column, whatever its declared `type`.

Declare `type: "string"` or omit `type` for a collection of complex values. Evaluation fails, rather than writing `null` items, when such a column declares another type or when an expression computes a complex value that has no source JSON, such as `type()`. The failure's inner exception is a `ViewDefinitionColumnException`, whose message names the column but never quotes resource values. The CLI shows only that cause on the console and in the `--stats-out` `errorMessage`; for other failures, such as a value that cannot convert to the column's `type`, it shows only the generic failure message.

## `%rowIndex`

The `%rowIndex` environment variable is a 0-based integer available inside any `forEach`, `forEachOrNull`, or `repeat` iteration. It resets to 0 for each new outer context. At the top level (outside any iteration) it is always `0`.

```json
{
  "forEach": "name",
  "column": [
    { "name": "name_index", "path": "%rowIndex", "type": "integer" },
    { "name": "family",     "path": "family",    "type": "string"  }
  ]
}
```

A common use is constructing surrogate keys: `id & '-' & %rowIndex.toString()`.

## Column Tags

Tags attach implementation-specific metadata to a column. They flow through to `ColumnSchema.Tags` in the schema output and are not evaluated against FHIR data.

```json
{
  "name": "family_name",
  "path": "name.first().family",
  "type": "string",
  "tag": [
    { "name": "ansi/type",      "value": "VARCHAR(100)" },
    { "name": "custom/indexed", "value": "true" }
  ]
}
```

Retrieve tags programmatically:

```csharp
var schemaEvaluator = new SqlOnFhirSchemaEvaluator();
var schema = schemaEvaluator.GetSchema(viewDefExpression);

foreach (var column in schema)
{
    Console.WriteLine($"{column.Name}: {column.Type}");
    foreach (var tag in column.Tags ?? [])
        Console.WriteLine($"  [{tag.Name}] = {tag.Value}");
}
```

## Conformance Matrix

Ignixa runs the shared [sql-on-fhir.js](https://github.com/FHIR/sql-on-fhir.js) test-suite definitions through its own .NET evaluator in CI, and the conformance matrix compares the test-by-test results against other known SQL on FHIR implementations:

**[Open Conformance Matrix](/sql-on-fhir-conformance)**

- **Raw Report**: [test_report.json](https://brendankowitz.github.io/ignixa-fhir/test_report.json)
- Other implementations' results are mirrored locally at docs-deploy time, so the matrix stays available even if a third-party host is down.

## Related Documentation

- [SQL on FHIR v2 Specification](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/)
