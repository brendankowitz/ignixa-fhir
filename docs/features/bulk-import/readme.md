# Feature: Bulk Import Operation

**Status**: Proposed
**Created**: 2025-10-19

## Problem Statement

FHIR servers need to support bulk data import for initial data loading and ongoing data synchronization. The FHIR Bulk Data Import specification defines an asynchronous pattern for importing large datasets in NDJSON format.

## Constraints

- Must comply with FHIR Bulk Data Import specification
- Must use DurableTask framework (consistent with $export)
- Must support initial load (with negative version IDs) and incremental sync modes
- Must provide progress tracking and resumability
- Must support multi-tenant isolation

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [import-operation](investigations/import-operation.md) | Viable | 6-phase implementation plan using DurableTask, estimated 54-70 hours |

## Decision

*No ADR yet - investigations in progress*
