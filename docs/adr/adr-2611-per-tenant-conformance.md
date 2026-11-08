# ADR 2611: Per-Tenant Conformance State

## Status
Proposed. Supersedes in part [ADR-2512](adr-2512-event-sourced-conformance.md): the "Global scope" property of
package-controlled resources. The rest of ADR-2512 (event sourcing, lifecycle states, explicit activation) stands.

## Context
- ADR-2512 made package-controlled conformance (SearchParameter, StructureDefinition, CompartmentDefinition,
  CodeSystem, ValueSet) global: one `SourceEvents` table, read and written in tenant 1's database
  (`ConformanceServicesRegistration`), projected into one process-wide `ConformanceState`.
- The package API is tenant-shaped: `/tenant/{id}/admin/packages/load` stores the package in tenant `{id}`, but
  activation changes search and validation for every tenant. Isolated mode promises each tenant its own store
  ([ADR-2510](adr-2510-multi-tenancy.md)); a shared catalog breaks that promise.
- The reindex feature ([PR #469](https://github.com/brendankowitz/ignixa-fhir/pull/469)) absorbs the mismatch:
  - a job spans every active tenant, and a parameter becomes `Enabled` only after all of them are reindexed;
  - a tenant activated while a job runs fails that job;
  - definitions carry a FHIR version so other versions' tenants are unaffected, and one `(resourceType, code)`
    claimed under two versions is rejected with `SP_FHIR_VERSION_CONFLICT`;
  - because a job or package load changes every tenant, `$reindex` and package load/unload require `SystemAdmin`
    while more than one tenant is active.
- Tenant 1 doubles as the server's conformance authority, coupling every tenant to one customer's database.

## Options
1. **Keep global** - rejected: contradicts Isolated mode, keeps every cost above, and tenant administrators cannot
   manage their own implementation guides.
2. **Shared catalog, per-tenant status** - rejected: tracking status per (parameter, tenant) fixes enablement, but
   one tenant's package still changes the others' definitions, and the version conflict remains.
3. **Per-tenant store and projection, located by tenant mode** - chosen.

## Decision
- The conformance store follows `TenantMode`:

  | Mode | `SourceEvents` and package resources | Projection |
  |---|---|---|
  | Isolated | each tenant's own database or store | one `ConformanceState` per tenant |
  | Distributed (future) | the system partition (tenant 0) | one, shared by the partitions it serves |

- A tenant's event stream holds only its own activations. Event ids, the activation lock, the conformance barrier
  and the staleness lease are per tenant; the barrier and lease already live in each tenant's database (PR #469).
- Package load, activation and unload are tenant operations: `/tenant/{id}/admin/packages` affects tenant `{id}`
  alone. Base FHIR definitions stay in process, per FHIR version, and are not stored per tenant.
- A reindex job belongs to one tenant: its job row, lock and orchestration live in that tenant, and a parameter's
  transitions follow that tenant's job alone. `/tenant/{id}/$reindex` is authorized like any tenant-wide write
  (update on `*`, unrestricted grant); the `SystemAdmin` requirement is removed.
- Consumers (search definitions, validation, capability statement, compartments) resolve the projection for the
  request's tenant instead of a singleton. A projection loads on first use, the sync service keeps each loaded
  projection current, and an idle projection may be evicted.
- Tenant 0 stays reserved and unexposed in Isolated mode.

## Migration
- On upgrade, each active tenant other than 0 receives a copy of tenant 1's events, so it starts with today's
  definitions. The copy is idempotent (keyed by source event id, with a per-tenant completion marker) and runs
  before the tenant serves requests; tenant 1 keeps its events.
- Parameters already `Enabled` stay `Enabled`: the global jobs that enabled them covered every tenant. A parameter
  still `Pending` or `Reindexing` becomes `Pending` in each tenant and completes with that tenant's next job.
- Package resources and package terminology are copied, or reloaded by package id and version, into each tenant
  that lacks them.

## Consequences

**Positive:**
- Isolated mode is isolated for conformance too; tenant administrators manage their own guides and reindexing.
- Reindex simplifies to one tenant per job. Cross-tenant planning, the "tenant added after the job started"
  failure, per-version targeting and `SP_FHIR_VERSION_CONFLICT` (a tenant has one FHIR version) are deleted, as is
  the `SystemAdmin` requirement on `$reindex` and package load/unload.
- No customer's database is the server's conformance authority.

**Negative:**
- Memory and startup cost grow with the number of tenants; lazy loading and eviction bound them, and both must be
  measured.
- The same guide in N tenants means N activations and N reindexes; definitions are not shared between tenants.
- The migration touches every tenant; a defect duplicates or loses activations everywhere, so it must be idempotent
  and tested against multi-tenant upgrades.
- Every conformance consumer moves from a singleton to tenant resolution: a broad, mostly mechanical change.
- In Distributed mode, activation again spans partitions; that is acceptable because the partitions serve one
  customer.
