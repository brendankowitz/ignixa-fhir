---
sidebar_position: 4
title: Operations
description: FHIR operations supported by Ignixa
---

# Operations

Ignixa supports standard FHIR operations for validation, bulk data, patient access, and terminology.

## Core Operations

### $validate

[FHIR Spec](https://hl7.org/fhir/resource-operation-validate.html)

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

[FHIR Spec](https://hl7.org/fhir/patient-operation-everything.html)

Retrieve all data for a patient:

```bash
GET /Patient/{id}/$everything
GET /Patient/{id}/$everything?start=2024-01-01&end=2024-12-31
GET /Patient/{id}/$everything?_type=Observation,Condition
GET /Patient/{id}/$everything?_since=2024-01-01T00:00:00Z
GET /Patient/{id}/$everything?_count=100
```

### $member-match

[Da Vinci HRex Spec](https://hl7.org/fhir/us/davinci-hrex/OperationDefinition-member-match.html)

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

| Operation | Spec | Description |
|-----------|------|-------------|
| `$export` | [Bulk Data](https://hl7.org/fhir/uv/bulkdata/export.html) | Async export to NDJSON or Parquet |
| `$import` | [Bulk Data](https://hl7.org/fhir/uv/bulkdata/import.html) | Async bulk import from NDJSON |

### $export

Start an asynchronous bulk export operation:

```bash
# System-level export (auto-detect tenant)
POST /$export

# Tenant-scoped export
POST /tenant/{tenantId}/$export

# Group-scoped export (members only)
POST /Group/{groupId}/$export

# Query parameters
POST /tenant/{tenantId}/$export?_type=Patient,Observation&_since=2024-01-01&_outputFormat=ndjson
```

Supported parameters:
- `_type` - Comma-separated list of resource types to export
- `_since` - Only include resources modified after this date
- `_typeFilter` - Advanced resource filtering
- `_outputFormat` - `ndjson` (default) or `parquet`
- `_viewDefinition` - SQL on FHIR ViewDefinition for Parquet output

Returns `202 Accepted` with `Content-Location` header pointing to the status endpoint.

### $import

Start an asynchronous bulk import operation:

```bash
# Tenant-scoped import
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
      "name": "inputSource",
      "valueUri": "https://example.org/export/patients.ndjson"
    },
    {
      "name": "mode",
      "valueCode": "IncrementalLoad"
    }
  ]
}
```

Returns `202 Accepted` with `Content-Location` header to poll job status.

See [Bulk Operations](/docs/server/features/bulk-operations) for detailed usage, parameters, and configuration.

## Experimental Operations

These operations are available when experimental features are enabled.

### $summary (IPS)

[IPS Spec](https://hl7.org/fhir/uv/ips/OperationDefinition-summary.html)

Generate an International Patient Summary:

```bash
# By patient ID (GET)
GET /Patient/{id}/$summary

# By patient ID (POST)
POST /Patient/{id}/$summary

# By patient identifier (GET)
GET /Patient/$summary?identifier=http://example.org|12345

# With specific profile
GET /Patient/{id}/$summary?profile=http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips
```

### $expand (ValueSet)

[FHIR Spec](https://hl7.org/fhir/valueset-operation-expand.html)

Expand a ValueSet to a list of codes:

```bash
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes&filter=blood
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes&count=100&offset=0
```

### $translate (ConceptMap)

[FHIR Spec](https://hl7.org/fhir/conceptmap-operation-translate.html)

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

[FHIR Spec](https://hl7.org/fhir/codesystem-operation-subsumes.html)

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

[FHIR Spec](https://hl7.org/fhir/structuremap-operation-transform.html)

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

- [`$document`](https://hl7.org/fhir/composition-operation-document.html) - Generate document from Composition
- [`$validate-code`](https://hl7.org/fhir/valueset-operation-validate-code.html) - Validate code in ValueSet
- [`$lookup`](https://hl7.org/fhir/codesystem-operation-lookup.html) - CodeSystem code lookup
- [`$snapshot`](https://hl7.org/fhir/structuredefinition-operation-snapshot.html) - Generate StructureDefinition snapshot

## Related Documentation

- [Bulk Operations](/docs/server/features/bulk-operations)
- [Validation](/docs/server/features/validation)
