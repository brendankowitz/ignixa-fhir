# Investigation: HAPI FHIR Validation Message Format

**Feature**: validation
**Status**: Complete
**Created**: 2025-10-20
**Original ADR**: N/A

---

## Executive Summary

HAPI FHIR uses a **structured OperationOutcome** format with:
- **Constraint keys** as primary identifiers (e.g., "bdl-7", "ele-1", "ext-1")
- **Coded details** using `http://hl7.org/fhir/tools/CodeSystem/tx-issue-type` for terminology issues
- **Location + Expression** for precise error positioning
- **Human-readable diagnostics** with consistent patterns

**Key Finding**: Ignixa must align with HAPI's OperationOutcome structure to ensure ecosystem compatibility.

---

## OperationOutcome Structure

### Example 1: Terminology Validation Failure

```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "code-invalid",
    "details": {
      "coding": [{
        "system": "http://hl7.org/fhir/tools/CodeSystem/tx-issue-type",
        "code": "not-in-vs"
      }],
      "text": "The provided code 'http://code.system/url#CODE' was not found in the value set 'http://value.set/url|1.0.0'"
    },
    "location": ["code"],
    "expression": ["code"]
  }]
}
```

**Key Elements**:
- `severity`: "error" | "warning" | "information" | "fatal"
- `code`: FHIR IssueType code (e.g., "code-invalid", "invariant", "structure")
- `details.coding`: Machine-readable issue type from `tx-issue-type` CodeSystem
- `details.text`: Human-readable message with specifics
- `location`: XPath-style path (legacy, for XML)
- `expression`: FHIRPath expression (modern, preferred)

### Example 2: Invariant Constraint Failure

From Schematron rules (bundle.sch, patient.sch, etc.):

```xml
<sch:assert test="not(f:total) or (f:type/@value = 'searchset') or (f:type/@value = 'history')">
  bdl-1: total only when a search or history
</sch:assert>
```

**Resulting OperationOutcome**:
```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "invariant",
    "details": {
      "text": "bdl-1"
    },
    "diagnostics": "bdl-1: total only when a search or history",
    "location": ["Bundle.total"],
    "expression": ["Bundle.total"]
  }]
}
```

**Pattern**: `{constraint-key}: {human description}`

---

## Error Code Conventions

### 1. Constraint Keys (Primary Identifiers)

HAPI uses constraint keys from FHIR StructureDefinitions:

| Prefix | Scope | Examples | Description |
|--------|-------|----------|-------------|
| `bdl-*` | Bundle | bdl-1, bdl-7, bdl-11 | Bundle-specific invariants |
| `ele-*` | Element | ele-1 | Element base constraints |
| `ext-*` | Extension | ext-1 | Extension constraints |
| `ref-*` | Reference | ref-1 | Reference resolution constraints |
| `dom-*` | DomainResource | dom-6 | DomainResource constraints |

**Example Constraint Keys**:
- **ext-1**: "Must have either extensions or value[x], not both"
- **bdl-7**: "FullUrl must be unique in a bundle, or else entries with the same fullUrl must have different meta.versionId (except in history bundles)"
- **ele-1**: "All FHIR elements must have a @value or children"

### 2. Terminology Issue Codes

From `http://hl7.org/fhir/tools/CodeSystem/tx-issue-type`:

| Code | Description | Severity |
|------|-------------|----------|
| `not-in-vs` | Code not found in ValueSet | Error |
| `invalid-code` | Unknown code in CodeSystem | Error |
| `code-invalid` | General code validation failure | Error |
| `vs-invalid` | ValueSet itself is invalid | Error |

### 3. FHIR IssueType Codes

Standard FHIR `OperationOutcome.issue.code` values:

| Code | Usage | Example |
|------|-------|---------|
| `invariant` | Constraint/invariant violation | bdl-1, ele-1 failures |
| `structure` | Structural validation failure | Missing required element |
| `code-invalid` | Terminology validation failure | Invalid code in ValueSet |
| `value` | Value validation failure | Invalid data type value |
| `required` | Required element missing | Cardinality min=1 violation |

---

## Message Format Patterns

### Pattern 1: Invariant Failures

**Format**: `{constraint-key}: {human description}`

**Examples**:
```
"bdl-1: total only when a search or history"
"bdl-7: FullUrl must be unique in a bundle..."
"ext-1: Must have either extensions or value[x], not both"
"ele-1: All FHIR elements must have a @value or children"
```

### Pattern 2: Terminology Failures

**Format**: `The provided code '{system}#{code}' was not found in the value set '{valueSet|version}'`

**Examples**:
```
"The provided code 'http://code.system/url#CODE' was not found in the value set 'http://value.set/url|1.0.0'"
"Unknown code 'CODE' in the CodeSystem 'http://code.system/url' version '1.0.0'"
```

### Pattern 3: Cardinality Violations

**Format**: `{Element path} must have at least {min} occurrence(s)` or `{Element path} must have at most {max} occurrence(s)`

**Examples**:
```
"Patient.name must have at least 1 occurrence(s)"
"Bundle.entry must have at most 1 occurrence(s)"
```

### Pattern 4: Type Mismatches

**Format**: `Expected type '{expected}' but found '{actual}'`

**Examples**:
```
"Expected type 'string' but found 'number'"
"Expected type 'CodeableConcept' but found 'Coding'"
```

---

## Severity Mapping

HAPI uses FHIR's severity levels:

| Severity | Usage | Blocks Processing |
|----------|-------|-------------------|
| **fatal** | Critical failures (parsing errors, invalid JSON) | Yes |
| **error** | Validation failures (invariants, cardinality) | Yes |
| **warning** | Best practices, extensible bindings | No |
| **information** | Informational messages, hints | No |

### Severity Decision Rules

1. **fatal**: Resource cannot be parsed or is fundamentally broken
2. **error**: Violates FHIR spec constraints (SHALL requirements)
3. **warning**: Violates best practices (SHOULD requirements) or extensible bindings
4. **information**: Hints, suggestions, additional context

**Example**:
- Missing required element (`Patient.name` with `min=1`): **error**
- Extensible binding violation: **warning**
- Best practice suggestion (e.g., prefer SNOMED over local codes): **information**

---

## Location vs Expression

HAPI provides both for backward compatibility:

### Location (Legacy, XPath-style)

```json
"location": ["Bundle.entry[2].resource"]
```

**Format**: Element path with array indices (XPath-like)

### Expression (Modern, FHIRPath)

```json
"expression": ["Bundle.entry[2].resource"]
```

**Format**: FHIRPath expression (preferred for FHIR R4+)

**Recommendation**: Ignixa should populate **both** for compatibility, but prefer **expression** for primary logic.

---

## Integration Recommendations for Ignixa

### 1. Enhance ValidationIssue Model

**Current**:
```csharp
public sealed record ValidationIssue(IssueSeverity severity, string path, string message);
```

**Recommended**:
```csharp
public sealed record ValidationIssue
{
    public IssueSeverity Severity { get; init; }
    public string Code { get; init; }  // NEW: Constraint key or issue code
    public string Message { get; init; }
    public string Location { get; init; }  // FHIRPath expression
    public CodeableConcept? Details { get; init; }  // NEW: Coded details (optional)
}
```

### 2. Add ToOperationOutcome() Method

```csharp
public OperationOutcome ToOperationOutcome()
{
    var outcome = new OperationOutcome();

    foreach (var issue in Issues)
    {
        outcome.Issue.Add(new OperationOutcome.IssueComponent
        {
            Severity = MapSeverity(issue.Severity),
            Code = DetermineIssueType(issue.Code),  // Map to FHIR IssueType
            Details = issue.Details ?? new CodeableConcept { Text = issue.Code },
            Diagnostics = issue.Message,
            Expression = new[] { issue.Location },
            Location = new[] { issue.Location }  // Same for compatibility
        });
    }

    return outcome;
}
```

### 3. Constraint Key Formatting

**Pattern**: Always format invariant failures as `{key}: {description}`

```csharp
public static ValidationIssue InvariantFailure(string key, string description, string location)
{
    return new ValidationIssue
    {
        Severity = IssueSeverity.Error,
        Code = key,
        Message = $"{key}: {description}",
        Location = location
    };
}
```

### 4. Terminology Issue Coding

For terminology failures, include coded details:

```csharp
public static ValidationIssue TerminologyFailure(string code, string system, string valueSet, string location)
{
    return new ValidationIssue
    {
        Severity = IssueSeverity.Error,
        Code = "code-invalid",
        Message = $"The provided code '{system}#{code}' was not found in the value set '{valueSet}'",
        Location = location,
        Details = new CodeableConcept
        {
            Coding = new[]
            {
                new Coding
                {
                    System = "http://hl7.org/fhir/tools/CodeSystem/tx-issue-type",
                    Code = "not-in-vs"
                }
            }
        }
    };
}
```

---

## Constraint Keys Reference

### Common Base Constraints

| Key | Description | Applies To |
|-----|-------------|------------|
| **ele-1** | All FHIR elements must have a @value or children | All elements |
| **ext-1** | Must have either extensions or value[x], not both | Extension |
| **ref-1** | SHALL have a contained resource if a local reference is provided | Reference |

### Bundle Constraints

| Key | Description |
|-----|-------------|
| **bdl-1** | total only when a search or history |
| **bdl-2** | entry.search only when a search |
| **bdl-3** | entry.request mandatory for batch/transaction/history, otherwise prohibited |
| **bdl-4** | entry.response mandatory for batch-response/transaction-response/history, otherwise prohibited |
| **bdl-5** | must be a resource unless there's a request or response |
| **bdl-7** | FullUrl must be unique in a bundle, or else entries with the same fullUrl must have different meta.versionId (except in history bundles) |
| **bdl-8** | fullUrl cannot be a version specific reference |
| **bdl-9** | A document must have an identifier with a system and a value |
| **bdl-10** | A document must have a date |
| **bdl-11** | A document must have a Composition as the first resource |
| **bdl-12** | A message must have a MessageHeader as the first resource |

### Domain Resource Constraints

| Key | Description |
|-----|-------------|
| **dom-6** | A resource should have narrative for robust management |

---

## Comparison with Firely Validator

| Aspect | HAPI | Firely | Recommendation |
|--------|------|--------|----------------|
| **Constraint Keys** | Primary identifier in `details.text` | Used in `IssueAssertion` | **Follow HAPI** (widely adopted) |
| **Message Format** | `{key}: {description}` | Similar pattern | **Align with HAPI** |
| **Coded Details** | Uses `tx-issue-type` CodeSystem | Less emphasis on coding | **Use HAPI's approach** for terminology |
| **Location Format** | Both `location` (XPath) and `expression` (FHIRPath) | FHIRPath only | **Provide both** for compatibility |
| **Severity Levels** | 4 levels (fatal/error/warning/information) | Same | **Align** |

---

## Implementation Checklist

Phase 1 (Core Abstractions):
- ✅ Research complete (this document)
- ⬜ Add `Code` property to `ValidationIssue`
- ⬜ Add `Details` property (CodeableConcept) to `ValidationIssue`
- ⬜ Implement `ToOperationOutcome()` method on `ValidationResult`
- ⬜ Create helper methods for common issue patterns (InvariantFailure, TerminologyFailure, etc.)

Phase 2+ (Validators):
- ⬜ FhirPathInvariantAssertion uses `{key}: {description}` format
- ⬜ BindingAssertion uses terminology issue coding
- ⬜ CardinalityAssertion uses cardinality message pattern
- ⬜ Test against HAPI canonical resources to verify alignment

---

## References

1. **HAPI OperationOutcome Examples**: `ThirdParty/Validation/hapi-fhir-validation/src/test/resources/terminology/`
2. **HAPI Validator Implementation**: `ThirdParty/Validation/hapi-fhir-validation/src/main/java/org/hl7/fhir/common/hapi/validation/validator/`
3. **Schematron Constraint Definitions**: `ThirdParty/Validation/hapi-fhir-validation-resources-r4/src/main/resources/org/hl7/fhir/r4/model/schema/*.sch`
4. **FHIR OperationOutcome Specification**: https://www.hl7.org/fhir/operationoutcome.html
5. **tx-issue-type CodeSystem**: http://hl7.org/fhir/tools/CodeSystem/tx-issue-type

---

**End of Analysis**
