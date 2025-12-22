---
sidebar_position: 1
title: Validation
description: Three-tier validation system
---

# Validation

Ignixa provides a three-tier validation system that balances performance with conformance checking.

## Validation Features

The server provides validation via the `$validate` operation endpoint. Validation checks FHIR resource structure and conformance rules.

### Validation Checks

Ignixa performs the following validation checks:

- JSON structure validity
- Required field presence
- Basic type checking
- Value domain validation
- Reference format checking
- CodeableConcept structure
- Cardinality constraints
- StructureDefinition constraints (if profiles are loaded)
- Extension validation
- Invariant (FHIRPath) evaluation

Note: Detailed configuration of validation levels is managed through installed FHIR packages and StructureDefinitions. For custom validation behavior, use invariants in StructureDefinitions.

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

### Basic Validation (Tenant-Explicit)

```bash
POST /tenant/{tenantId}/Patient/$validate
Content-Type: application/fhir+json

{
  "resourceType": "Patient",
  "name": [{ "family": "Smith" }]
}
```

Or single-tenant mode:

```bash
POST /Patient/$validate
Content-Type: application/fhir+json

{
  "resourceType": "Patient",
  "name": [{ "family": "Smith" }]
}
```

### Validate Against a Specific Profile

Use a Parameters resource with the `profile` parameter:

```bash
POST /tenant/{tenantId}/Patient/$validate
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "profile",
      "valueUri": "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"
    },
    {
      "name": "resource",
      "resource": {
        "resourceType": "Patient",
        "name": [{ "family": "Smith" }]
      }
    }
  ]
}
```

### Validation Modes

Specify validation mode (create, update, delete) in the Parameters resource:

```bash
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "mode",
      "valueCode": "create"
    },
    {
      "name": "resource",
      "resource": { ... }
    }
  ]
}
```

Supported modes:
- `create` - Validate as if creating a new resource
- `update` - Validate as if updating an existing resource
- `delete` - Validate deletion constraints

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

## Validation Configuration

The server validates resources during create and update operations. Validation behavior is controlled by installed FHIR packages and their StructureDefinitions.

To control which profiles validate resources:
1. Install FHIR packages using the package management endpoints
2. Configure StructureDefinitions with validation rules and invariants
3. Resources will automatically be validated against installed profiles

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

Terminology validation checks coded values against ValueSets defined in installed FHIR packages. The server uses the Ignixa Terminology Service to expand and validate value sets.

To enable terminology validation:
1. Install FHIR packages that contain ValueSet definitions
2. Configure StructureDefinitions with binding constraints
3. The server will validate coded elements against their bound ValueSets during validation

See [Operations](/docs/server/fhir/operations#expand-valueset) for $expand, $translate, and $subsumes operations.

## Best Practices

1. **Use `$validate` endpoint before creating resources** - Test conformance without storing invalid data
2. **Profile validation via StructureDefinitions** - Install the appropriate FHIR packages for your domain (e.g., US Core, AU Base)
3. **Custom invariants** - Add FHIRPath expressions in StructureDefinitions for business rule validation
4. **Error handling** - Check OperationOutcome severity levels to determine whether validation failures should be treated as fatal

## Related Documentation

- [ADR: Validation Architecture](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-validation-architecture.md)
- [Core SDK: Validation](/docs/core-sdk/validation)
