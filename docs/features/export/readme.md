# Feature: Bulk Export

**Status**: Research
**Created**: 2025-10-14

## Problem Statement

FHIR Bulk Data Export enables large-scale data extraction from the server. This requires efficient streaming, background job management, and storage coordination for multi-gigabyte exports.

## Constraints

- Must support $export operation at system, patient, and group levels
- Must handle large datasets without memory pressure
- Must support async operation with status polling
- Must generate NDJSON output files
- Must support type and since filters

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [streaming-architecture](investigations/streaming-architecture.md) | Viable | Streaming export architecture for large datasets |
| [streaming-analysis](investigations/streaming-analysis.md) | Complete | Analysis of streaming patterns for export |
| [high-throughput-design](investigations/high-throughput-design.md) | Viable | High-throughput export design for petabyte-scale data |
| [channel-based-operations](investigations/channel-based-operations.md) | Viable | Channel-based async export operations |

## Decision

*No ADR yet - investigations in progress*
