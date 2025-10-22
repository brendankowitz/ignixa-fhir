# Ignixa.Validation

FHIR validation system with three-tier architecture.

## Architecture

**Three-Tier Validation Pipeline** (ADR-2527):

| Tier | Target | Checks | Use Case |
|------|--------|--------|----------|
| **Fast** | <25ms | JSON structure, required fields | CREATE/UPDATE (blocking) |
| **Spec** | <200ms | + Cardinality, types, FHIRPath invariants | CREATE/UPDATE (blocking) |
| **Profile** | <1000ms | + Custom profiles, slicing, terminology | $validate (async) |

## Quick Start

### Tier 1 (Fast) Validation

```csharp
using Ignixa.Validation;
using Ignixa.SourceNodeSerialization.SourceNodes;

var json = JsonNode.Parse("{\"resourceType\":\"Patient\"}");
var validator = new FastValidator();
var result = validator.Validate(json);

if (!result.IsValid)
{
    var operationOutcome = result.ToOperationOutcome();
    // Return operationOutcome to client
}
```

### Custom Validation Checks

```csharp
using Ignixa.Validation.Checks;

var sourceNode = JsonNodeSourceNode.Create(json);
var checks = new List<IValidationCheck>
{
    new RequiredFieldCheck("id", isRequired: true),
    new CardinalityCheck("name", min: 1, max: null) // 1..*
};

var result = validator.Validate(sourceNode, checks);
```

## Project Structure

```
Ignixa.Validation/
├── Abstractions/
│   ├── IValidationCheck.cs           - Base interface for all checks
│   ├── IValidationSchemaResolver.cs  - Schema resolution interface
│   └── ValidationSchema.cs           - Schema container with checks
├── Checks/
│   ├── JsonStructureCheck.cs         - Validates JSON structure
│   ├── RequiredFieldCheck.cs         - Validates required fields
│   ├── CardinalityCheck.cs           - Validates min/max cardinality
│   ├── TypeCheck.cs                  - Validates FHIR data types
│   ├── ReferenceFormatCheck.cs       - Validates Reference elements
│   ├── CodingStructureCheck.cs       - Validates Coding/CodeableConcept
│   ├── FhirPathInvariantCheck.cs     - Validates FHIRPath constraints (ele-1, etc.)
│   ├── ChoiceElementCheck.cs         - Validates value[x] choice types
│   └── ExtensionStructureCheck.cs    - Validates extension structure
├── Schema/
│   ├── StructureDefinitionSchemaBuilder.cs      - Builds schemas from metadata
│   ├── StructureDefinitionSchemaResolver.cs     - Resolves by canonical URL
│   └── CachedValidationSchemaResolver.cs        - Caching decorator
├── FastValidator.cs                  - Tier 1 validator service
├── ValidationResult.cs               - Result model with ToOperationOutcome()
├── ValidationIssue.cs                - Issue model (HAPI-compatible)
├── ValidationState.cs                - Immutable state threading
└── ValidationSettings.cs             - Three-tier configuration
```

## Key Design Decisions

1. **ISourceNode over JsonNode**: Uses FHIR-aware navigation (choice types, shadow properties)
2. **HAPI Compatibility**: OperationOutcome structure matches HAPI FHIR patterns
3. **No SDK Dependencies**: Uses only Ignixa models (OperationOutcomeJsonNode)
4. **Immutable State**: ValidationState uses record pattern for thread-safety
5. **Composable Checks**: IValidationCheck interface enables pluggable validators

## Available Validators

### Tier 1 (Fast) - Universal Checks

| Check | Purpose | Error Code |
|-------|---------|------------|
| **JsonStructureCheck** | Validates JSON is well-formed object | `structure-invalid` |
| **IdFormatCheck** | Validates id format (64 char max, a-zA-Z0-9.-) | `id-format-invalid` |
| **NarrativeCheck** | Validates Narrative.status and Narrative.div | `narrative-invalid` |

### Tier 2 (Spec) - Schema-Driven Checks

| Check | Purpose | Error Code | Extracted From |
|-------|---------|------------|----------------|
| **CardinalityCheck** | Validates min/max element count (includes required fields) | `cardinality-violation` | `Min`, `Max` |
| **TypeCheck** | Validates primitive types (string, integer, etc.) | `type-mismatch` | `DefaultTypeName` |
| **ReferenceFormatCheck** | Validates Reference format (relative/literal) | `reference-format-invalid` | Type == "Reference" |
| **CodingStructureCheck** | Validates Coding/CodeableConcept structure | `coding-structure-invalid` | Type == "Coding\|CodeableConcept" |
| **FhirPathInvariantCheck** | Validates FHIRPath constraints (ele-1, dom-1, etc.) | Constraint key (e.g., `ele-1`) | `Constraints[]` |
| **ChoiceElementCheck** | Validates choice type elements (value[x] one variant only) | `choice-multiple`, `choice-invalid-type` | `IsChoiceElement` |
| **ExtensionStructureCheck** | Validates extension structure (url + value/nested) | `ext-url-required`, `ext-content-required`, `ext-both-value-and-nested` | Type == "Extension" |

### Schema-Driven Validation Example

```csharp
// Build schema from StructureDefinition
var provider = new MemoryStructureDefinitionSummaryProvider();
var builder = new StructureDefinitionSchemaBuilder();
var schema = builder.BuildSchema(provider.Provide("Patient"), provider);

// Validate using schema
var sourceNode = JsonNodeSourceNode.Create(patientJson);
var result = schema.Validate(sourceNode, settings, state);
```

## Phase Status

- ✅ **Phase 1**: Core abstractions (COMPLETED Oct 20, 2025)
- ✅ **Phase 2**: Basic checks (COMPLETED Oct 20, 2025)
- ✅ **Phase 3**: Schema building (COMPLETED Oct 20, 2025)
- ✅ **Phase 4 Week 1**: FHIRPath invariants (COMPLETED Oct 20, 2025)
- ✅ **Phase 4 Week 2**: Cardinality & choice types (COMPLETED Oct 20, 2025)
- 📋 **Phase 5-6**: Terminology, slicing, integration (PLANNED)

See [ADR-2527](../../docs/investigations/ADR-2527-comprehensive-validation-system.md) for details.
