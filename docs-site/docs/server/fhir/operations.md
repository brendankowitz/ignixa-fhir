---
sidebar_position: 4
title: Operations
description: FHIR operations supported by Ignixa
---

# Operations

Ignixa supports standard FHIR operations for validation, bulk data, patient access, and terminology.

## Core Operations

### $validate

Validate a resource against FHIR specifications and profiles:

```bash
# Type-level validation
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

#### Validation Modes

```bash
# Validate against a profile
POST /Patient/$validate?profile=http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient

# Validation mode (create, update, delete)
POST /Patient/$validate?mode=create
```

#### Validation Depth

Control validation depth via Prefer header:

```bash
POST /Patient/$validate
Prefer: mode=minimal   # Structure only
Prefer: mode=spec      # FHIR spec compliance (default)
Prefer: mode=full      # Full profile validation with terminology
```

### $everything

Retrieve all data for a patient:

```bash
GET /Patient/{id}/$everything
GET /Patient/{id}/$everything?start=2024-01-01&end=2024-12-31
GET /Patient/{id}/$everything?_type=Observation,Condition
GET /Patient/{id}/$everything?_since=2024-01-01T00:00:00Z
GET /Patient/{id}/$everything?_count=100
```

### $member-match

Match patients across different payer systems (HRex specification):

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
    },
    {
      "name": "CoverageToMatch",
      "resource": {
        "resourceType": "Coverage",
        "status": "active",
        "beneficiary": { "reference": "Patient/member" }
      }
    }
  ]
}
```

## Bulk Data Operations

### $export

Bulk data export following the [FHIR Bulk Data Access](https://hl7.org/fhir/uv/bulkdata/) specification:

```bash
# System-level export
POST /$export

# Tenant-level export
POST /tenant/{tenantId}/$export

# Group-level export
POST /Group/{id}/$export
```

#### Export Parameters

| Parameter | Description |
|-----------|-------------|
| `_type` | Resource types to include (comma-separated) |
| `_since` | Export resources modified since |
| `_typeFilter` | Search filters per type |
| `_outputFormat` | `application/fhir+ndjson` or `application/vnd.apache.parquet` |
| `_viewDefinition` | SQL on FHIR ViewDefinition ID (required for Parquet) |

#### Async Response

```
HTTP/1.1 202 Accepted
Content-Location: /tenant/{tenantId}/_export/{jobId}
```

Poll for completion:

```bash
GET /tenant/{tenantId}/_export/{jobId}
```

### $import

Bulk data import:

```bash
POST /tenant/{tenantId}/$import
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "inputFormat",
      "valueCode": "application/fhir+ndjson"
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

## Experimental Operations

These operations are available when experimental features are enabled.

### $summary (IPS)

Generate an International Patient Summary:

```bash
# By patient ID
GET /Patient/{id}/$summary

# By patient identifier
GET /Patient/$summary?identifier=http://example.org|12345

# With specific profile
GET /Patient/{id}/$summary?profile=http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips
```

### $expand (ValueSet)

Expand a ValueSet to a list of codes:

```bash
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes&filter=blood
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes&count=100&offset=0
```

### $translate (ConceptMap)

Translate codes between systems using ConceptMap:

```bash
POST /ConceptMap/$translate
Content-Type: application/fhir+json

{
  "code": "123",
  "system": "http://source.org",
  "url": "http://example.org/ConceptMap/my-map"
}
```

### $subsumes (CodeSystem)

Test subsumption relationship between codes:

```bash
POST /CodeSystem/$subsumes
Content-Type: application/fhir+json

{
  "codeA": "parent-code",
  "codeB": "child-code",
  "system": "http://example.org/CodeSystem/my-codes"
}
```

### $transform (StructureMap)

Transform data using a StructureMap:

```bash
# Using a stored StructureMap
POST /StructureMap/{id}/$transform
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "content",
    "resource": { ... }
  }]
}

# Using an inline StructureMap
POST /StructureMap/$transform
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "sourceMap",
      "resource": { "resourceType": "StructureMap", ... }
    },
    {
      "name": "content",
      "resource": { ... }
    }
  ]
}
```

## Not Yet Implemented

The following operations are planned but not yet available:

- `$document` - Generate document from Composition
- `$validate-code` - Validate code in ValueSet
- `$lookup` - CodeSystem code lookup
- `$snapshot` - Generate StructureDefinition snapshot

## Related Documentation

- [Bulk Operations](/docs/server/features/bulk-operations)
- [Validation](/docs/server/features/validation)
