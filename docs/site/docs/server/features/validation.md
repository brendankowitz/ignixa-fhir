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

### Installed Logical Models

Installed logical models retain their defining package canonical rather than being treated as
core FHIR resources. Snapshot children and recursive `contentReference` definitions are validated
at their instance locations, including required fields inside nested `select` and `unionAll`
branches. Unresolvable content references are schema errors, not permission to skip a subtree.

The embedded SQL-on-FHIR package supports R4, R4B and R5. Its ViewDefinition snapshot inherits
required `status` from CanonicalResource; a minimal example is:

```json
{
  "resourceType": "ViewDefinition",
  "status": "active",
  "resource": "Patient",
  "select": [{
    "column": [{ "name": "id", "path": "id" }]
  }]
}
```

At Spec depth, missing `status`, `select`, or a nested column's `path` is rejected.
Full depth additionally evaluates the model's invariants, such as allowing at most one of
`forEach`, `forEachOrNull`, and `repeat` on each selection.

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

Create and update requests run validation through the registered mediator pipeline,
before resource persistence and search indexing. Validation errors return HTTP 400
with a FHIR `OperationOutcome`; a rejected update does not create a new version.

`Tenants:Configurations:<index>:ValidationDepth` defaults to `Spec`. The
`Prefer: validation=minimal|spec|full` request header overrides that tenant default.
`Minimal` is **not** a validation-off switch: it runs the schema's universal
structural checks, including required fields, primitive types, and narrative checks.
`Spec` adds schema checks and required terminology bindings. `Full` additionally
evaluates profile invariants, slicing, and advanced terminology checks. Selecting a
lower tier does not disable logical-ID validation.

Logical IDs must contain 1–64 ASCII letters, digits, hyphens, or periods. Narrative
`text.div` must be a well-formed XHTML `div` in the XHTML namespace, using FHIR's
permitted formatting subset without active content. Escaped markup and ordinary
text mentioning scripts are not executable markup. `pre` permits
`xml:space="preserve"`; that attribute is not accepted on arbitrary elements.
CSS checks distinguish actual properties/functions and URL schemes from quoted
text or passive HTTPS paths containing script-like words. Accepted narrative text
is preserved, not rewritten or sanitized. Narrative validation is not a
replacement for the [FHIR rendering security guidance](https://hl7.org/fhir/R4/security.html#narrative).

Validation depth is not a claim that every possible FHIR constraint is checked.
In particular, existing non-choice `dateTime` validation accepts loose timezone
precision at `Spec` (for example, `Period.start = "2021-10-13+02:00"`), but rejects
it at `Full`. Choice-valued dateTimes such as `Observation.effectiveDateTime` use
strict primitive syntax, including at `Spec`. The loose form is **not conformant
FHIR dateTime**; this is a compatibility policy, not a change to the FHIR grammar.
Use `Full` when strict non-choice dateTime syntax is required.

Installed FHIR packages and their StructureDefinitions supply profile rules.
The write pipeline resolves schemas and typed elements for the current request
tenant. `meta.profile` canonicals are looked up by their complete identity:
`https://example.org/StructureDefinition/patient|2.0` selects that business
version, never another profile that happens to share the final path segment.
The current package adapter requires a StructureDefinition **snapshot**; it does
not generate snapshots from differentials.

Unavailable asserted profiles (including unavailable requested versions) retain
the existing warning policy at Spec/Full: available schemas are still applied,
the unavailable profile is not treated as validated, and the warning is logged.
Missing the **base** schema is different: an admitted resource cannot be
validated at all, so the server returns HTTP 500 with a FHIR `OperationOutcome`
and does not write the resource. This represents server schema availability,
not a finding that the submitted resource is invalid.

To control which profiles validate resources:
1. Install FHIR packages using the package management endpoints
2. Configure StructureDefinitions with validation rules and invariants
3. Declare applicable installed profile canonicals in the resource's `meta.profile`

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
