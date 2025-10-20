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
│   └── IValidationSchemaResolver.cs  - Schema resolution (Phase 3)
├── Checks/
│   ├── JsonStructureCheck.cs         - Validates JSON structure
│   ├── RequiredFieldCheck.cs         - Validates required fields
│   ├── CardinalityCheck.cs           - Validates min/max cardinality
│   └── TypeCheck.cs                  - Validates FHIR data types
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

## Phase Status

- ✅ **Phase 1**: Core abstractions (COMPLETE)
- ✅ **Phase 2**: Basic checks (COMPLETE)
- 🚧 **Phase 3**: Schema building & FHIRPath invariants (PLANNED)
- 📋 **Phase 4**: Profile validation & terminology (PLANNED)

See [ADR-2527](../../docs/investigations/ADR-2527-comprehensive-validation-system.md) for details.
