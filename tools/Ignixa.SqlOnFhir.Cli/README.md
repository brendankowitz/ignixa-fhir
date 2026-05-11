# Ignixa SQL on FHIR CLI

Command-line tool for processing FHIR NDJSON resources with SQL on FHIR ViewDefinitions.

## Installation

```bash
dotnet tool install -g Ignixa.SqlOnFhir.Cli
```

## Usage

All commands require the FHIR version first (`stu3`, `r4`, `r4b`, `r5`, or `r6`), followed by the operation.

```bash
ignixa-sqlonfhir <version> run --views <file|dir> --input <file|dir> --out <file|dir>
ignixa-sqlonfhir <version> preview --views <file|dir> [--input <file|dir>]
ignixa-sqlonfhir <version> validate --views <file|dir>
```

## Commands

### Run ViewDefinitions

Use `run` to convert NDJSON resources to Parquet, CSV, or NDJSON. Single-file mode detects the format from `--out`; batch mode uses `--format`.

```bash
ignixa-sqlonfhir r4 run \
  --views patient-view.json \
  --input patients.ndjson \
  --out patients.parquet

ignixa-sqlonfhir r4 run \
  --views patient-view.json \
  --input patients.ndjson \
  --out patients.csv

ignixa-sqlonfhir r4 run \
  --views patient-view.json \
  --input patients.ndjson \
  --out patients.ndjson
```

Batch mode discovers ViewDefinitions in a directory and matches NDJSON inputs by resource type:

```bash
ignixa-sqlonfhir r4 run \
  --views views \
  --input fhir-ndjson \
  --out output \
  --format parquet \
  --pattern "**/*.json" \
  --input-pattern "*{resource}*.ndjson" \
  --stats-out output/stats.json
```

### Preview Schema and Sample Data

Use `preview` to display the extracted schema. Add `--input` to show sample rows.

```bash
ignixa-sqlonfhir r4 preview --views patient-view.json

ignixa-sqlonfhir r4 preview \
  --views patient-view.json \
  --input patients.ndjson \
  --rows 10
```

Directory preview accepts the same `--views` directory and `--pattern` used by batch `run`.

### Validate ViewDefinitions

Validate one ViewDefinition or every matching file in a directory:

```bash
ignixa-sqlonfhir r4 validate --views patient-view.json

ignixa-sqlonfhir r4 validate \
  --views views \
  --pattern "**/*.json"
```

## FHIRPath Variables

`run` and `preview` accept repeatable runtime variables for ViewDefinitions that reference declared constants:

```bash
ignixa-sqlonfhir r4 run \
  --views patient-view.json \
  --input patients.ndjson \
  --out patients.csv \
  --var cohortId=research-2026 \
  --var effectiveDate=2026-01-01
```

Caller-provided variables override ViewDefinition constants with the same name.

## Options

| Option | Commands | Description |
|--------|----------|-------------|
| `--views` | `run`, `preview`, `validate` | ViewDefinition file or directory. |
| `--input` | `run`, `preview` | NDJSON file or directory. Optional for schema-only preview. |
| `--out` | `run` | Output file in single mode or output directory in batch mode. |
| `--format` | `run` | Batch output format: `parquet`, `csv`, or `ndjson`. Defaults to `parquet`. |
| `--pattern` | `run`, `preview`, `validate` | ViewDefinition glob for directory mode. Defaults to `**/*.json`. |
| `--input-pattern` | `run` | NDJSON match pattern for batch mode. Defaults to `*{resource}*.ndjson`. |
| `--var` | `run`, `preview` | Runtime FHIRPath variable as `name=value`; repeat for multiple variables. |
| `--quiet` | `run` | Suppress console output. |
| `--stats-out` | `run` | Write batch stats JSON to a file. |

## FHIR Version Support

- `stu3` - FHIR STU3 specification
- `r4` - FHIR R4 specification
- `r4b` - FHIR R4B specification
- `r5` - FHIR R5 specification
- `r6` - FHIR R6 specification

Specify the version as the first argument to any command.

## Sample ViewDefinition

Create a file `patient-view.json`:

```json
{
  "resourceType": "ViewDefinition",
  "resource": "Patient",
  "select": [
    {
      "column": [
        { "name": "id", "path": "id", "type": "string" },
        { "name": "family_name", "path": "name.where(use='official').first().family", "type": "string" },
        { "name": "given_name", "path": "name.where(use='official').first().given.first()", "type": "string" },
        { "name": "birth_date", "path": "birthDate", "type": "date" }
      ]
    }
  ]
}
```
