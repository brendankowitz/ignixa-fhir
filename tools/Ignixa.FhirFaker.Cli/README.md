# FHIR Faker CLI

A command-line tool for generating and modeling FHIR test data.

## Installation

Install as a .NET global tool:

```bash
dotnet tool install --global Ignixa.FhirFaker.Cli
```

Or install locally in a project:

```bash
dotnet tool install Ignixa.FhirFaker.Cli
```

## Getting Help

Use the built-in help command to discover available options:

```bash
# General help
ignixa-faker help

# List all available scenarios
ignixa-faker help scenarios

# List all available observation states
ignixa-faker help states

# List all available cities
ignixa-faker help cities

# Show supported FHIR versions
ignixa-faker help versions

# Command-specific help
ignixa-faker r4 resource --help
ignixa-faker r4 scenario --help
ignixa-faker r4 population --help
```

## Usage

All commands start with a FHIR version and require an output folder:

```bash
ignixa-faker <version> <command> --out <folder> [options]
```

Available FHIR versions: `stu3`, `r4`, `r4b`, `r5`, `r6`

### Generate Single Resources

Generate a Patient resource with specific attributes:

```bash
ignixa-faker r4 resource Patient --out ./output --firstname Bob --surname Smith --from Seattle
```

Generate an Observation using a predefined state:

```bash
ignixa-faker r4 resource Observation BloodGlucose --out ./output
```

### Generate Predefined Scenarios

Generate a complete patient scenario with related resources:

```bash
ignixa-faker r4 scenario DiabeticPatient --out ./output --resolved-references
```

Available scenarios include:
- `DiabeticPatient` - Type 2 diabetes with medication escalation
- `WellnessVisit` - Routine wellness visit with observations
- `UrinaryTractInfection` - UTI diagnosis and treatment
- `AsthmaticChild` - Pediatric asthma management
- And many more...

### Generate Populations

Generate multiple patients from a specific location:

```bash
ignixa-faker r4 population --out ./output --from Seattle --count 100 --resolved-references
```

## Options

- `--out <folder>` - **Required** - Output folder for generated files (will be created if it doesn't exist)
- `--resolved-references` - Creates a batch bundle instead of references (for scenario and population commands)
- `--firstname <name>` - Set patient first name
- `--surname <name>` - Set patient surname
- `--from <city>` - Generate from a specific city
- `--count <number>` - Number of resources/patients to generate

## Output

All commands generate JSON files in the specified output directory with the format:
- Single resources: `{resource}-{name}-{id}.json` or `patient-{id}.json`
- Scenarios: `bundle-{scenario}-{id}.json`
- Populations: `bundle-population-{city}-{count}-{id}.json`

## Examples

```bash
# Generate a single patient from R4
ignixa-faker r4 resource Patient --out ./output --firstname Alice --surname Johnson

# Generate a diabetic patient scenario using R4
ignixa-faker r4 scenario DiabeticPatient --out ./output --resolved-references

# Generate 50 patients from Boston using R4
ignixa-faker r4 population --out ./output --from Boston --count 50 --resolved-references

# Generate a blood glucose observation using R4
ignixa-faker r4 resource Observation BloodGlucose --out ./output
```

## FHIR Versions

Supported FHIR versions:
- **stu3** - FHIR STU3 (v3.0.2)
- **r4** - FHIR R4 (v4.0.1)
- **r4b** - FHIR R4B (v4.3.0)
- **r5** - FHIR R5 (v5.0.0)
- **r6** - FHIR R6 (v6.0.0)

## More Information

Visit the [Ignixa FHIR repository](https://github.com/brendankowitz/ignixa-fhir) for more information.
