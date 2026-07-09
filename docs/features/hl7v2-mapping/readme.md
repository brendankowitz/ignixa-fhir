# Feature: HL7v2 Mapping

**Status**: Exploring
**Created**: 2026-07-09

## Problem Statement

Ignixa needs a credible path for converting between HL7 v2 messages and FHIR resources without hard-coding every mapping in C#. The key question is whether FHIR Mapping Language can carry the transformation rules for both directions: HL7 v2 to FHIR and FHIR to HL7 v2.

## Constraints

- HL7 v2 ER7 messages are delimiter-based text, while FHIR Mapping Language operates over named child trees.
- Inbound and outbound conversion must be represented as separate maps because StructureMap transformations are unidirectional.
- Terminology translation, such as PID-8 administrative sex to FHIR `Patient.gender`, should use ConceptMap-compatible mappings where possible.
- The approach should preserve Ignixa's existing layer rules and keep format parsing/serialization outside the FML engine.
- NHapi should be treated as the preferred ER7 parser/encoder candidate, but hidden behind an Ignixa-owned logical-tree adapter.
- Example tests should be executable and should not imply production HL7 v2 parsing support before an adapter exists.

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [fhir-mapping-language](investigations/fhir-mapping-language.md) | Viable | Use FHIR Mapping Language over typed HL7v2 logical models, with ER7 parsing/serialization handled by an adapter outside the map engine |
| [nhapi-logical-model-adapter](investigations/nhapi-logical-model-adapter.md) | Viable | Use NHapi for HL7 v2 parse/encode and project NHapi messages through an Ignixa logical-tree adapter for FML |

## Decision

*No ADR yet - investigations in progress*
