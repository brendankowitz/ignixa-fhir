# FHIR Mapping Language CLI (ignixa-fml)

A command-line tool for working with FHIR Mapping Language (FML). Transform, preview, and validate FHIR resource mappings.

## Current Status

**✅ Validate Command**: Fully functional - parses and validates FML mapping files
**🚧 Convert & Preview Commands**: Framework in place, requires schema/type integration for full execution

The validate command works perfectly for checking FML syntax and structure. The convert and preview commands have the basic infrastructure but require integration with FHIR schema providers (ISchema/IElement) for complete mapping execution. This will be added in future iterations.

## Installation

Install as a .NET global tool:

```bash
dotnet tool install --global Ignixa.Fml.Cli
```

Or install locally in a project:

```bash
dotnet tool install Ignixa.Fml.Cli
```

## Commands

### Convert (Transform)

Execute a mapping to transform FHIR resources and save the result.

```bash
ignixa-fml convert \
  --map "MyMap.map" \
  --input "source-patient.json" \
  --out "result-bundle.json" \
  --context "./definitions" \
  --format json
```

**Options:**
- `--map` (required) - Path to the mapping file (.map text or .json StructureMap)
- `--input` (required) - Path to input file, directory, or NDJSON file
- `--out` (required) - Path to output file or directory
- `--context` (optional) - Directory containing custom StructureDefinitions/ValueSets
- `--format` (optional) - Output format: `json` (default) or `xml`

**Input Types:**

1. **Single file**: Process one JSON file
   ```bash
   ignixa-fml convert --map map.map --input patient.json --out result.json
   ```

2. **Directory**: Process all JSON files in a directory
   ```bash
   ignixa-fml convert --map map.map --input ./input-dir --out ./output-dir
   ```

3. **NDJSON file**: Process a newline-delimited JSON file
   ```bash
   ignixa-fml convert --map map.map --input data.ndjson --out results.ndjson
   ```

### Preview

Run a mapping and display the result in the console (useful for quick iteration).

```bash
ignixa-fml preview \
  --map "MyMap.map" \
  --input "source-patient.json" \
  --context "./definitions"
```

**Options:**
- `--map` (required) - Path to the mapping file
- `--input` (required) - Path to input file
- `--context` (optional) - Directory containing custom StructureDefinitions/ValueSets

**Example output:**
```
📖 Loading mapping from MyMap.map...
✓ Mapping 'PatientTransform' loaded successfully

📄 Reading input from source-patient.json...
✓ Input loaded

🔄 Executing mapping...
✓ Mapping executed successfully

📋 Transformation Result:
─────────────────────────────────────────────────────────────
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [...]
}
─────────────────────────────────────────────────────────────
```

### Validate (Compile)

Parse and validate a mapping file to ensure it's syntactically correct.

```bash
ignixa-fml validate \
  --map "MyMap.map" \
  --context "./definitions"
```

**Options:**
- `--map` (required) - Path to the mapping file
- `--context` (optional) - Directory containing custom StructureDefinitions/ValueSets

**Example output:**
```
📖 Validating mapping from MyMap.map...

✓ Mapping validation successful!

📋 Mapping Details:
   URL: http://example.org/fhir/StructureMap/PatientTransform
   Identifier: PatientTransform

📦 Uses Declarations:
      source | Patient         | http://hl7.org/fhir/StructureDefinition/Patient
      target | Bundle          | http://hl7.org/fhir/StructureDefinition/Bundle

📊 Groups:
   PatientToBundle(source src : Patient, target bundle : Bundle)
      2 rule(s)

✓ All checks passed!
```

## FHIR Mapping Language Syntax

The tool supports the full FHIR Mapping Language specification. Example mapping:

```
map "http://example.org/fhir/StructureMap/PatientTransform" = "PatientTransform"

uses "http://hl7.org/fhir/StructureDefinition/Patient" alias Patient as source
uses "http://hl7.org/fhir/StructureDefinition/Bundle" alias Bundle as target

group PatientToBundle(source src : Patient, target bundle : Bundle) {
  src -> bundle.entry as entry then {
    src -> entry.resource = create('Patient') as tgt then PatientContent(src, tgt);
  };
}

group PatientContent(source src : Patient, target tgt : Patient) {
  src.identifier -> tgt.identifier;
  src.name -> tgt.name;
  src.telecom -> tgt.telecom;
}
```

## Working with Context Directories

The `--context` option allows you to specify a directory containing custom StructureDefinitions and ValueSets. This enables:

- Type validation against custom profiles
- Support for extensions
- ConceptMap-based terminology translation

**Directory structure:**
```
definitions/
├── StructureDefinition-custom-patient.json
├── StructureDefinition-custom-bundle.json
├── ValueSet-custom-codes.json
└── ConceptMap-code-mapping.json
```

## Examples

### Basic transformation
```bash
# Transform a single patient resource
ignixa-fml convert \
  --map patient-to-bundle.map \
  --input patient.json \
  --out bundle.json
```

### Preview with context
```bash
# Preview transformation with custom definitions
ignixa-fml preview \
  --map patient-transform.map \
  --input patient.json \
  --context ./my-profiles
```

### Batch processing
```bash
# Transform all files in a directory
ignixa-fml convert \
  --map patient-transform.map \
  --input ./patients \
  --out ./bundles
```

### NDJSON processing
```bash
# Process NDJSON file
ignixa-fml convert \
  --map patient-transform.map \
  --input patients.ndjson \
  --out transformed.ndjson
```

### Validation
```bash
# Validate mapping syntax
ignixa-fml validate --map complex-mapping.map

# Validate with context
ignixa-fml validate \
  --map complex-mapping.map \
  --context ./custom-profiles
```

## Error Handling

The tool provides clear error messages for:

- **Parse errors**: Shows line and column where syntax errors occur
- **Missing files**: Validates all input paths before processing
- **Execution errors**: Reports transformation failures with context

Example error output:
```
✗ Validation failed!

Parse Error: Unexpected token 'uses' at line 5, column 10
Location: Line 5, Column 10
```

## More Information

- [FHIR Mapping Language Specification](https://hl7.org/fhir/mapping-language.html)
- [StructureMap Resource](https://hl7.org/fhir/structuremap.html)
- [Ignixa FHIR Repository](https://github.com/brendankowitz/ignixa-fhir)

## License

MIT License - see LICENSE file in repository root
