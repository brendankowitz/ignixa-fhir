# Feature: Conditional Operations

**Status**: Complete
**Created**: 2025-10-19

## Problem Statement

FHIR conditional operations allow clients to create, update, patch, and delete resources based on search criteria rather than explicit IDs. This is critical for idempotent integrations and data synchronization workflows.

## Constraints

- Must comply with FHIR R4 conditional interaction specification
- Must support all conditional verbs: create (If-None-Exist), read (If-None-Match), update, patch, delete
- Must work within transaction bundles
- Must support multi-tenant routing

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [conditional-crud](investigations/conditional-crud.md) | Merged | All 6 phases implemented: conditional create, update, delete, patch, read, and bundle integration |

## Related ADRs

- [ADR-2510: FHIR Patch Operations](../../adr/adr-2510-patch-operations.md)
- [ADR-2510: Conditional CRUD Operations](../../adr/adr-2510-conditional-operations.md)

## Decision

All conditional operations implemented with optimistic concurrency control and verbose OperationOutcomes. See [conditional-crud](investigations/conditional-crud.md).
