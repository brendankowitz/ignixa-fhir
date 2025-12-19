# Feature: Caching

**Status**: Implementation Complete
**Created**: 2025-10-31

## Problem Statement

High-performance FHIR server requires multi-level caching to avoid redundant work across HTTP requests, application lifetime, and tenant isolation boundaries. Caching must support both in-memory and distributed scenarios.

## Constraints

- Must support request-scoped, application-level, tenant-scoped, and static caches
- Must be thread-safe for shared caches
- Must support multi-tenant isolation
- Must be memory-efficient with bounded growth
- Must support both in-memory and distributed (Redis) backends

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [architecture](investigations/architecture.md) | Complete | Comprehensive overview of all caching mechanisms with performance measurements |
| [abstraction-architecture](investigations/abstraction-architecture.md) | Complete | Unified caching abstraction supporting in-memory and distributed scenarios |

## Decision

See [ADR-2510: Four-Scope Caching Architecture](../../adr/adr-2510-caching-architecture.md)
