# Feature: Comprehensive Validation System

**Status**: Core Complete
**Created**: 2025-10-20

## Problem Statement

FHIR validation is critical for production servers but must balance correctness, performance, and flexibility. Different use cases (API create, $validate operation, bulk import) require different validation depths and performance targets.

## Constraints

- Structural validation must complete in <25ms for API operations
- Full profile validation can take up to 1000ms for $validate
- Must support tiered validation (Tier 1: structural, Tier 2: profile, Tier 3: terminology)
- Must integrate with terminology services for binding validation
- Architecture inspired by Firely's compiled schema pattern

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [validation-architecture](investigations/validation-architecture.md) | Merged | Core system complete: abstractions, basic validators, schema building, FHIRPath invariants, cardinality, choice types, API integration |
| [two-tier-architecture](investigations/two-tier-architecture.md) | Viable | Two-tier validation: Fast structural (10-50ms) and full profile validation (500ms-5s) with opt-in complexity |
| [codegen-requirements](investigations/codegen-requirements.md) | Complete | Analysis of additional codegen needed for Phase 4-6: invariants, extensions, bindings, ValueSets |
| [parity-analysis](investigations/parity-analysis.md) | Complete | Feature parity achieved between legacy FastPathValidator and new composable FastValidator |
| [reference-implementations](investigations/reference-implementations.md) | Complete | Architectural patterns extracted from Firely and HAPI validators |
| [architecture-overview](investigations/architecture-overview.md) | Complete | FastPathValidator architecture diagram and request flow |
| [integration-summary](investigations/integration-summary.md) | Complete | FastPathValidator integration in Application layer (CreateOrUpdateResourceHandler) |
| [depth-refactor](investigations/depth-refactor.md) | In Progress | Consolidate ValidationTier/Mode into single ValidationDepth enum |
| [hapi-message-format](investigations/hapi-message-format.md) | Complete | HAPI FHIR OperationOutcome structure for ecosystem compatibility |

## Decision

Firely-inspired validation architecture with compiled schemas and composable assertions. Core phases 1-6 complete, terminology integration pending.

See [ADR-2510: Three-Tier Validation Architecture](../../adr/adr-2510-validation-architecture.md)
