# Feature: Unified Validation, Terminology & Package Management

**Status**: Proposed
**Created**: 2025-01-08

## Problem Statement

Three interconnected systems (Package Management, Validation, Terminology) have circular dependencies and shared infrastructure. They must be implemented in a coordinated manner to avoid rework and ensure proper integration.

## Constraints

- Package management loads FHIR NPM packages (IGs, profiles, terminology)
- Validation requires profile definitions from packages
- Terminology requires ValueSet/CodeSystem from packages
- Validation requires terminology for binding validation
- Must coordinate implementation phases across all three systems

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [package-validation-integration](investigations/package-validation-integration.md) | Viable | 12-16 week coordinated implementation plan addressing circular dependencies |

## Decision

*No ADR yet - investigations in progress*
