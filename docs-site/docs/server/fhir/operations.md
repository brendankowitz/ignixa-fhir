---
sidebar_position: 4
title: Operations
description: FHIR operations supported by Ignixa
---

# Operations

Ignixa supports standard FHIR operations and custom operations for healthcare workflows.

## Validation

### $validate

Validate a resource against FHIR specifications and profiles:

```bash
POST /Patient/$validate
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "resource",
    "resource": {
      "resourceType": "Patient",
      "name": [{ "family": "Smith" }]
    }
  }]
}
```

Response:

```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "information",
    "code": "informational",
    "diagnostics": "Validation successful"
  }]
}
```

#### Validation Modes

```bash
# Validate against a profile
POST /Patient/$validate?profile=http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient

# Validation mode
POST /Patient/$validate?mode=create
POST /Patient/$validate?mode=update
POST /Patient/$validate?mode=delete
```

## Bulk Data Operations

### $export

Bulk data export following the [FHIR Bulk Data Access](https://hl7.org/fhir/uv/bulkdata/) specification:

```bash
# System-level export
GET /$export

# Patient-level export
GET /Patient/$export

# Group-level export
GET /Group/{id}/$export
```

#### Export Parameters

| Parameter | Description |
|-----------|-------------|
| `_outputFormat` | ndjson, parquet |
| `_since` | Export resources modified since |
| `_type` | Resource types to include |
| `_typeFilter` | Search filters per type |

#### Async Response

```
HTTP/1.1 202 Accepted
Content-Location: /$export-poll-status?_jobId=abc123
```

Poll for completion:

```bash
GET /$export-poll-status?_jobId=abc123
```

### $import

Bulk data import:

```bash
POST /$import
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "inputFormat",
      "valueCode": "application/fhir+ndjson"
    },
    {
      "name": "inputSource",
      "valueUri": "https://storage.example.org/import/"
    },
    {
      "name": "input",
      "part": [
        { "name": "type", "valueCode": "Patient" },
        { "name": "url", "valueUri": "https://storage.example.org/import/Patient.ndjson" }
      ]
    }
  ]
}
```

## Patient Operations

### $member-match

Match patients across different sources:

```bash
POST /Patient/$member-match
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "MemberPatient",
      "resource": {
        "resourceType": "Patient",
        "identifier": [{ "system": "http://example.org", "value": "12345" }],
        "name": [{ "family": "Smith", "given": ["John"] }]
      }
    }
  ]
}
```

### $everything

Retrieve all data for a patient:

```bash
GET /Patient/{id}/$everything
GET /Patient/{id}/$everything?start=2024-01-01&end=2024-12-31
```

## Document Operations

### $document

Generate a document from a Composition:

```bash
GET /Composition/{id}/$document
GET /Composition/{id}/$document?persist=true
```

## Terminology Operations

### $expand (ValueSet)

Expand a ValueSet:

```bash
GET /ValueSet/{id}/$expand
POST /ValueSet/$expand

{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "url", "valueUri": "http://hl7.org/fhir/ValueSet/observation-codes" },
    { "name": "filter", "valueString": "blood" }
  ]
}
```

### $validate-code (ValueSet)

Check if a code is in a ValueSet:

```bash
GET /ValueSet/{id}/$validate-code?code=29463-7&system=http://loinc.org
```

### $lookup (CodeSystem)

Get details about a code:

```bash
GET /CodeSystem/$lookup?system=http://loinc.org&code=29463-7
```

### $translate (ConceptMap)

Translate codes between systems:

```bash
GET /ConceptMap/$translate?url=http://example.org/map&code=123&system=http://source.org
```

## Structure Operations

### $snapshot (StructureDefinition)

Generate a snapshot from a differential:

```bash
POST /StructureDefinition/$snapshot

{
  "resourceType": "StructureDefinition",
  "differential": { ... }
}
```

### $transform (StructureMap)

Transform data using a StructureMap:

```bash
POST /StructureMap/{id}/$transform
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "source",
    "resource": { ... }
  }]
}
```

## Custom Operations

Ignixa supports custom operations via OperationDefinition:

```json
{
  "resourceType": "OperationDefinition",
  "name": "myOperation",
  "status": "active",
  "kind": "operation",
  "code": "my-operation",
  "resource": ["Patient"],
  "system": false,
  "type": true,
  "instance": true,
  "parameter": [...]
}
```

## Related Documentation

- [Bulk Operations](/docs/server/features/bulk-operations)
- [Validation](/docs/server/features/validation)
