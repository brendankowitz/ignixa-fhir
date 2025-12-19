# ADR 2510: Four-Scope Caching Architecture

## Status
Accepted

## Context
FHIR server operations have repeated computations that benefit from caching:
- FHIRPath expression compilation
- CapabilityStatement generation
- Schema lookups
- Search parameter definitions

Caching must balance performance gains against memory usage and cache invalidation complexity.

## Decision
Implement **four cache scopes** with different lifetimes:

| Scope | Lifetime | Thread-Safe | Purpose |
|-------|----------|-------------|---------|
| **Request** | Single HTTP request | No | Avoid redundant work within request |
| **Application** | App lifetime | Yes (ConcurrentDictionary) | Hot-path performance |
| **Tenant** | Tenant registration | Yes (per-tenant isolation) | Tenant-specific config |
| **Static** | Process lifetime | Yes (read-only after init) | Immutable metadata |

**Design Principles:**
- **Lazy initialization**: Populate caches on first use, not at startup
- **Thread-safety**: All shared caches use `ConcurrentDictionary` or immutable collections
- **Memory-bounded**: Application-level caches have implicit bounds (limited expression variety)
- **No TTL/expiration**: Caches live for their scope lifetime (stateless server, restart to clear)

**Key Cache Locations:**
- `ResourceJsonNode` - Request-scoped derived view caching (Meta, SourceNode, TypedElement)
- `FhirPathCompiler` - Application-scoped compiled expression delegates
- `CapabilityStatementBuilder` - Application-scoped generated statements
- `SearchParameterDefinitionManager` - Static parameter definitions

## Consequences

**Positive:**
- Clear ownership and invalidation semantics per scope
- No complex TTL logic or background expiration
- Thread-safe by design for shared caches
- Lazy loading avoids startup cost

**Negative:**
- Restart required to clear application/static caches
- Request-scoped caches add per-request memory overhead (~200 bytes)
- No distributed cache support (single-instance only)

## References
- Investigation: `docs/features/caching/investigations/architecture.md`
- Investigation: `docs/features/caching/investigations/abstraction-architecture.md`
