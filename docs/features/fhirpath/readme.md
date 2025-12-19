# Feature: FHIRPath

FHIRPath expression evaluation engine for FHIR resource querying and data extraction.

## Status

In Progress

## Overview

This feature provides FHIRPath expression evaluation capabilities used throughout the FHIR server for search parameters, invariants, validation, and data extraction.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [Performance Optimization](investigations/performance-optimization.md) | Complete | 2025-10-16 | Performance analysis and optimization strategies for FHIRPath evaluation |
| [Gap Analysis](investigations/gap-analysis.md) | Complete | 2025-11-18 | Analysis of FHIRPath implementation gaps and missing functionality |

## Key Components

- FHIRPath expression parser
- Expression evaluator
- Function library
- Performance optimizations
- Caching strategies

## Related Features

- [Search](../search/readme.md)
- [Validation](../validation/readme.md)
- [FHIR Operations](../fhir-operations/readme.md)
