# Feature: Search Capabilities

**Status**: Partial Implementation
**Created**: 2025-10-22

## Problem Statement

FHIR search is a complex domain requiring support for compartment searches, wildcard patterns, chained parameters, and composite search parameters. This feature area covers search-related enhancements beyond basic resource search.

## Constraints

- Must comply with FHIR R4 search specification (Section 3.1.0.9.1)
- Performance must scale with resource count
- Must support multi-tenant routing

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [compartment-wildcard-search](investigations/compartment-wildcard-search.md) | Merged | Implements `GET /Patient/123/*` to search all resource types in a compartment |
| [query-parsing](investigations/query-parsing.md) | Complete | FHIR search query parsing into expression trees, simplifies legacy SearchOptionsFactory |
| [codegen-parameters](investigations/codegen-parameters.md) | Viable | Code-generated search parameter definitions for 70% faster cold start (50-200ms → <5ms) |
| [index-serialization](investigations/index-serialization.md) | Complete | Compact JSON serialization reduces search index storage by 25-35% |
| [custom-parameters](investigations/custom-parameters.md) | Viable | Lifecycle management for FHIR SearchParameters from base spec, IGs, and custom definitions |
| [parallel-indexing](investigations/parallel-indexing.md) | Viable | Parallel search indexing feasibility (2-3x speedup, LOW PRIORITY - DB I/O is bottleneck) |
| [sql-on-fhir](investigations/sql-on-fhir.md) | Viable | SQL on FHIR v2 implementation analysis using ViewDefinitions for analytics queries |
| [chaining-refactor](investigations/chaining-refactor.md) | In Progress | Refactoring ChainingSearchTests to use ScenarioBuilder pattern |
| [composite-provider](investigations/composite-provider.md) | Viable | Search parameter composite provider pattern for IG-defined parameters |
| [custom-parameter-architecture-comparison](investigations/custom-parameter-architecture-comparison.md) | In Progress | Comparison of 3 architectural approaches for custom search parameter indexing (Microsoft/HAPI/LinuxForHealth analysis) |
| [event-sourced-conformance](investigations/event-sourced-conformance.md) | Viable | Event-sourced architecture for package/conformance management - replaces 6 classes with 3, eliminates caches, atomic activation |
| [includes-operation](investigations/includes-operation.md) | **Implemented** | `$includes` operation for paginated include/revinclude results with `_includesCount` parameter |
| [not-referenced-search](investigations/not-referenced-search.md) | **Implemented** | `_not-referenced` parameter to find orphaned resources not referenced by others (SQL only) |

## Related ADRs

- [ADR 2509: InMemory Search Architecture](../../adr/adr-2509-inmemory-search.md)
- [ADR 2512: Event-Sourced Conformance Management](../../adr/adr-2512-event-sourced-conformance.md)

## Decision

Compartment wildcard search implemented with multi-resource-type expansion. See [compartment-wildcard-search](investigations/compartment-wildcard-search.md).
