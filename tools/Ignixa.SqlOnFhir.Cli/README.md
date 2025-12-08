# Ignixa SQL on FHIR CLI

Command-line tool for processing FHIR resources using SQL on FHIR ViewDefinitions.

## Installation

```bash
dotnet tool install -g Ignixa.SqlOnFhir.Cli
```

## Usage

### Convert FHIR Resources to Parquet

Convert FHIR resources from NDJSON to Parquet format using a ViewDefinition:

```bash
ignixa-sqlonfhir convert --viewdefinition myview.json --input mypatients.ndjson --out myparquetfile.parquet --format parquet
```

### Convert FHIR Resources to CSV

Convert FHIR resources from NDJSON to CSV format using a ViewDefinition:

```bash
ignixa-sqlonfhir convert --viewdefinition myview.json --input mypatients.ndjson --out mycsvfile.csv --format csv
```

### Preview Schema and Sample Data

Extract schema from a ViewDefinition and show a preview of converted rows:

```bash
ignixa-sqlonfhir preview --viewdefinition myview.json --input mypatients.ndjson
```

This displays:
- The extracted schema (column names and types)
- A few sample rows formatted for console display

### Validate ViewDefinition

Validate a ViewDefinition file for correctness:

```bash
ignixa-sqlonfhir validate --viewdefinition myview.json
```

## Examples

### Sample ViewDefinition

Create a file `patient-view.json`:

```json
{
  "resourceType": "ViewDefinition",
  "resource": "Patient",
  "select": [
    {
      "column": [
        {
          "name": "id",
          "path": "id",
          "type": "string"
        },
        {
          "name": "family_name",
          "path": "name.where(use='official').first().family",
          "type": "string"
        },
        {
          "name": "given_name",
          "path": "name.where(use='official').first().given.first()",
          "type": "string"
        },
        {
          "name": "birth_date",
          "path": "birthDate",
          "type": "date"
        }
      ]
    }
  ]
}
```

### Convert to Parquet

```bash
ignixa-sqlonfhir convert \
  --viewdefinition patient-view.json \
  --input patients.ndjson \
  --out patients.parquet \
  --format parquet
```

### Preview Results

```bash
ignixa-sqlonfhir preview \
  --viewdefinition patient-view.json \
  --input patients.ndjson
```

## FHIR Version Support

The tool currently defaults to FHIR R4 for all resources. Future versions will include automatic detection of FHIR versions (STU3, R4, R4B, R5, R6) from resource metadata.
