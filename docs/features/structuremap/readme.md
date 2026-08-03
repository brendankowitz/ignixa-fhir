# Feature: StructureMap Transform

FHIR StructureMap-based data transformation engine using the $transform operation.

## Status

Approved

## Overview

This feature implements the FHIR $transform operation for StructureMap-based data transformations, enabling format conversion, data mapping, and structure transformation across FHIR resources.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [Implementation Summary](investigations/implementation-summary.md) | Approved | 2025-11-29 | Summary of StructureMap transform implementation approach |
| [Operation Integration](investigations/operation-integration.md) | Approved | 2025-11-29 | Integration of $transform operation into the FHIR server |
| [Mutation Strategy](investigations/mutation-strategy.md) | Approved | 2025-11-30 | Strategy for handling in-place vs copy-on-write transformations |
| [FML Oracle Conformance Corpus](investigations/fml-oracle-conformance-corpus.md) | Approved | 2026-07-28 | Grading the FML engine against HL7 `fhir-test-cases` structure-mapping and the R4/R5→R6 cross-version maps |

## Key Components

- StructureMap engine integration
- $transform operation endpoint
- Mutation handling (in-place vs copy)
- Map compilation and caching
- Multi-tenant map storage

## Related Features

- [FHIR Operations](../fhir-operations/readme.md)
- [Validation](../validation/readme.md)
- [Package Management](../package-management/readme.md)
