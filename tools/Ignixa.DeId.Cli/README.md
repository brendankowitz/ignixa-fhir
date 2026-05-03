# Ignixa.DeId.Cli

CLI tool for de-identifying FHIR resources using configurable FHIRPath-based rules.

## Installation

```bash
dotnet tool install --global Ignixa.DeId.Cli
```

## Usage

```bash
ignixa-deid r4 deidentify --input ./input --output ./output --config config.json
```

## Options

| Option | Description |
|--------|-------------|
| `--input` | Input folder containing FHIR resource files |
| `--output` | Output folder for de-identified files |
| `--config` | Path to configuration file (default: `configuration-sample.json`) |
| `--bulk-data` | Process NDJSON bulk data format |
| `--recursive` | Process subdirectories recursively |
| `--skip-existing` | Skip files already in output folder |
| `--validate-input` | Validate input resources |
| `--validate-output` | Validate de-identified output resources |
| `--verbose` | Enable verbose logging |

## License

MIT License. See LICENSE file in the repository root.
