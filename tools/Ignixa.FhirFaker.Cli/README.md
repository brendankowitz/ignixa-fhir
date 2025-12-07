# FHIR Faker CLI

A command-line tool for generating realistic FHIR test data using the Ignixa FhirFaker library.

## Installation

Install as a .NET global tool:

```bash
dotnet tool install --global Ignixa.FhirFaker.Cli
```

Or install locally in a project:

```bash
dotnet tool install Ignixa.FhirFaker.Cli
```

## Usage

All commands start with a FHIR version (currently only `r4` is supported):

```bash
fhir-faker <version> <command> [options]
```

### Generate Single Resources

Generate a Patient resource with specific attributes:

```bash
fhir-faker r4 resource Patient --firstname Bob --surname Smith --from Seattle
```

Generate an Observation using a predefined state:

```bash
fhir-faker r4 resource Observation BloodGlucose
```

### Generate Predefined Scenarios

Generate a complete patient scenario with related resources:

```bash
fhir-faker r4 scenario DiabeticPatient --resolved-references
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
fhir-faker r4 population --from Seattle --count 100 --resolved-references
```

## Options

- `--resolved-references` - Creates a batch bundle instead of references (for scenario and population commands)
- `--firstname <name>` - Set patient first name
- `--surname <name>` - Set patient surname
- `--from <city>` - Generate from a specific city
- `--count <number>` - Number of resources/patients to generate

## Output

All commands generate JSON files in the current directory with the format:
- Single resources: `{resource}-{name}-{id}.json` or `patient-{id}.json`
- Scenarios: `bundle-{scenario}-{id}.json`
- Populations: `bundle-population-{city}-{count}-{id}.json`

## Examples

```bash
# Generate a single patient from R4
fhir-faker r4 resource Patient --firstname Alice --surname Johnson

# Generate a diabetic patient scenario using R4
fhir-faker r4 scenario DiabeticPatient --resolved-references

# Generate 50 patients from Boston using R4
fhir-faker r4 population --from Boston --count 50 --resolved-references

# Generate a blood glucose observation using R4
fhir-faker r4 resource Observation BloodGlucose
```

## FHIR Versions

Currently supported:
- **r4** - FHIR R4 (v4.0.1)

Coming soon:
- **r5** - FHIR R5

## More Information

Visit the [Ignixa FHIR repository](https://github.com/brendankowitz/ignixa-fhir) for more information.
