# FHIR Fakes CLI

A command-line tool for generating and modeling FHIR test data.

## Installation

Install as a .NET global tool:

```bash
dotnet tool install --global Ignixa.FhirFakes.Cli
```

Or install locally in a project:

```bash
dotnet tool install Ignixa.FhirFakes.Cli
```

## Getting Help

Use the built-in help command to discover available options:

```bash
# General help
ignixa-fakes help

# List all available scenarios
ignixa-fakes help scenarios

# List all available observation states
ignixa-fakes help states

# List all available cities
ignixa-fakes help cities

# Show supported FHIR versions
ignixa-fakes help versions

# Command-specific help
ignixa-fakes r4 resource --help
ignixa-fakes r4 scenario --help
ignixa-fakes r4 population --help
```

## Usage

All commands start with a FHIR version and require an output folder:

```bash
ignixa-fakes <version> <command> --out <folder> [options]
```

Available FHIR versions: `stu3`, `r4`, `r4b`, `r5`, `r6`

### Generate Single Resources

Generate a Patient resource with specific attributes:

```bash
ignixa-fakes r4 resource Patient --out ./output --firstname Bob --surname Smith --from Seattle
```

Generate an Observation using a predefined state:

```bash
ignixa-fakes r4 resource Observation BloodGlucose --out ./output
```

### Generate Predefined Scenarios

Generate a complete patient scenario with related resources:

```bash
ignixa-fakes r4 scenario DiabeticPatient --out ./output --resolved-references

# Override a scenario parameter (repeatable)
ignixa-fakes r4 scenario DiabeticPatient --out ./output --param age=60 --param severity=3
```

Available scenarios include:
- `DiabeticPatient` - Type 2 diabetes with medication escalation
- `WellnessVisit` - Routine wellness visit with observations
- `UrinaryTractInfection` - UTI diagnosis and treatment
- `AsthmaticChild` - Pediatric asthma management
- And many more...

Run `ignixa-fakes help scenarios` for the full, current list — or discover them programmatically via
`ScenarioCatalog.GetAll()` in the `Ignixa.FhirFakes` library.

### Generate at Maximum Density with a Clinical Theme

Fill in every optional element, keeping sibling coded fields thematically coherent:

```bash
ignixa-fakes r4 resource Procedure --out ./output --density maximum --theme cardiology

# Omit --theme for a random (but still coherent) theme; use --theme none to disable theming
ignixa-fakes r4 resource Procedure --out ./output --density maximum
```

### Generate Populations

Generate multiple patients from a specific location:

```bash
ignixa-fakes r4 population --out ./output --from Seattle --count 100 --resolved-references
```

Generate populations in ndjson format (useful for `$import` operations):

```bash
ignixa-fakes r4 population --out ./output --from Seattle --count 100 --ndjson
```

## Options

- `--out <folder>` - **Required** - Output folder for generated files (will be created if it doesn't exist)
- `--resolved-references` - Creates a batch bundle instead of references (for scenario and population commands)
- `--ndjson` - Write ndjson files instead of bundles (implies --resolved-references, only available for population command)
- `--firstname <name>` - Set patient first name
- `--surname <name>` - Set patient surname
- `--from <city>` - Generate from a specific city
- `--count <number>` - Number of resources/patients to generate
- `--param <name=value>` - Override a scenario parameter, repeatable (`scenario` command only)
- `--density <minimal|realistic|maximum>` - Control how many optional elements are populated (`resource` command; default `minimal`)
- `--theme <domain|none>` - Clinical theme for coherent code selection, e.g. `cardiology`, `orthopedic-surgery`, or `none` to disable (`resource` command)
- `--seed <number>` - Seed for reproducible generation (`resource` command)
- `--edge-cases [selectors]` - Apply edge-case/fuzz mutations, optionally scoped to families or categories (`resource` command)
- `--validate` - Validate generated resources against the FHIR schema

**Exit codes** (scripting/CI): `0` success, `1` runtime error (generation/I/O failure), `2` usage error (invalid arguments, unknown scenario/state/selector/theme/density value).

## Output

All commands generate JSON files in the specified output directory with the format:
- Single resources: `{resource}-{name}-{id}.json` or `patient-{id}.json`
- Scenarios: `bundle-{scenario}-{id}.json`
- Populations (bundles): `bundle-population-{city}-{count}-{id}.json`
- Populations (ndjson): `{version}-population-{city}-{ResourceType}-{count}-{id}.ndjson`

## Examples

```bash
# Generate a single patient from R4
ignixa-fakes r4 resource Patient --out ./output --firstname Alice --surname Johnson

# Generate a diabetic patient scenario using R4
ignixa-fakes r4 scenario DiabeticPatient --out ./output --resolved-references

# Generate 50 patients from Boston using R4 as bundles
ignixa-fakes r4 population --out ./output --from Boston --count 50 --resolved-references

# Generate 50 patients from Boston using R4 as ndjson files (for $import)
ignixa-fakes r4 population --out ./output --from Boston --count 50 --ndjson

# Generate a blood glucose observation using R4
ignixa-fakes r4 resource Observation BloodGlucose --out ./output
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
