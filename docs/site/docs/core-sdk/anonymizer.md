---
sidebar_position: 6
title: Anonymizer
description: FHIR resource anonymization via FHIRPath-based rules
---

# Anonymizer

The `Ignixa.Anonymizer` package provides FHIR resource de-identification and anonymization via FHIRPath-based rules. Supports HIPAA Safe Harbor de-identification standards and multiple anonymization methods.

## Installation

```bash
dotnet add package Ignixa.Anonymizer
```

## Quick Start

```csharp
using Ignixa.Anonymizer;
using Ignixa.Specification;

// Create anonymizer from configuration file
var schema = FhirVersion.R4.GetSchemaProvider();
var engine = new AnonymizerEngine("config.json", schema);

// Anonymize JSON
var patientJson = """
{
  "resourceType": "Patient",
  "id": "example",
  "name": [{ "family": "Smith", "given": ["John"] }],
  "birthDate": "2000-01-01"
}
""";

var anonymized = engine.AnonymizeJson(patientJson);
Console.WriteLine(anonymized);
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

## Configuration File

Anonymization rules are defined in a JSON configuration file:

```json
{
  "fhirVersion": "R4",
  "processingErrors": "raise",
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

#### processingErrors

How to handle processing errors:

| Value | Behavior |
|-------|----------|
| `"raise"` | Throw exception on error (default) |
| `"skip"` | Return empty element on error |

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

## Batch Processing

Process large datasets with parallel execution:

```csharp
using Ignixa.Anonymizer;
using Ignixa.Anonymizer.PartitionedExecution;
using Ignixa.Specification;

var schema = FhirVersion.R4.GetSchemaProvider();
var engine = new AnonymizerEngine("config.json", schema);

// Setup reader and consumer
var reader = new FhirStreamReader(inputStream);
var consumer = new FhirStreamConsumer(outputStream);

// Create executor
var executor = new FhirPartitionedExecutor<string, string>(reader, consumer)
{
    PartitionCount = 8,  // Number of parallel threads
    BatchSize = 100,     // Resources per batch
    AnonymizerFunctionAsync = async content =>
    {
        return await Task.Run(() => engine.AnonymizeJson(content));
    }
};

// Execute with progress reporting
var progress = new Progress<BatchAnonymizeProgressDetail>(detail =>
{
    Console.WriteLine($"Thread {detail.CurrentThreadId}: {detail.ProcessedCount} resources");
});

await executor.ExecuteAsync(cancellationToken, progress);
```

### Reader/Consumer Interfaces

**FhirStreamReader** - Reads from JSON stream (NDJSON or Bundle):
```csharp
var reader = new FhirStreamReader(inputStream);
```

**FhirStreamConsumer** - Writes to JSON stream:
```csharp
var consumer = new FhirStreamConsumer(outputStream, pretty: false);
```

**FhirEnumerableReader** - Reads from IEnumerable:
```csharp
var reader = new FhirEnumerableReader(resources);
```

**Custom reader:**
```csharp
public class CustomReader : IFhirDataReader<string>
{
    public async Task<string> NextAsync()
    {
        // Return next JSON string or null when done
    }
}
```

**Custom consumer:**
```csharp
public class CustomConsumer : IFhirDataConsumer<string>
{
    public async Task ConsumeAsync(string content)
    {
        // Process anonymized resource
    }

    public async Task CompleteAsync()
    {
        // Finalize (close files, etc.)
    }
}
```

### Performance Tuning

| Setting | Recommendation |
|---------|----------------|
| **PartitionCount** | 2x CPU cores for I/O-bound, 1x for CPU-bound |
| **BatchSize** | 50-100 for small resources, 10-20 for large |
| **KeepOrder** | Set to `false` for better throughput if order doesn't matter |

## Custom Processors

Implement custom anonymization logic:

```csharp
using Ignixa.Anonymizer.Processors;
using Ignixa.Anonymizer.Models;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

public class CustomMaskProcessor : IAnonymizerProcessor
{
    public ProcessResult Process(
        ResourceJsonNode resource,
        IElement node,
        ProcessContext? context = null,
        Dictionary<string, object>? settings = null)
    {
        var result = new ProcessResult();

        if (node.Value is null)
        {
            return result;
        }

        // Custom masking logic
        var value = node.Value.ToString();
        var masked = value.Length > 4
            ? "****" + value.Substring(value.Length - 4)
            : "****";

        node.Value = masked;

        result.AddProcessRecord(AnonymizationOperations.Custom, node);
        return result;
    }
}
```

### Register Custom Processor

```csharp
using Ignixa.Anonymizer.Processors.Factory;

public class CustomProcessorFactory : CustomProcessorFactory
{
    public CustomProcessorFactory()
    {
        AddProcessor("customMask", typeof(CustomMaskProcessor));
    }
}

// Use in configuration
var factory = new CustomProcessorFactory();
var engine = new AnonymizerEngine("config.json", schema, factory);
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

### AnonymizerEngine

```csharp
// Create from config file
public AnonymizerEngine(
    string configFilePath,
    IFhirSchemaProvider schema,
    IAnonymizerProcessorFactory? customProcessorFactory = null)

// Create from config manager
public AnonymizerEngine(
    AnonymizerConfigurationManager configurationManager,
    IFhirSchemaProvider schema,
    IAnonymizerProcessorFactory? customProcessorFactory = null)

// Anonymize JSON string
public string AnonymizeJson(
    string json,
    AnonymizerSettings? settings = null)

// Anonymize ResourceJsonNode
public ResourceJsonNode AnonymizeElement(ResourceJsonNode resource)
```

### AnonymizerSettings

```csharp
public class AnonymizerSettings
{
    public bool IsPrettyOutput { get; set; } = false;
}
```

### FhirPartitionedExecutor

```csharp
public class FhirPartitionedExecutor<TSource, TResult>
{
    public int PartitionCount { get; set; } = 4;
    public int BatchSize { get; set; } = 50;
    public bool KeepOrder { get; set; } = true;

    public Task ExecuteAsync(
        CancellationToken cancellationToken,
        IProgress<BatchAnonymizeProgressDetail>? progress = null);
}
```

## Related Documentation

- [FHIRPath](/docs/core-sdk/fhirpath)
- [Serialization](/docs/core-sdk/serialization)
- [Specification](/docs/core-sdk/abstractions)
