---
sidebar_position: 1
title: Validation
description: Three-tier validation system
---

# Validation

Ignixa provides a three-tier validation system that balances performance with conformance checking.

## Validation Levels

### Fast Validation

Structural validation only - fastest option:

- JSON structure validity
- Required field presence
- Basic type checking
- No external lookups

```json
{
  "Validation": {
    "Level": "Fast"
  }
}
```

### Specification Validation

FHIR specification compliance:

- All Fast checks, plus:
- Value domain validation
- Reference format checking
- CodeableConcept structure
- Cardinality constraints

```json
{
  "Validation": {
    "Level": "Spec"
  }
}
```

### Profile Validation

Full profile-based validation:

- All Spec checks, plus:
- StructureDefinition constraints
- Extension validation
- Terminology binding validation
- Invariant (FHIRPath) evaluation

```json
{
  "Validation": {
    "Level": "Profile",
    "EnableProfileValidation": true
  }
}
```

## Validation Flow

```
Resource Input
     │
     ▼
┌─────────────┐
│    Fast     │ Structure, required fields
└──────┬──────┘
       │
       ▼
┌─────────────┐
│    Spec     │ FHIR specification rules
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Profile   │ Custom profiles, invariants
└──────┬──────┘
       │
       ▼
OperationOutcome
```

## Using $validate

Validate resources without storing:

```bash
POST /Patient/$validate
Content-Type: application/fhir+json

{
  "resourceType": "Patient",
  "name": [{ "family": "Smith" }]
}
```

### Validate Against Profile

```bash
POST /Patient/$validate?profile=http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient
```

### Validation Modes

```bash
# Validate for create
POST /Patient/$validate?mode=create

# Validate for update
POST /Patient/$validate?mode=update
```

## OperationOutcome

Validation results are returned as OperationOutcome:

```json
{
  "resourceType": "OperationOutcome",
  "issue": [
    {
      "severity": "error",
      "code": "required",
      "diagnostics": "Patient.name: minimum required = 1, but only found 0",
      "location": ["Patient.name"]
    },
    {
      "severity": "warning",
      "code": "business-rule",
      "diagnostics": "Patient.gender: value is missing",
      "location": ["Patient.gender"]
    }
  ]
}
```

### Severity Levels

| Severity | Description | Result |
|----------|-------------|--------|
| `fatal` | Processing cannot continue | Rejected |
| `error` | Violates FHIR rules | Rejected |
| `warning` | Doesn't conform to best practice | Accepted |
| `information` | Informational message | Accepted |

## Validation on Create/Update

Configure automatic validation:

```json
{
  "Validation": {
    "ValidateOnCreate": true,
    "ValidateOnUpdate": true,
    "RejectOnError": true,
    "RejectOnWarning": false
  }
}
```

## Custom Validation Rules

Add custom validation via invariants in StructureDefinitions:

```json
{
  "resourceType": "StructureDefinition",
  "constraint": [{
    "key": "us-core-8",
    "severity": "error",
    "human": "Patient.name.family or Patient.name.given SHALL be present",
    "expression": "family.exists() or given.exists()"
  }]
}
```

## Terminology Validation

Validate coded values against ValueSets:

```json
{
  "Validation": {
    "ValidateTerminology": true,
    "TerminologyServer": "https://tx.fhir.org/r4"
  }
}
```

## Usage Guidelines

| Level | Use Case |
|-------|----------|
| Fast | High-throughput ingestion, bulk import |
| Spec | Standard API operations |
| Profile | Compliance testing, IG validation |

For high-volume ingestion, consider:

1. Use Fast validation on ingest
2. Batch validate asynchronously
3. Profile validate on read

## Related Documentation

- [ADR: Validation Architecture](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-validation-architecture.md)
- [Core SDK: Validation](/docs/core-sdk/validation)
