# Feature: Serialization

Custom resource support and serialization model improvements.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [model-refactoring](investigations/model-refactoring.md) | Viable | 2025-01-08 | Refactoring plan for serialization models using mutable wrappers |
| [viewdefinition-support](investigations/viewdefinition-support.md) | Proposed | 2025-01-08 | ViewDefinition custom resource support via IG loading |

## Overview

This feature area addresses two major serialization concerns:

### 1. Serialization Model Refactoring

**Problem**: Current serialization models are verbose and inefficient:
- Accessing complex objects creates new wrapper objects on-the-fly
- List manipulation requires awkward workarounds
- Repetitive boilerplate code in property implementations

**Solution**: Introduce mutable list wrappers and helper methods:
- `MutableJsonList<T>` for strongly-typed list manipulation
- `MutablePrimitiveList<T>` for primitive collections
- Helper methods in `BaseJsonNode` to reduce boilerplate

### 2. ViewDefinition Custom Resource Support

**Problem**: ViewDefinition (SQL-on-FHIR v2) works for CRUD but lacks:
- Proper validation using StructureDefinitions
- Search parameter support
- CapabilityStatement advertisement

**Solution**: Implementation Guide (IG) Loading infrastructure:
- Download FHIR packages from packages.fhir.org
- Automatically register StructureDefinitions and SearchParameters
- Detect and support custom resources (ViewDefinition, etc.)
- Industry-standard approach used by major FHIR servers

**Estimated Effort**: 32-48 hours (4-6 days) over 4 phases
