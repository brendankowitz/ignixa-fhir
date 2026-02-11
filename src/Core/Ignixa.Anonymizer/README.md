# Ignixa.Anonymizer

FHIR resource de-identification and anonymization via FHIRPath-based rules. Supports HIPAA Safe Harbor de-identification standards.

## Installation

```bash
dotnet add package Ignixa.Anonymizer
```

## Quick Start

```csharp
using Ignixa.Anonymizer;
using Ignixa.Specification;

// Create anonymizer from configuration
var schema = FhirVersion.R4.GetSchemaProvider();
var engine = new AnonymizerEngine("config.json", schema);

// Anonymize FHIR JSON
var patientJson = """
{
  "resourceType": "Patient",
  "id": "example",
  "name": [{ "family": "Smith", "given": ["John"] }],
  "birthDate": "2000-01-01"
}
""";

var anonymized = engine.AnonymizeJson(patientJson);
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

## Anonymization Methods

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

- **Dates:** Preserve year only (if age ≤89)
- **Ages:** Truncate ages >89 to "90+"
- **Zip Codes:** Keep first 3 digits (except restricted areas)

## Batch Processing

```csharp
using Ignixa.Anonymizer.PartitionedExecution;

var reader = new FhirStreamReader(inputStream);
var consumer = new FhirStreamConsumer(outputStream);

var executor = new FhirPartitionedExecutor<string, string>(reader, consumer)
{
    PartitionCount = 8,
    BatchSize = 100,
    AnonymizerFunctionAsync = async content =>
        await Task.Run(() => engine.AnonymizeJson(content))
};

await executor.ExecuteAsync(cancellationToken);
```

## Documentation

**Full documentation:** https://brendankowitz.github.io/ignixa-fhir/docs/core-sdk/anonymizer

## Attribution

This library is based on the [FHIR Tools for Anonymization](https://github.com/microsoft/FHIR-Tools-for-Anonymization) by Microsoft, adapted for the Ignixa ecosystem.

## License

MIT License. See LICENSE file in the repository root.
