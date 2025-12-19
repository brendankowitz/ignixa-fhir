# Feature: SQL Server Storage

**Status**: Partial Implementation
**Created**: 2025-10-28

## Problem Statement

SQL Server provides the primary production storage backend for FHIR resources. This feature area covers SQL-specific optimizations, stored procedures, connection pooling, and Entity Framework patterns.

## Constraints

- Must optimize for high-throughput CRUD operations
- Must support efficient batch operations via TVPs
- Must manage connection pooling for multi-tenant scenarios
- Must integrate with Entity Framework Core

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [provider-architecture](investigations/provider-architecture.md) | Complete | SQL provider architecture and design |
| [connection-pooling](investigations/connection-pooling.md) | Viable | Connection pooling analysis for multi-tenant scenarios |
| [merge-procedure](investigations/merge-procedure.md) | Viable | MERGE stored procedure pattern for upserts |
| [ef-core-tvp](investigations/ef-core-tvp.md) | Viable | Table-valued parameters with EF Core |
| [transaction-abstraction](investigations/transaction-abstraction.md) | Viable | Transaction table core abstraction |
| [test-failures](investigations/test-failures.md) | Active | SQL on FHIR v2 test failure analysis with 72/135 passing, boolean NULL semantics and forEach architecture issues identified |

## Decision

*No ADR yet - investigations in progress*
