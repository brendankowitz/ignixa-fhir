# Feature: Conformance Matrix

Cross-FHIR-server conformance test suite modelled after [fhir262](https://github.com/healthsamurai/fhir262). Runs a shared TestScript suite against multiple FHIR server implementations and publishes a comparison matrix.

## Investigations

| Investigation | Status | Date | Description |
|---------------|--------|------|-------------|
| [fhir262-reimagined](investigations/fhir262-reimagined.md) | In Progress | 2026-06-08 | Design for reimagining fhir262 using our TestScript engine + FhirFaker |

## Goal

Produce a static site (GitHub Pages) with a pass/fail matrix like https://healthsamurai.github.io/fhir262/ — rows are test cases, columns are FHIR server implementations (Aidbox, HAPI, Medplum, ignixa-fhir, etc.).

## See Also

- [E2E Testing Feature](../e2e-testing/readme.md) — internal E2E test coverage for ignixa-fhir
- [FhirFaker Feature](../fhir-faker/readme.md) — synthetic FHIR data generation
