---
sidebar_position: 1
title: Architecture Decision Records
description: Index of architectural decisions for Ignixa FHIR
---

# Architecture Decision Records

This section contains Architecture Decision Records (ADRs) documenting key architectural choices made in the Ignixa FHIR project.

## What is an ADR?

An Architecture Decision Record captures a significant architectural decision along with its context and consequences. ADRs help us:

- **Remember** why decisions were made
- **Communicate** decisions to team members
- **Evaluate** decisions in hindsight
- **Guide** future decisions

## Core Design Principle: F5 Developer Experience

All architectural decisions support the principle that **a developer can press F5 and run the solution with minimal setup**. This means:

- No complex infrastructure requirements
- Self-contained dependencies
- Clear configuration with sensible defaults
- Optional production dependencies (SQL Server, etc.) are additive

## ADR Index

### Authorization & Security

| ADR | Title | Status |
|-----|-------|--------|
| [2501](./adr-2501-authorization.md) | RBAC Authorization with Capability Statement Enforcement | Accepted |

### Architecture & Design

| ADR | Title | Status |
|-----|-------|--------|
| [2509](./adr-2509-vertical-slice-architecture.md) | Vertical Slice Architecture | Accepted |
| [2509](./adr-2509-inmemory-search.md) | In-Memory Search Index | Accepted |
| [2509](./adr-2509-bundle-processing.md) | Bundle Processing | Accepted |

### Data & Storage

| ADR | Title | Status |
|-----|-------|--------|
| [2510](./adr-2510-multi-tenancy.md) | Multi-Tenancy and Data Partitioning | Proposed |

### Operations & Features

| ADR | Title | Status |
|-----|-------|--------|
| [2510](./adr-2510-background-jobs.md) | Background Jobs with DurableTask | Accepted |
| [2510](./adr-2510-validation-architecture.md) | Three-Tier Validation Architecture | Accepted |

:::note
Additional ADRs are available in the [docs/adr](https://github.com/brendankowitz/ignixa-fhir/tree/main/docs/adr) folder of the repository.
:::

## ADR Format

Each ADR follows this structure:

```markdown
# ADR {YYMM}: {Short Title}

## Status
Proposed | Accepted | Deprecated | Superseded

## Context
What problem are we solving? Why is this decision needed?

## Decision
What did we decide? Key choices and architecture diagrams.

## Consequences

**Positive:**
- Benefits of the decision

**Negative:**
- Trade-offs and drawbacks
```

## Creating New ADRs

1. Create file: `docs/adr/adr-{YYMM}-{short-title}.md`
2. Use the template above
3. Keep it concise (40-100 lines)
4. Focus on decision and rationale, not implementation details
5. Link to relevant source code or investigations

## References

- [Documenting Architecture Decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
- [Microsoft FHIR Server ADRs](https://github.com/microsoft/fhir-server/tree/main/docs/arch)
