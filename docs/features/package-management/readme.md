# Feature: Package Management

**Status**: Decided (lifecycle ADR Proposed; not implemented)
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
| [reliable-package-lifecycle](investigations/reliable-package-lifecycle.md) | Merged into proposed ADR | Evidence-backed comparison with dotnet-fhir-packages: durable completion, dependency planning, verified acquisition integration, and visible outcomes |

The earlier investigations describe historical designs, not a census of the current
implementation. In particular, transitive dependency loading and verified acquisition
now exist. The lifecycle investigation records the 2026-10-20 baseline and distinguishes
remaining gaps from capabilities already implemented.

## Decision

[ADR-2610: Reliable Package Lifecycle](adr-2610-reliable-package-lifecycle.md) proposes
evolving the existing package infrastructure with explicit planning, installation
completion, and activation boundaries. The ADR is **Proposed**, not accepted or
implemented. It addresses lifecycle reliability; the other investigations' broader
hosting, terminology, conversion, and schema-extensibility proposals remain separate.
