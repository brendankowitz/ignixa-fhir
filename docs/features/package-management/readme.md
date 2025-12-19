# Feature: Package Management

**Status**: Proposed
**Created**: 2025-10-08

## Problem Statement

FHIR servers need to support loading Implementation Guides (IGs), profiles, extensions, and StructureMaps from npm registries and Simplifier.net. This capability is essential for:

- Regulatory compliance (US Core, IPS)
- Industry standards (Da Vinci, CARIN Blue Button, IHE profiles)
- Custom validation profiles
- Cross-version conversion using StructureMaps

## Constraints

- Must integrate with existing validation architecture
- Must support both build-time code generation and runtime loading
- Must support public and private package registries
- Must enable tenant-specific Implementation Guides
- Must maintain performance targets for validation

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [npm-simplifier](investigations/npm-simplifier.md) | Viable | FHIR package integration from npm and Simplifier.net registries |
| [multi-version-ig](investigations/multi-version-ig.md) | Viable | Multi-version IG loading system with header-based resolution |
| [enhancements](investigations/enhancements.md) | Viable | Package management enhancements and optimization strategies |
| [custom-structures](investigations/custom-structures.md) | Viable | Custom structures and R6 extensibility support |
| [version-conversion](investigations/version-conversion.md) | Viable | FHIR version conversion and multi-version support |
| [packaging-strategy](investigations/packaging-strategy.md) | Viable | Overall packaging strategy and approach |
| [valueset-migration](investigations/valueset-migration.md) | Viable | ValueSet provider migration plan |

## Decision

*No ADR yet - investigations in progress*
