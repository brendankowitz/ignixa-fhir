# ADR-2512: Event-Sourced Conformance Management

**Status**: Proposed
**Date**: 2025-12-20
**Feature**: search, packages

## Context

The current SearchParameter resolution system involves 6+ classes and ~2000 lines of code traversing multiple layers: `LoadPackageHandler` → `PackageLoadedSearchParameterSyncHandler` → `FhirVersionContext` → `CompositeSearchParameterDefinitionManager` → `SearchParameterConflictResolver` → `SearchIndexReferenceDataCache`. This architecture suffers from race conditions during concurrent package loads, silent failures when sync handlers catch and log exceptions, N+1 database queries, and 5 separate cache dictionaries requiring manual invalidation. Conflict resolution depends on load order, making behavior non-deterministic across server restarts.

Additionally, we need a clear stance on which conformance resources should be package-controlled (versioned, explicit activation) versus API-controlled (CRUD, runtime lookup). Resources that affect server-wide behavior (SearchParameter, StructureDefinition, CompartmentDefinition) have high blast radius when malformed and should require explicit activation. Resources used at runtime for specific operations (ViewDefinition, Consent) can be safely managed via standard FHIR API.

## Resource Categorization

### System/Package-Controlled Resources (Event-Sourced)

These resources affect server-wide behavior and require explicit package activation. A malformed resource can break the entire server. They are:

- **Global scope** - affect all tenants and all API operations
- **Versioned** - tied to specific package versions
- **Activated explicitly** - require admin action to enable
- **Immutable once active** - changes require new package version

| Resource Type | Why Package-Controlled | Failure Impact |
|---------------|------------------------|----------------|
| **SearchParameter** | Defines indexing behavior; malformed expressions crash indexer | Server-wide search broken |
| **StructureDefinition** | Defines validation rules; malformed profiles reject valid resources | Server-wide validation broken |
| **CompartmentDefinition** | Defines compartment membership; errors break patient-level access | Compartment queries return wrong data |
| **CodeSystem** (terminology) | Defines valid codes; errors break terminology operations | `$validate-code`, `$lookup` fail |
| **ValueSet** (terminology) | Defines code sets for validation; errors cause false validation failures | Validation rejects valid resources |

### Tenant/User-Controlled Resources (Standard FHIR API)

These resources are used at runtime for specific operations. A malformed resource affects only that operation or user. They are:

- **Tenant-scoped** - isolated per tenant
- **CRUD-managed** - standard FHIR create/read/update/delete
- **Runtime lookup** - resolved when operation executes
- **Mutable** - can be updated anytime via API

| Resource Type | Why API-Controlled | Failure Impact |
|---------------|-------------------|----------------|
| **ViewDefinition** | Used by `$extract`/`$export`; errors fail one export job | One export fails |
| **Consent** | Per-patient access rules; errors affect one patient | One patient's access wrong |
| **Subscription** | Per-resource notifications; errors miss one notification | One subscription fails |
| **OperationDefinition** (custom) | Custom operation metadata; errors affect one operation | One custom op unavailable |
| **NamingSystem** | Identifier resolution; errors affect identifier lookups | One identifier type fails |
| **MessageDefinition** | Message routing; errors affect one message type | One message type fails |

### Decision Criteria

**Use package control when:**
- Resource affects server indexing, validation, or core search behavior
- Malformed resource could crash server or corrupt data
- Resource should be versioned with implementation guides
- Changes require coordinated deployment across server instances

**Use API control when:**
- Resource is used at runtime for a specific operation
- Malformed resource only affects that operation
- Resource is tenant-specific or user-specific
- Resource needs frequent updates without package releases

## Options Considered

1. **Current multi-cache architecture** - Keep existing 6-class flow with manual cache invalidation *(rejected: race conditions, silent failures, non-deterministic)*

2. **Materialized projection tables** - Add `ActiveSearchParameters` table updated on each package load *(rejected: still requires cache invalidation logic, doesn't solve multi-instance sync)*

3. **Event-sourced with in-memory projection** - Single `SourceEvents` table, replay on startup into in-memory dictionaries *(viable)*

## Decision

Adopt event-sourced conformance management as detailed in [event-sourced-conformance.md](../features/search/investigations/event-sourced-conformance.md).

A single `SourceEvents` table captures all conformance changes (package activated, search parameter activated, reindex started/completed, deactivated, etc.). On startup, servers replay all events (~2000-5000 for typical deployments) into an in-memory `ConformanceState` projection in <100ms. Package activation validates the entire configuration atomically before emitting events, detecting conflicts at activation time rather than query time. Multi-instance synchronization becomes trivial: each server tracks its last processed `EventId` and polls for new events.

SearchParameters transition through explicit lifecycle states: Pending → Reindexing → Enabled → Disabled. Only Enabled parameters are used for search; others are tracked but ignored. Base FHIR parameters skip directly to Enabled (pre-indexed at resource creation time).

This approach reuses the existing `SearchParameterResolutionOptions` for priority-based conflict resolution while eliminating the complex cache invalidation flow. Query-time resolution becomes a single O(1) dictionary lookup instead of multi-layer cache traversal.

## Consequences

- **Removes ~1,500 lines** of cache management code across 5+ classes
- **Adds ~400 lines** for event store, projection, and activation pipeline
- **Enables full audit trail** - "why is this SP active?" becomes a simple event query
- **Simplifies multi-instance deployment** - no distributed cache invalidation needed
- **Requires migration** - existing package state must be converted to events on first startup
- **Changes activation model** - packages must be explicitly activated after upload (no auto-activation on load)
