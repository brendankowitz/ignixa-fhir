# FHIR Validation CLI

A command-line tool for validating FHIR resources using the Ignixa.Validation library.

## Installation

Install as a .NET global tool:

```bash
dotnet tool install --global Ignixa.Validation.Cli
```

Or install locally in a project:

```bash
dotnet tool install Ignixa.Validation.Cli
```

## Usage

All commands start with a FHIR version:

```bash
fhir-validation <version> validate [options]
```

Available FHIR versions: `stu3`, `r4`, `r4b`, `r5`, `r6`

### Validation Options

The tool supports three different usage modes:

#### 1. Validate a JSON file and save results

Validate a FHIR resource from a file and save the validation results (as an OperationOutcome resource):

```bash
fhir-validation r4 validate --input patient.json --out validation-results.json
```

#### 2. Validate a JSON string

Validate a FHIR resource from a JSON string (useful for CI/CD pipelines):

```bash
fhir-validation r4 validate --json '{ "resourceType": "Patient", "id": "example" }' --out results.json
```

#### 3. Display formatted results in console

Validate and display nicely formatted results in the console:

```bash
fhir-validation r4 validate --input patient.json --console
```

You can also combine `--out` and `--console` to get both file output and console display:

```bash
fhir-validation r4 validate --input patient.json --out results.json --console
```

## Output Formats

### OperationOutcome (JSON file)

When using `--out`, the tool generates a FHIR OperationOutcome resource containing all validation issues. This format is compatible with FHIR servers and can be processed programmatically.

### Console Output

When using `--console`, the tool displays a formatted summary similar to validator.fhir.org:

```
═══════════════════════════════════════════════════════════════
  FHIR Validation Results (R4)
═══════════════════════════════════════════════════════════════
  Resource Type: Patient
  Status: ✗ INVALID
═══════════════════════════════════════════════════════════════

Issue Summary:
  Fatal:          0
  Error:          2
  Warning:        1
  Information:    0
  Total:          3

───────────────────────────────────────────────────────────────
Validation Issues:
───────────────────────────────────────────────────────────────

❌ ERROR        @ Patient.identifier[0].system
   cardinality-violation: Patient.identifier[0].system must have at least 1 occurrence(s), but found 0

❌ ERROR        @ Patient.name[0]
   ele-1: All FHIR elements must have a @value or children

⚠️  WARNING      @ Patient.telecom[0].value
   Recommended element is missing

═══════════════════════════════════════════════════════════════
```

## Examples

```bash
# Validate an R4 Patient resource with console output
fhir-validation r4 validate --input patient.json --console

# Validate and save OperationOutcome to file
fhir-validation r4 validate --input observation.json --out validation.json

# Validate a JSON string
fhir-validation r4 validate --json '{"resourceType":"Patient","id":"test"}' --console

# Validate using different FHIR versions
fhir-validation stu3 validate --input patient-stu3.json --console
fhir-validation r5 validate --input patient-r5.json --console

# Both file and console output
fhir-validation r4 validate --input bundle.json --out results.json --console
```

## Exit Codes

- `0` - Validation successful (no errors)
- `1` - Validation failed (errors found) or tool error

## FHIR Versions

Supported FHIR versions:
- **stu3** - FHIR STU3 (v3.0.2)
- **r4** - FHIR R4 (v4.0.1)
- **r4b** - FHIR R4B (v4.3.0)
- **r5** - FHIR R5 (v5.0.0)
- **r6** - FHIR R6 (v6.0.0)

## More Information

Visit the [Ignixa FHIR repository](https://github.com/brendankowitz/ignixa-fhir) for more information.
