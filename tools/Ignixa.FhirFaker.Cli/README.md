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

### Generate Single Resources

Generate a Patient resource with specific attributes:

```bash
fhir-faker resource Patient --firstname Bob --surname Smith --from Seattle
```

Generate an Observation using a predefined state:

```bash
fhir-faker resource Observation BloodGlucose
```

### Generate Predefined Scenarios

Generate a complete patient scenario with related resources:

```bash
fhir-faker scenario DiabeticPatient --resolved-references
```

Available scenarios include:
- `DiabeticPatient` - Type 2 diabetes with medication escalation
- `WellnessVisit` - Routine wellness visit with observations
- `UrinaryTractInfection` - UTI diagnosis and treatment
- And more...

### Generate Populations

Generate multiple patients from a specific location:

```bash
fhir-faker population --from Seattle --count 100 --resolved-references
```

## Options

- `--resolved-references` - Creates a batch bundle instead of references
- `--firstname <name>` - Set patient first name
- `--surname <name>` - Set patient surname
- `--from <city>` - Generate from a specific city
- `--count <number>` - Number of resources/patients to generate

## Output

All commands generate JSON files in the current directory with the format:
- Single resources: `{resource}-{name}-{id}.json`
- Scenarios: `bundle-{scenario}-{id}.json`
- Populations: `bundle-population-{city}-{count}-{id}.json`

## Examples

```bash
# Generate a single patient
fhir-faker resource Patient --firstname Alice --surname Johnson

# Generate a diabetic patient scenario
fhir-faker scenario DiabeticPatient --resolved-references

# Generate 50 patients from Boston
fhir-faker population --from Boston --count 50 --resolved-references
```

## More Information

Visit the [Ignixa FHIR repository](https://github.com/brendankowitz/ignixa-fhir) for more information.
