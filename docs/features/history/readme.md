# Feature: History Operations

**Status**: Research
**Created**: 2025-10-14

## Problem Statement

FHIR History operations provide access to previous versions of resources. This requires efficient storage of historical versions and optimized retrieval for potentially large result sets.

## Constraints

- Must support resource, type, and system-level history
- Must support _since, _at, _count parameters
- Must handle large history sets via streaming
- Must integrate with versioning storage patterns

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [streaming-migration](investigations/streaming-migration.md) | Viable | Streaming history response generation |

## Decision

*No ADR yet - investigations in progress*
