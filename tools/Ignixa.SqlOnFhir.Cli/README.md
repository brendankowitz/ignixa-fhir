# Ignixa SQL on FHIR CLI

Command-line tool for processing FHIR resources using SQL on FHIR ViewDefinitions.

## Installation

```bash
dotnet tool install -g Ignixa.SqlOnFhir.Cli
```

## Usage

All commands require specifying the FHIR version first (stu3, r4, r4b, r5, or r6), followed by the operation.

### Convert FHIR Resources to Parquet

Convert FHIR resources from NDJSON to Parquet format using a ViewDefinition. The output format is automatically detected from the file extension (.parquet or .csv):

```bash
ignixa-sqlonfhir r4 convert --viewdefinition myview.json --input mypatients.ndjson --out myparquetfile.parquet
```

### Convert FHIR Resources to CSV

Convert FHIR resources from NDJSON to CSV format using a ViewDefinition:

```bash
ignixa-sqlonfhir r4 convert --viewdefinition myview.json --input mypatients.ndjson --out mycsvfile.csv
```

### Preview Schema and Sample Data

Extract schema from a ViewDefinition and show a preview of converted rows:

```bash
ignixa-sqlonfhir r4 preview --viewdefinition myview.json --input mypatients.ndjson
```

This displays:
- The extracted schema (column names and types)
- A few sample rows formatted for console display

### Validate ViewDefinition

Validate a ViewDefinition file for correctness:

```bash
ignixa-sqlonfhir r4 validate --viewdefinition myview.json
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
ignixa-sqlonfhir r4 convert \
  --viewdefinition patient-view.json \
  --input patients.ndjson \
  --out patients.parquet
```

### Convert to CSV

```bash
ignixa-sqlonfhir r4 convert \
  --viewdefinition patient-view.json \
  --input patients.ndjson \
  --out patients.csv
```

### Preview Results

```bash
ignixa-sqlonfhir r4 preview \
  --viewdefinition patient-view.json \
  --input patients.ndjson
```

## FHIR Version Support

The tool supports multiple FHIR versions through explicit version selection:
- **stu3** - FHIR STU3 specification
- **r4** - FHIR R4 specification (most common)
- **r4b** - FHIR R4B specification
- **r5** - FHIR R5 specification
- **r6** - FHIR R6 specification

Specify the version as the first argument to any command.

## Output Format Detection

The convert command automatically detects the output format based on the file extension:
- `.parquet` - Parquet format (columnar, compressed)
- `.csv` - CSV format (comma-separated values)

Invalid extensions will be rejected with an error message.
