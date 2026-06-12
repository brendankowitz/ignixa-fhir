---
sidebar_position: 2
title: Package Stability
---

<!-- GENERATED FILE - do not edit by hand. -->
<!-- Regenerate with: pwsh eng/Generate-StabilityMatrix.ps1 (also runs in the docs CI workflow). -->

# Package Stability

Ignixa packages are classified per [ADR 2606](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2606-nuget-experimental-versioning.md):

| Level | Version format | Meaning |
|-------|---------------|---------|
| **Stable** | `1.0.0` | Production-ready, stable API |
| **Beta** | `1.0.0-beta` | Feature-complete, API stabilizing |
| **Alpha** | `1.0.0-alpha` | Experimental, breaking changes expected |

Pre-release packages require the `--prerelease` flag (`dotnet add package <id> --prerelease`).
A package is never more stable than any package it depends on.

## Libraries

| Package | Stability | Description |
|---------|-----------|-------------|
| `Ignixa.Abstractions` | Stable | Core interfaces and abstractions for Ignixa FHIR Server |
| `Ignixa.Analyzers` | Stable | Roslyn analyzers for Ignixa to prevent common misuse patterns across the codebase |
| `Ignixa.Extensions.FirelySdk5` | Stable | Firely SDK 5.x interoperability shims for bidirectional conversion (legacy version support) |
| `Ignixa.Extensions.FirelySdk6` | Stable | Firely SDK interoperability shims for bidirectional conversion |
| `Ignixa.FhirFakes` | Stable | A comprehensive FHIR test data generation library for modeling patient populations and medical histories. |
| `Ignixa.FhirPath` | Stable | FHIRPath expression evaluation engine for FHIR resources |
| `Ignixa.PackageManagement` | Stable | NPM package management for FHIR Implementation Guides |
| `Ignixa.Search` | Stable | FHIR search parameter indexing and query infrastructure |
| `Ignixa.Serialization` | Stable | High-performance FHIR JSON serialization |
| `Ignixa.Sidecar.Contracts` | Stable | Shared gRPC contracts for Ignixa FHIR Server sidecar integration. Enables building custom sidecar services for audit, authorization (RBAC), metrics, and logging. Distributed as application-level package for internal use. |
| `Ignixa.Specification` | Stable | FHIR specification data and structure providers (R4/R4B/R5/STU3) |
| `Ignixa.SqlOnFhir` | Stable | SQL on FHIR implementation for analytics queries |
| `Ignixa.SqlOnFhir.Writers` | Stable | Writers for SQL on FHIR - Parquet and CSV output formats |
| `Ignixa.Validation` | Stable | FHIR resource validation with profile support |
| `Ignixa.DeId` | Beta (pre-release) | FHIR data de-identification library supporting R4, R4B, R5, R6, and STU3 via Ignixa SDK |
| `Ignixa.FhirMappingLanguage` | Beta (pre-release) | FHIR Mapping Language (FML) parser and execution engine |
| `Ignixa.NarrativeGenerator` | Beta (pre-release) | FHIR narrative generation using Scriban templates with FHIRPath support |
| `Ignixa.TestScript` | Beta (pre-release) | FHIR TestScript execution engine - parse and evaluate TestScript resources against any FHIR server |
| `Ignixa.TestScript.FhirFakes` | Beta (pre-release) | FhirFakes integration for TestScript fixture generation - auto-generate test data from resource type |
| `Ignixa.TestScript.XUnit` | Beta (pre-release) | xUnit integration for FHIR TestScript execution - discover and run TestScript files as xUnit theories |

## CLI Tools

| Package | Stability | Description |
|---------|-----------|-------------|
| `Ignixa.FhirFakes.Cli` | Stable | CLI tool for generating FHIR test data using the FhirFakes library. Supports single resources, scenarios, and population generation. |
| `Ignixa.SqlOnFhir.Cli` | Stable | CLI tool for SQL on FHIR ViewDefinition processing - convert FHIR resources to Parquet/CSV, preview schemas, and validate ViewDefinitions. |
| `Ignixa.Validation.Cli` | Stable | CLI tool for validating FHIR resources using the Ignixa.Validation library. Supports file input, JSON string input, and formatted console output. |
| `Ignixa.ConformanceMatrix.Cli` | Beta (pre-release) | CLI tool for running FHIR TestScript conformance suites against a server and producing a conformance matrix report. |
| `Ignixa.DeId.Cli` | Beta (pre-release) | CLI tool for de-identifying FHIR resources using configurable rules. Supports dateshift, redact, encrypt, cryptohash, substitute, perturb, and generalize operations. |

