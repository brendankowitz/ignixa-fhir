# ADR 2510: Three-Tier Validation Architecture

## Status
Accepted

## Context
FHIR validation must balance correctness, performance, and flexibility. Different use cases have different requirements:
- CREATE/UPDATE API calls need fast validation (<25ms)
- $validate operation can be slower but comprehensive
- Bulk import needs minimal validation for throughput

Firely SDK uses a compiled schema architecture with composable assertions. HAPI uses XML-based Schematron rules. We need an approach that fits Ignixa's JsonNode-based resource model.

## Decision
Implement a **three-tier validation pipeline** based on Firely's compiled schema pattern:

```
┌────────────────┐   ┌──────────────────┐   ┌─────────────────┐
│  Tier 1: FAST  │ → │   Tier 2: SPEC   │ → │ Tier 3: PROFILE │
│    <25ms       │   │     <200ms       │   │    <1000ms      │
└────────────────┘   └──────────────────┘   └─────────────────┘
│ JSON structure │   │ Type checking    │   │ Custom profiles │
│ Required fields│   │ Cardinality      │   │ Slicing         │
│ Basic syntax   │   │ FHIRPath (ele-1) │   │ Terminology*    │
```

**Use Case Mapping:**
- CREATE/UPDATE: Tier 1 + Tier 2 (synchronous, blocking)
- $validate: All tiers (async, returns OperationOutcome)
- Bulk Import: Tier 1 only (fast lane, log errors)

**Core Components:**
- `IValidationSchemaResolver` - Cached schema compilation
- `ValidationSchema` - Immutable schema tree with `IAssertion[]`
- `FastValidator` - Composable validators (Cardinality, Type, FHIRPath, etc.)
- `ValidationBehavior` - Medino pipeline integration

## Consequences

**Positive:**
- Clear performance guarantees per tier
- Composable validators enable easy extension
- Cached schemas avoid repeated compilation
- Async terminology integration doesn't block API calls

**Negative:**
- Three tiers add complexity vs single validator
- Schema compilation requires memory (mitigated by caching)
- Terminology validation requires external service integration

## References
- Investigation: `docs/features/validation/investigations/validation-architecture.md`
