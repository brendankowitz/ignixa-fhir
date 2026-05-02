---
sidebar_position: 1
title: Architecture Decision Records
description: Index of architectural decisions for Ignixa FHIR
---

# Architecture Decision Records

Architecture Decision Records (ADRs) document significant architectural choices made in the Ignixa FHIR project. ADRs are maintained in the repository at [`docs/adr/`](https://github.com/brendankowitz/ignixa-fhir/tree/main/docs/adr).

## Core Design Principle

All architectural decisions support the **F5 Developer Experience**: a developer can press F5 and run the solution with minimal setup.

## ADR Index

### Authorization & Security

| ADR | Title | Status |
|-----|-------|--------|
| [2501](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2501-authorization.md) | RBAC Authorization with Capability Statement Enforcement | Accepted |
| [2602](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2602-deid-library.md) | FHIR DeId Library | Accepted |

### Architecture & Design

| ADR | Title | Status |
|-----|-------|--------|
| [2509](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2509-vertical-slice-architecture.md) | Vertical Slice Architecture | Accepted |
| [2509](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2509-inmemory-search.md) | InMemory Search Architecture | Accepted |
| [2509](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2509-bundle-processing.md) | Bundle Processing with Channels | Accepted |
| [2510](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-capability-sourcenode-model.md) | CapabilityStatement Without Firely SDK | Accepted |

### Data & Storage

| ADR | Title | Status |
|-----|-------|--------|
| [2510](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-multi-tenancy.md) | Multi-Tenancy and Data Partitioning | Proposed |
| [2510](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-ndjson-storage.md) | NDJSON File-Based Storage | Accepted |
| [2510](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-caching-architecture.md) | Four-Scope Caching Architecture | Accepted |

### Operations & Features

| ADR | Title | Status |
|-----|-------|--------|
| [2510](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-background-jobs.md) | Background Jobs with DurableTask | Accepted |
| [2510](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-validation-architecture.md) | Three-Tier Validation Architecture | Accepted |
| [2510](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-conditional-operations.md) | Conditional CRUD Operations | Accepted |
| [2510](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-patch-operations.md) | FHIR Patch Operations | In Progress |
| [2512](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2512-member-match-operation.md) | $member-match Operation | Proposed |
| [2512](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2512-narrative-generator.md) | Narrative Generator Library | Accepted |
| [2512](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2512-patient-summary-generation.md) | Patient Summary Generation | Proposed |

## ADR Format

Each ADR follows this structure:

```markdown
# ADR {YYMM}: {Short Title}

## Status
Proposed | Accepted | Deprecated | Superseded

## Context
What problem are we solving?

## Decision
What did we decide?

## Consequences
**Positive:** Benefits
**Negative:** Trade-offs
```

## Creating New ADRs

1. Create file: `docs/adr/adr-{YYMM}-{short-title}.md`
2. Use the template above
3. Keep it concise (40-100 lines)
4. Focus on decision and rationale, not implementation details
