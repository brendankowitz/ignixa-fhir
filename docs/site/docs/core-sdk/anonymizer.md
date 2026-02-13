---
sidebar_position: 6
title: Anonymizer
description: FHIR resource anonymization via FHIRPath-based rules
---

# Ignixa.Anonymizer

The `Ignixa.Anonymizer` package provides FHIR resource de-identification and anonymization via FHIRPath-based rules. Supports HIPAA Safe Harbor de-identification standards and multiple anonymization methods.

## Installation

```bash
dotnet add package Ignixa.Anonymizer
```

## Getting Started

### 1. Register Services

Register the anonymizer with dependency injection:

```csharp
using Ignixa.Anonymizer.Extensions;
using Ignixa.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();

// Register anonymizer with configuration file
services.AddFhirAnonymizer(builder =>
{
    builder.WithConfigurationFile("anonymizer-config.json");
});

// Register FHIR schema provider
services.AddSingleton<IFhirSchemaProvider>(FhirVersion.R4.GetSchemaProvider());

// Add logging (optional but recommended)
services.AddLogging(logging => logging.AddConsole());

var provider = services.BuildServiceProvider();
```

### 2. Anonymize Resources

Use the `IAnonymizerEngine` interface to anonymize resources:

```csharp
using Ignixa.Anonymizer;

var engine = provider.GetRequiredService<IAnonymizerEngine>();

var patientJson = """
{
  "resourceType": "Patient",
  "id": "example",
  "name": [{ "family": "Smith", "given": ["John"] }],
  "birthDate": "2000-01-01"
}
""";

var result = await engine.AnonymizeAsync(patientJson);

if (result.IsSuccess)
{
    Console.WriteLine(result.Value.AnonymizedJson);
    Console.WriteLine($"Processed {result.Value.Metrics.NodesProcessed} nodes in {result.Value.Metrics.Duration.TotalMilliseconds}ms");
}
else
{
    Console.Error.WriteLine($"Error: {result.Error.Message}");
}
```

**Output:**
```json
{
  "resourceType": "Patient",
  "id": "698d54f0494528a759f19c8e87a9f99e75a5881b9267ee3926bcf62c992d84ba",
  "meta": {
    "security": [
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "code": "REDACTED",
        "display": "redacted"
      }
    ]
  },
  "birthDate": "2000-02-11"
}
```

## Error Handling

The library uses the `Result<T>` pattern for explicit error handling:

```csharp
var result = await engine.AnonymizeAsync(resourceJson);

// Pattern matching
var output = result.Match(
    onSuccess: r => r.AnonymizedJson,
    onFailure: err => $"ERROR: {err.Message}"
);

// Conditional checking
if (result.IsSuccess)
{
    var anonymized = result.Value.AnonymizedJson;
    var resource = result.Value.Resource; // Parsed ResourceJsonNode for chaining
    var metrics = result.Value.Metrics;
    var warnings = result.Value.Warnings;
}
else
{
    var errorCode = result.Error.Code;
    var errorMessage = result.Error.Message;
    var exception = result.Error.Exception; // May be null
    var path = result.Error.Path; // FHIRPath location if applicable
}
```

## Request Options

Control anonymization behavior per request using `RequestOptions`:

```csharp
using Ignixa.Anonymizer;

var options = new RequestOptions
{
    IsPrettyOutput = true,
    ValidateInput = true,
    ValidateOutput = true
};

var result = await engine.AnonymizeAsync(resourceJson, options);
```

**Available Options:**

| Option | Description | Default |
|--------|-------------|---------|
| `IsPrettyOutput` | Format output JSON with indentation | `false` |
| `ValidateInput` | Validate input resources before anonymization | `false` |
| `ValidateOutput` | Validate anonymized output resources | `false` |

## Bulk Processing

For processing multiple resources, use the streaming API:

```csharp
using Ignixa.Serialization.SourceNodes;
using System.Text.Json.Nodes;

async IAsyncEnumerable<ResourceJsonNode> LoadResourcesAsync()
{
    foreach (var line in File.ReadLines("patients.ndjson"))
    {
        yield return ResourceJsonNode.Parse(line);
    }
}

var resources = LoadResourcesAsync();

await foreach (var result in engine.AnonymizeManyAsync(resources))
{
    if (result.IsSuccess)
    {
        await File.AppendAllTextAsync("anonymized.ndjson", result.Value.AnonymizedJson + "\n");
    }
    else
    {
        Console.Error.WriteLine($"Failed: {result.Error.Message}");
    }
}
```

## Configuration

### Using Configuration File

```csharp
services.AddFhirAnonymizer(builder =>
{
    builder.WithConfigurationFile("config.json");
});
```

### Using In-Memory Configuration

```csharp
using Ignixa.Anonymizer.Configuration;

services.AddFhirAnonymizer(builder =>
{
    builder.WithOptions(options =>
    {
        options.Configure(opts =>
        {
            opts.FhirVersion = "R4";
            opts.Rules = [
                new FhirPathRule
                {
                    Path = "Patient.id",
                    Method = "cryptoHash"
                },
                new FhirPathRule
                {
                    Path = "descendants().ofType(HumanName)",
                    Method = "redact"
                }
            ];
            opts.Parameters = new ParameterOptions
            {
                DateShiftKey = "your-secret-key",
                CryptoHashKey = "your-hash-key",
                EnablePartialDatesForRedact = true,
                EnablePartialAgesForRedact = true,
                EnablePartialZipCodesForRedact = true
            };
        });
    });
});
```

### Configuration File Format

Anonymization rules are defined in a JSON configuration file:

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
    "encryptKey": "your-encrypt-key",
    "enablePartialDatesForRedact": true,
    "enablePartialAgesForRedact": true,
    "enablePartialZipCodesForRedact": true,
    "dateShiftScope": "resource",
    "restrictedZipCodeTabulationAreas": ["036", "059", "102"]
  }
}
```

### Configuration Fields

#### fhirVersion

The FHIR version for validation. Valid values: `"R4"`, `"R4B"`, `"R5"`, `"STU3"`. Leave empty for version-agnostic processing.

#### fhirPathRules

Array of anonymization rules. Each rule has:

| Field | Type | Description |
|-------|------|-------------|
| `path` | string | FHIRPath expression to select elements |
| `method` | string | Anonymization method (see below) |
| Additional fields | varies | Method-specific settings |

**Rule Precedence:** Rules execute in order. Earlier rules take precedence over later rules.

#### parameters

Global configuration:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `dateShiftKey` | string | auto-generated | Secret key for consistent date shifting |
| `dateShiftScope` | string | `"resource"` | Scope for date shifting: `"resource"`, `"file"`, `"folder"` |
| `cryptoHashKey` | string | auto-generated | Secret key for HMAC-SHA256 hashing |
| `encryptKey` | string | auto-generated | Secret key for AES encryption |
| `enablePartialDatesForRedact` | boolean | `false` | Preserve year for HIPAA Safe Harbor |
| `enablePartialAgesForRedact` | boolean | `false` | Round ages greater than 89 to 90+ |
| `enablePartialZipCodesForRedact` | boolean | `false` | Truncate zip codes to 3 digits |
| `restrictedZipCodeTabulationAreas` | string[] | `[]` | Zip prefixes with population less than 20,000 |

## Anonymization Methods

### cryptoHash

Replaces values with HMAC-SHA256 hash. Deterministic (same input = same output with same key).

```json
{
  "path": "Patient.id",
  "method": "cryptoHash"
}
```

**Use case:** Patient identifiers, resource IDs, references.

**Before:**
```json
{
  "resourceType": "Patient",
  "id": "example",
  "managingOrganization": {
    "reference": "Organization/1"
  }
}
```

**After:**
```json
{
  "resourceType": "Patient",
  "id": "698d54f0494528a759f19c8e87a9f99e75a5881b9267ee3926bcf62c992d84ba",
  "managingOrganization": {
    "reference": "urn:uuid:c79c7c19a33d2c87e8e45e4e50f5dfd8"
  }
}
```

### dateShift

Shifts dates by a consistent random offset per resource/file/folder.

```json
{
  "path": "descendants().ofType(date)",
  "method": "dateShift"
}
```

**Configuration:**
- `dateShiftKey` - Secret for deterministic shifting
- `dateShiftScope` - Scope: `"resource"`, `"file"`, `"folder"`

**Use case:** Preserve temporal relationships while masking actual dates.

**Before:**
```json
{
  "birthDate": "2000-01-01",
  "deceasedDateTime": "2023-06-15T10:00:00Z"
}
```

**After (shifted by +41 days):**
```json
{
  "birthDate": "2000-02-11",
  "deceasedDateTime": "2023-07-26T10:00:00Z"
}
```

### redact

Removes or partially redacts sensitive data according to HIPAA Safe Harbor rules.

```json
{
  "path": "descendants().ofType(HumanName)",
  "method": "redact"
}
```

**Partial Redaction Features** (HIPAA Safe Harbor compliant):

| Data Type | Behavior with `enablePartial*` |
|-----------|-------------------------------|
| **Dates** | Keep year only if age 89 or younger |
| **Ages** | Truncate ages over 89 to "90+" |
| **Zip Codes** | Keep first 3 digits (except restricted areas) |

**Example with Partial Dates:**

```json
// Configuration
{
  "parameters": {
    "enablePartialDatesForRedact": true,
    "enablePartialAgesForRedact": true
  }
}
```

**Before:**
```json
{
  "resourceType": "Patient",
  "birthDate": "1985-06-15",
  "name": [{ "family": "Smith", "given": ["John"] }],
  "address": [{ "postalCode": "12345" }]
}
```

**After:**
```json
{
  "resourceType": "Patient",
  "birthDate": "1985",
  "address": [{ "postalCode": "12300" }]
}
```

**Restricted Zip Codes:** The `restrictedZipCodeTabulationAreas` parameter lists 3-digit zip prefixes with population less than 20,000 (per HIPAA). These are fully redacted:

```json
{
  "parameters": {
    "restrictedZipCodeTabulationAreas": ["036", "059", "102", "203", "205"]
  }
}
```

### encrypt

AES encryption for reversible anonymization.

```json
{
  "path": "Patient.identifier.value",
  "method": "encrypt"
}
```

**Use case:** When de-anonymization is required later.

**Before:**
```json
{
  "identifier": [
    { "system": "urn:oid:1.2.36.146.595.217.0.1", "value": "12345" }
  ]
}
```

**After:**
```json
{
  "identifier": [
    { "system": "urn:oid:1.2.36.146.595.217.0.1", "value": "U2FsdGVkX1..." }
  ]
}
```

### substitute

Replaces values with fixed substitutes.

**Primitive values:**
```json
{
  "path": "Patient.gender",
  "method": "substitute",
  "replaceWith": "unknown"
}
```

**Complex types:**
```json
{
  "path": "Patient.name[0]",
  "method": "substitute",
  "replaceWith": "{\"family\": \"Anonymous\", \"given\": [\"Patient\"]}"
}
```

**Before:**
```json
{
  "gender": "male",
  "name": [{ "family": "Smith", "given": ["John"] }]
}
```

**After:**
```json
{
  "gender": "unknown",
  "name": [{ "family": "Anonymous", "given": ["Patient"] }]
}
```

### perturb

Adds random noise to numeric values for statistical privacy.

```json
{
  "path": "Observation.valueQuantity",
  "method": "perturb",
  "span": 5.0,
  "rangeType": "fixed",
  "roundTo": 2
}
```

**Settings:**

| Field | Type | Description |
|-------|------|-------------|
| `span` | number | Noise range (plus/minus span/2) |
| `rangeType` | string | `"fixed"` or `"proportional"` |
| `roundTo` | integer | Decimal places (0-28) |

**Use case:** Anonymize lab values while preserving statistical properties.

**Before:**
```json
{
  "resourceType": "Observation",
  "valueQuantity": {
    "value": 120.5,
    "unit": "mg/dL"
  }
}
```

**After (with span=5, rangeType=fixed, roundTo=1):**
```json
{
  "resourceType": "Observation",
  "valueQuantity": {
    "value": 122.3,
    "unit": "mg/dL"
  }
}
```

### keep

Explicitly preserves elements that would otherwise be redacted.

```json
{
  "path": "descendants().ofType(HumanName)",
  "method": "redact"
},
{
  "path": "Patient.name.use",
  "method": "keep"
}
```

**Use case:** Whitelist specific fields when using broad redaction rules.

**Before:**
```json
{
  "name": [
    { "use": "official", "family": "Smith", "given": ["John"] }
  ]
}
```

**After:**
```json
{
  "name": [
    { "use": "official" }
  ]
}
```

### generalize

Generalizes values based on conditional rules.

```json
{
  "path": "Patient.communication.language.coding.code",
  "method": "generalize",
  "cases": {
    "$this in ('en-US' | 'en-GB' | 'en-AU')": "'en'",
    "('es-ES' | 'es-MX') contains $this": "'es'"
  },
  "otherValues": "keep"
}
```

**Settings:**

| Field | Type | Description |
|-------|------|-------------|
| `cases` | object | Map of FHIRPath condition → replacement expression |
| `otherValues` | string | `"keep"` or `"redact"` for unmatched values |

**Use case:** Reduce granularity of coded values.

**Before:**
```json
{
  "communication": [
    {
      "language": {
        "coding": [
          { "system": "urn:ietf:bcp:47", "code": "en-US" }
        ]
      }
    }
  ]
}
```

**After:**
```json
{
  "communication": [
    {
      "language": {
        "coding": [
          { "system": "urn:ietf:bcp:47", "code": "en" }
        ]
      }
    }
  ]
}
```

## FHIRPath Rules Guide

### Basic Path Expressions

```json
// Specific element
{"path": "Patient.id", "method": "cryptoHash"}

// Nested element
{"path": "Patient.name.family", "method": "redact"}

// Array elements
{"path": "Patient.identifier.value", "method": "redact"}
```

### Using descendants()

Match all descendants of a type:

```json
// All HumanName elements anywhere in the resource
{"path": "descendants().ofType(HumanName)", "method": "redact"}

// All date primitives
{"path": "descendants().ofType(date)", "method": "dateShift"}

// All Identifier complex types
{"path": "descendants().ofType(Identifier)", "method": "redact"}
```

### Conditional Selection

```json
// Addresses in specific city
{"path": "Patient.address.where(city='Boston')", "method": "keep"}

// Phone numbers with specific use
{"path": "Patient.telecom.where(system='phone' and use='mobile')", "method": "redact"}
```

### Common Patterns

**Redact all 18 HIPAA identifiers:**

```json
{
  "fhirPathRules": [
    {"path": "descendants().ofType(HumanName)", "method": "redact"},
    {"path": "descendants().ofType(Address)", "method": "redact"},
    {"path": "descendants().ofType(ContactPoint)", "method": "redact"},
    {"path": "descendants().ofType(Identifier)", "method": "redact"},
    {"path": "descendants().ofType(Attachment)", "method": "redact"},
    {"path": "descendants().ofType(date)", "method": "dateShift"},
    {"path": "descendants().ofType(dateTime)", "method": "dateShift"},
    {"path": "descendants().ofType(instant)", "method": "dateShift"}
  ]
}
```

**Preserve structure, redact content:**

```json
{
  "fhirPathRules": [
    {"path": "Patient.name.use", "method": "keep"},
    {"path": "Patient.address.state", "method": "keep"},
    {"path": "Patient.address.country", "method": "keep"},
    {"path": "descendants().ofType(HumanName)", "method": "redact"},
    {"path": "descendants().ofType(Address)", "method": "redact"}
  ]
}
```

**Hash references to maintain relationships:**

```json
{
  "fhirPathRules": [
    {"path": "Resource.id", "method": "cryptoHash"},
    {"path": "descendants().ofType(Reference).reference", "method": "cryptoHash"},
    {"path": "Bundle.entry.fullUrl", "method": "redact"}
  ]
}
```

## Custom Processors

Implement custom anonymization logic by creating a processor and registering it with DI:

```csharp
using Ignixa.Anonymizer.Processors;
using Ignixa.Anonymizer.Models;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

public class CustomMaskProcessor : IAnonymizerProcessor
{
    public ValueTask<ProcessResult> ProcessAsync(
        ResourceJsonNode resource,
        IElement node,
        ProcessContext? context = null,
        Dictionary<string, object>? settings = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessResult();

        if (node.Value is null)
        {
            return ValueTask.FromResult(result);
        }

        // Custom masking logic
        var value = node.Value.ToString();
        var masked = value.Length > 4
            ? "****" + value.Substring(value.Length - 4)
            : "****";

        node.Value = masked;

        result.AddProcessRecord(AnonymizationOperations.Custom, node);
        return ValueTask.FromResult(result);
    }
}
```

### Register Custom Processor

```csharp
services.AddFhirAnonymizer(builder =>
{
    builder.WithConfigurationFile("config.json");
    builder.AddProcessor<CustomMaskProcessor>("customMask");
});
```

**Configuration file:**
```json
{
  "fhirPathRules": [
    {
      "path": "Patient.identifier.value",
      "method": "customMask"
    }
  ]
}
```

## Security Labels

Anonymized resources are tagged with security labels:

```json
{
  "meta": {
    "security": [
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "code": "REDACTED",
        "display": "redacted"
      },
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "code": "ABSTRED",
        "display": "abstracted"
      },
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "code": "CRYTOHASH",
        "display": "cryptographic hash function"
      },
      {
        "code": "PERTURBED",
        "display": "exact value is replaced with another exact value"
      }
    ]
  }
}
```

## HIPAA Safe Harbor

Configure for HIPAA Safe Harbor de-identification:

```json
{
  "fhirPathRules": [
    {"path": "descendants().ofType(Extension)", "method": "redact"},
    {"path": "descendants().ofType(HumanName)", "method": "redact"},
    {"path": "descendants().ofType(Address)", "method": "redact"},
    {"path": "descendants().ofType(ContactPoint)", "method": "redact"},
    {"path": "descendants().ofType(Identifier)", "method": "redact"},
    {"path": "descendants().ofType(Attachment)", "method": "redact"},
    {"path": "descendants().ofType(Annotation)", "method": "redact"},
    {"path": "descendants().ofType(Narrative)", "method": "redact"},
    {"path": "descendants().ofType(date)", "method": "dateShift"},
    {"path": "descendants().ofType(dateTime)", "method": "dateShift"},
    {"path": "descendants().ofType(instant)", "method": "dateShift"},
    {"path": "Patient.address.state", "method": "keep"},
    {"path": "Patient.address.country", "method": "keep"}
  ],
  "parameters": {
    "dateShiftKey": "your-secret-key",
    "cryptoHashKey": "your-hash-key",
    "enablePartialDatesForRedact": true,
    "enablePartialAgesForRedact": true,
    "enablePartialZipCodesForRedact": true,
    "restrictedZipCodeTabulationAreas": [
      "036", "059", "102", "203", "205", "369", "556", "692",
      "821", "823", "878", "879", "884", "893"
    ]
  }
}
```

This configuration addresses the 18 HIPAA identifiers:

1. Names - `HumanName` redacted
2. Geographic subdivisions - `Address` redacted (except state/country)
3. Dates - Shifted with partial year preservation
4. Phone/fax/email - `ContactPoint` redacted
5. SSN - `Identifier` redacted
6. Medical record numbers - `Identifier` redacted
7. Health plan numbers - `Identifier` redacted
8. Account numbers - `Identifier` redacted
9. Certificate/license numbers - `Identifier` redacted
10. Vehicle identifiers - Custom rule if present
11. Device identifiers - Custom rule if present
12. URLs - `Attachment.url`, `Reference.reference` redacted/hashed
13. IP addresses - Custom rule if present
14. Biometric identifiers - `Attachment` redacted
15. Full-face photos - `Attachment` redacted
16. Other unique numbers - `Identifier` redacted
17. Ages over 89 - Truncated with `enablePartialAgesForRedact`
18. Zip codes - Truncated with `enablePartialZipCodesForRedact`

## API Reference

### IAnonymizerEngine

```csharp
// Anonymize single resource from JSON
ValueTask<Result<AnonymizationResult>> AnonymizeAsync(
    string resourceJson,
    RequestOptions? settings = null,
    CancellationToken cancellationToken = default);

// Anonymize parsed resource node
ValueTask<Result<AnonymizationResult>> AnonymizeAsync(
    ResourceJsonNode resource,
    RequestOptions? settings = null,
    CancellationToken cancellationToken = default);

// Anonymize stream of resources (bulk processing)
IAsyncEnumerable<Result<AnonymizationResult>> AnonymizeManyAsync(
    IAsyncEnumerable<ResourceJsonNode> resources,
    RequestOptions? settings = null,
    CancellationToken cancellationToken = default);
```

### RequestOptions

```csharp
public sealed record RequestOptions
{
    public bool IsPrettyOutput { get; init; }
    public bool ValidateInput { get; init; }
    public bool ValidateOutput { get; init; }
}
```

### AnonymizationResult

```csharp
public sealed record AnonymizationResult
{
    public required ResourceJsonNode Resource { get; init; }
    public required string AnonymizedJson { get; init; }
    public required ProcessingMetrics Metrics { get; init; }
    public ImmutableArray<string> Warnings { get; init; }
    public required AppliedSecurityLabels AppliedLabels { get; init; }
}

public sealed record ProcessingMetrics
{
    public required int NodesProcessed { get; init; }
    public required TimeSpan Duration { get; init; }
    public required ImmutableDictionary<string, int> OperationCounts { get; init; }
}
```

## CLI Tool

The `ignixa-anonymizer` tool anonymizes FHIR resources from the command line.

### Installation

```bash
dotnet tool install --global Ignixa.Anonymizer.Cli
```

### Basic Usage

The CLI tool supports multiple FHIR versions and processes files in folders:

```bash
# Anonymize R4 resources
ignixa-anonymizer r4 anonymize --input ./input --output ./output --config config.json

# Anonymize R5 resources
ignixa-anonymizer r5 anonymize --input ./fhir-data --output ./anonymized --config config.json

# Process recursively through subdirectories
ignixa-anonymizer r4 anonymize --input ./input --output ./output --config config.json --recursive

# Skip files that already exist in output
ignixa-anonymizer r4 anonymize --input ./input --output ./output --config config.json --skip-existing
```

### NDJSON Bulk Data Format

Process FHIR bulk data in NDJSON format:

```bash
# Anonymize NDJSON bulk export files
ignixa-anonymizer r4 anonymize \
  --input ./bulk-export \
  --output ./anonymized-bulk \
  --config config.json \
  --bulk-data

# Process bulk data with validation
ignixa-anonymizer r4 anonymize \
  --input ./bulk-export \
  --output ./anonymized-bulk \
  --config config.json \
  --bulk-data \
  --validate-input \
  --validate-output
```

The `--bulk-data` flag processes files as NDJSON (newline-delimited JSON), where each line is a separate FHIR resource. This format is commonly used for bulk exports (`$export` operation).

### Command Options

| Option | Description | Default |
|--------|-------------|---------|
| `--input` | Input folder containing FHIR resource files (required) | - |
| `--output` | Output folder for anonymized files (required) | - |
| `--config` | Path to anonymizer configuration file | `configuration-sample.json` |
| `--bulk-data` | Process files in NDJSON bulk data format | `false` |
| `--skip-existing` | Skip files that already exist in output folder | `false` |
| `--recursive` | Process resource files recursively through subdirectories | `false` |
| `--verbose` | Enable verbose logging (trace level) | `false` |
| `--validate-input` | Validate input resources before anonymization | `false` |
| `--validate-output` | Validate anonymized output resources | `false` |

### Supported FHIR Versions

The CLI tool supports all FHIR versions:

| Command | FHIR Version |
|---------|--------------|
| `ignixa-anonymizer stu3 anonymize` | FHIR STU3 |
| `ignixa-anonymizer r4 anonymize` | FHIR R4 |
| `ignixa-anonymizer r4b anonymize` | FHIR R4B |
| `ignixa-anonymizer r5 anonymize` | FHIR R5 |
| `ignixa-anonymizer r6 anonymize` | FHIR R6 |

### Sample Configuration

The tool includes a `configuration-sample.json` file with comprehensive rules for de-identifying common PHI elements:

```json
{
  "fhirVersion": "R4",
  "fhirPathRules": [
    {"path": "Resource.id", "method": "cryptoHash"},
    {"path": "descendants().ofType(HumanName)", "method": "redact"},
    {"path": "descendants().ofType(Address)", "method": "redact"},
    {"path": "descendants().ofType(ContactPoint)", "method": "redact"},
    {"path": "descendants().ofType(Identifier).value", "method": "redact"},
    {"path": "descendants().ofType(Reference).reference", "method": "cryptoHash"},
    {"path": "descendants().ofType(date)", "method": "dateshift"},
    {"path": "descendants().ofType(dateTime)", "method": "dateshift"},
    {"path": "descendants().ofType(instant)", "method": "dateshift"}
  ],
  "parameters": {
    "dateShiftKey": "your-secret-key-here",
    "cryptoHashKey": "your-hash-key-here",
    "enablePartialAgesForRedact": true,
    "enablePartialDatesForRedact": true,
    "enablePartialZipCodesForRedact": true
  }
}
```

### Output Files

The tool processes files and maintains folder structure:

**Standard JSON Format**:
- Input: `./input/Patient/patient-123.json`
- Output: `./output/Patient/patient-123.json` (anonymized)

**NDJSON Format** (with `--bulk-data`):
- Input: `./input/export-patients.ndjson`
- Output: `./output/export-patients.ndjson` (anonymized, line-by-line)

### Example Workflows

**De-identify Patient Records for Research**:

```bash
# Create configuration with HIPAA Safe Harbor rules
cat > hipaa-config.json <<EOF
{
  "fhirVersion": "R4",
  "fhirPathRules": [
    {"path": "Resource.id", "method": "cryptoHash"},
    {"path": "descendants().ofType(HumanName)", "method": "redact"},
    {"path": "descendants().ofType(Address)", "method": "redact"},
    {"path": "descendants().ofType(ContactPoint)", "method": "redact"},
    {"path": "descendants().ofType(date)", "method": "dateshift"},
    {"path": "descendants().ofType(dateTime)", "method": "dateshift"}
  ],
  "parameters": {
    "dateShiftKey": "research-study-2024",
    "cryptoHashKey": "research-study-hash-key",
    "enablePartialAgesForRedact": true,
    "enablePartialDatesForRedact": true,
    "enablePartialZipCodesForRedact": true
  }
}
EOF

# Anonymize all resources recursively with validation
ignixa-anonymizer r4 anonymize \
  --input ./patient-data \
  --output ./research-dataset \
  --config hipaa-config.json \
  --recursive \
  --validate-output \
  --verbose
```

**Process Bulk Export for Data Sharing**:

```bash
# Anonymize bulk export NDJSON files
ignixa-anonymizer r4 anonymize \
  --input ./bulk-export \
  --output ./anonymized-export \
  --config config.json \
  --bulk-data \
  --validate-output
```

## Related Documentation

- [FHIRPath](/docs/core-sdk/fhirpath)
- [Serialization](/docs/core-sdk/serialization)
- [Specification](/docs/core-sdk/abstractions)
- [ADR 2602: Anonymizer Library](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2602-anonymizer-library.md)
