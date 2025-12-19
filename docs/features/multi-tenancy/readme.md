# Feature: Multi-Tenancy

**Status**: Proposed
**Created**: 2025-10-08

## Problem Statement

FHIR servers need to support multi-tenancy with two distinct operational modes:

1. **Isolation Mode**: Multiple separate customers with isolated data stores (database per tenant, schema per tenant, or partition key isolation)
2. **Distributed Mode**: Single customer with data sharded across multiple stores for horizontal scale

The goal is to support both patterns using unified abstractions, allowing seamless switching between modes.

## Constraints

- Must use existing IFhirRepository abstraction
- Must support tenant-specific configuration
- Must provide data isolation guarantees for isolation mode
- Must support distributed query fanout for distributed mode
- Must maintain performance targets (<100ms for isolation, <1s for distributed with <10 shards)

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [partitioning-modes](investigations/partitioning-modes.md) | Viable | Multi-tenancy data partitioning with isolation and distributed modes |
| [tenant-providers](investigations/tenant-providers.md) | Viable | Tenant provider configuration and management |

## Related ADRs

- [ADR 2510: Multi-Tenancy and Data Partitioning](../../adr/adr-2510-multi-tenancy.md)

## Decision

*No ADR yet - investigations in progress*
