# Feature: TestScript Execution Engine

**Status**: Proposed
**Created**: 2026-05-21

## Overview

A pure C# / Ignixa-native TestScript execution engine to parse and execute FHIR TestScript resources. TestScript is the FHIR standard for defining automated tests of FHIR servers, but no mature C#/.NET execution engine currently exists.

## Goals

- Execute FHIR TestScript resources against any FHIR server (HTTP or in-process)
- Follow the established Parser/Expression/Evaluator visitor pattern
- First-class integration with Ignixa.FhirFakes for fixture generation
- Produce FHIR TestReport resources and xUnit test results
- Support all FHIR versions (R4, R4B, R5)

## Constraints

- Must follow Ignixa layer architecture (Core library, no API/Application layer dependencies)
- Must use immutable execution context (consistent with FhirPath evaluator pattern)
- Must support both remote HTTP and in-process execution modes via `IFhirClient` abstraction
- One type per file, modern C# patterns

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [execution-engine](investigations/execution-engine.md) | Viable | Architecture, ecosystem analysis, implementation plan |
