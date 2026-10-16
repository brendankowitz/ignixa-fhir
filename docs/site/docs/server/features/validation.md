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
`Spec` adds schema checks and required terminology bindings, reference target types,
and every nested datatype, whether it is a plain element, a complex choice variant
(`valueQuantity`), an extension's value, or the inside of a CodeableConcept, Coding or
Reference. A Bundle or Parameters resource has each resource it carries validated too.
`Full` additionally evaluates profile invariants, slicing, extensible bindings, and
extensions against their loaded definitions, including where each may be used. Selecting
a lower tier does not disable logical-ID validation. See
[Validation depths](/docs/core-sdk/validation#validation-depths) for what each tier
checks and reports.

:::note Behaviour changes in this release
Writes stay strict at the default `Spec` depth: checks that were skipped now run, so a
write the server accepted before can be rejected. The list below is every change a
server caller can see.

**Newly rejected at `Spec` and `Full`** (HTTP 400):

- Rules that already applied to a plain element now also apply inside a complex choice
  variant (`valueCodeableConcept`, `valueQuantity`, ...) and an extension's value. For
  example, an Observation whose `valueCodeableConcept.coding.system` is a relative
  reference such as `"system1"` now fails `coding-system-absolute`, as
  `Observation.code.coding.system = "system1"` always did and as the HL7 validator
  reports it.
- The inside of a CodeableConcept, Coding or Reference: an unknown property
  (`code.foo`, `subject.actor`), a wrong JSON type (`coding.version: 5`,
  `reference: 123`), or an element repeated past its cardinality.
- A CodeableConcept with text only under a required binding (`binding-required-text-only`).
  A concept carrying only a `data-absent-reason` extension is not affected: the binding has
  no value to apply to.
- A reference to a resource type its element does not allow (`reference-target-type`),
  or one whose `type` disagrees with it (`reference-type-mismatch`). This covers relative,
  absolute and `_history` references, contained `#id`, `#` (the containing resource, or the
  resource itself at its root), conditional references (`Patient?identifier=...`, typed by
  the part before `?`), Bundle `urn:uuid:` entries, and in R4B and R5 the `reference` of a
  CodeableReference.
- A malformed extension: no `url`, more than one `value[x]`, a value breaking its datatype.
- A resource-level extension with a relative `url` (`extension-url-relative`); only a
  complex extension's sub-extensions may use one.
- A Bundle or Parameters resource whose entries break their own type or `meta.profile`:
  each `Bundle.entry.resource`, `Bundle.entry.response.outcome` and
  `Parameters.parameter.resource` (parts included) is validated, and an entry with no known
  `resourceType` is `entry-invalid-resourcetype`. The entries' references resolve against
  the Bundle.

**Newly rejected at `Full` only:**

- An extension used where its loaded definition's contexts do not allow it
  (`extension-context`), and a modifier extension under `extension` or a non-modifier
  under `modifierExtension` (`extension-modifier-mismatch`).
- The invariants of a nested Reference, CodeableConcept or Coding (for example `ref-1`,
  which now runs on every Reference; its R4, R4B and STU3 form is applied with R5's
  correction, so a Reference with only an identifier or a display passes).

**Newly accepted:**

- Below `Full`, a choice-variant `dateTime` with loose timezone precision
  (`effectiveDateTime = "2023-01-01T12:00:00"`) is accepted, as the same value on a plain
  element always was (bug #210-4; see below). It was rejected before.

**`Compatibility` depth** keeps accepting text-only required concepts, wrong reference
targets, relative `Coding.system` and relative extension urls, and does not check extension
contexts. It now validates extensions, complex choice variants, the insides of
CodeableConcept, Coding and Reference, and Bundle and Parameters entries, and accepts loose
choice dateTimes as above.

**New non-failing issues in `$validate` output** (`Information`): `reference-target-unchecked`
(a reference whose target type cannot be told, such as an unresolved `urn:uuid:` or an
identifier only), and at `Full` `extension-definition-unchecked` (an extension whose
definition is not loaded, or a sub-extension of a loaded complex extension) and
`extension-context-unchecked` (an extension whose definition allows it only in FHIRPath
contexts, which are not evaluated).

**Issue paths:** `coding-1` and `coding-system-absolute` issues are rooted at the resource
and indexed (`Observation.valueCodeableConcept.coding[0]`). Issues inside a Bundle entry
are rooted at the entry (`Bundle.entry[3].resource.status`). An issue inside a contained
resource of the container's own type is no longer rooted twice
(`Observation.contained[0].status`, not `Observation.contained[0].contained[0].status`).

**Installed packages:** snapshots projected from the core definitions name choice elements
`value[x]` and carry every target profile, so a differential-only profile constraining a
choice element now takes effect.
:::

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
In particular, `dateTime` validation accepts loose timezone precision below `Full`
(for example, `Period.start = "2021-10-13+02:00"` or
`Observation.effectiveDateTime = "2023-01-01T12:00:00"`), and rejects it at `Full`.
The rule is the same for a plain element and a choice variant. The loose form is
**not conformant R4 dateTime**; this is a compatibility policy (bug #210-4: real-world
data carries it, and the R5 regular expression admits it, although R5's prose still
requires a timezone whenever a time is present), not a change to the FHIR grammar. Every other
dateTime rule (component ranges, calendar validity) applies at every depth. Use `Full`
when strict dateTime syntax is required.

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
