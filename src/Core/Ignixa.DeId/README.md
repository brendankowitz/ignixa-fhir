# Ignixa.DeId

FHIR resource de-identification via FHIRPath-based rules. Supports HIPAA Safe Harbor de-identification standards.

## Installation

```bash
dotnet add package Ignixa.DeId
```

## Quick Start

```csharp
using Ignixa.DeId;
using Ignixa.Specification;

// Create de-identification engine from configuration
var schema = FhirVersion.R4.GetSchemaProvider();
var engine = new DeIdEngine("config.json", schema);

// De-identify FHIR JSON
var patientJson = """
{
  "resourceType": "Patient",
  "id": "example",
  "name": [{ "family": "Smith", "given": ["John"] }],
  "birthDate": "2000-01-01"
}
""";

var result = await engine.DeidentifyAsync(patientJson);
```

## Configuration Example

```json
{
  "fhirVersion": "R4",
  "fhirPathRules": [
    {
      "path": "Patient.id",
      "method": "cryptoHash"
    },
    {
      "path": "descendants().ofType(HumanName)",
      "method": "redact"
    },
    {
      "path": "descendants().ofType(date)",
      "method": "dateShift"
    }
  ],
  "parameters": {
    "dateShiftKey": "your-secret-key",
    "cryptoHashKey": "your-hash-key",
    "enablePartialDatesForRedact": true,
    "enablePartialAgesForRedact": true,
    "enablePartialZipCodesForRedact": true
  }
}
```

## De-identification Methods

| Method | Description |
|--------|-------------|
| **cryptoHash** | HMAC-SHA256 hashing (deterministic) |
| **dateShift** | Consistent date shifting |
| **redact** | Data removal with optional partial preservation (HIPAA Safe Harbor) |
| **encrypt** | Reversible AES encryption |
| **substitute** | Value replacement |
| **perturb** | Statistical noise addition |
| **keep** | Explicit preservation |
| **generalize** | Conditional value generalization |

## HIPAA Safe Harbor Support

Partial redaction features for HIPAA compliance:

- **Dates:** Preserve year only (if age <=89)
- **Ages:** Truncate ages >89 to "90+"
- **Zip Codes:** Keep first 3 digits (except restricted areas)

## Batch Processing

```csharp
using Ignixa.Serialization.SourceNodes;

async IAsyncEnumerable<ResourceJsonNode> LoadResourcesAsync()
{
    foreach (var line in File.ReadLines("patients.ndjson"))
    {
        yield return ResourceJsonNode.Parse(line);
    }
}

var resources = LoadResourcesAsync();

await foreach (var result in engine.DeidentifyManyAsync(resources))
{
    if (result.IsSuccess)
    {
        await File.AppendAllTextAsync("de-identified.ndjson", result.Value.DeidentifiedJson + "\n");
    }
    else
    {
        Console.Error.WriteLine($"Failed: {result.Error.Message}");
    }
}
```

## Documentation

**Full documentation:** https://brendankowitz.github.io/ignixa-fhir/docs/core-sdk/deid

## Attribution

This library is based on the [FHIR Tools for Anonymization](https://github.com/microsoft/FHIR-Tools-for-Anonymization) by Microsoft, adapted for the Ignixa ecosystem.

## License

MIT License. See LICENSE file in the repository root.
