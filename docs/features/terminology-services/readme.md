# Feature: Terminology Services

**Status**: Proposed
**Created**: 2025-01-08

## Problem Statement

FHIR Terminology operations ($expand, $validate-code, $lookup, $translate, $subsumes) are essential for production servers handling coded data. Current infrastructure supports minimal terminology validation only.

## Constraints

- Must comply with FHIR R4 Terminology Service specification
- Phase 1 target: <100ms response time for ValueSets <10K codes
- Phase 2 target: <50ms with caching for large terminologies (LOINC, SNOMED CT)
- Must coordinate with validation system and package management

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [terminology-operations](investigations/terminology-operations.md) | Viable | 3-phase implementation: MVP (2-3 weeks), Production (4-6 weeks), Advanced (optional) |
| [new-design](investigations/new-design.md) | Proposed | FHIR terminology import strategy with background processing and hybrid storage |
| [sql-design](investigations/sql-design.md) | Proposed | SQL-optimized ValueSet expansion with property/ancestry indexes for LOINC/SNOMED performance |

## Decision

*No ADR yet - investigations in progress*
