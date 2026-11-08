# ADR 2611: Per-Tenant Conformance State

## Status
Proposed. Supersedes in part [ADR-2512](adr-2512-event-sourced-conformance.md): the "Global scope" property of
package-controlled resources. The rest of ADR-2512 (event sourcing, lifecycle states, explicit activation) stands.

## Context
- ADR-2512 made package-controlled conformance (SearchParameter, StructureDefinition, CompartmentDefinition,
  CodeSystem, ValueSet) global: one `SourceEvents` table, read and written in tenant 1's database
  (`ConformanceServicesRegistration`), projected into one process-wide `ConformanceState`.
- Package storage is shared as well. On SQL Server, `IPackageResourceRepository` is bound to tenant 1's database
  (`DataLayerRegistration`; `dbo.PackageResource` has no tenant column), and package terminology lives in the system
  partition's database. Only the route is tenant-shaped: `/tenant/{id}/admin/packages/load` stores and activates
  for every tenant. Isolated mode promises each tenant its own store ([ADR-2510](adr-2510-multi-tenancy.md)); shared
  package storage and activation break that promise.
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
  and the staleness lease are per tenant. The barrier already lives in each tenant's database (PR #469). The lease
  does not: `ConformanceLease` is one process-local singleton, so it becomes one lease per tenant projection. Only
  a successful synchronization of that tenant's projection renews it, and a search checks the lease of the tenant
  it serves, so a current tenant cannot keep a stale tenant's searches open.
- In Isolated mode, package storage, load, activation and unload are tenant operations:
  `/tenant/{id}/admin/packages` stores and activates in tenant `{id}` alone. Base FHIR definitions stay in process,
  per FHIR version, and are not stored per tenant.
- In Isolated mode, a reindex job belongs to one tenant: its job row, lock and orchestration live in that tenant,
  and a parameter's transitions follow that tenant's job alone. `/tenant/{id}/$reindex` is authorized like any
  tenant-wide write (update on `*`, unrestricted grant); the `SystemAdmin` requirement is removed.
- Distributed mode is deferred to its own decision, which must define completion across every partition the shared
  projection serves and authorization against that shared authority. Until then, the bullets above apply to
  Isolated mode only, and Distributed mode keeps today's server-wide rules.
- Consumers (search definitions, validation, capability statement, compartments) resolve the projection for the
  request's tenant instead of a singleton. A projection loads on first use, the sync service keeps each loaded
  projection current, and an idle projection may be evicted.
- Tenant 0 stays reserved and unexposed in Isolated mode.

## Migration
These steps apply to Isolated mode. Migration into tenant 0 for Distributed mode is deferred with the rest of that
mode (see Decision).

- On upgrade, each active tenant other than 0 receives a copy of tenant 1's events that apply to it, so it starts
  with today's definitions. An activation recorded for a different FHIR version is excluded; unversioned
  activations apply to every tenant. The copy is idempotent (keyed by source event id, with a per-tenant completion
  marker) and runs before the tenant serves requests; tenant 1 keeps its events.
- Copied events keep their source event ids, and the tenant's stream continues after the highest copied id.
  Lifecycle events, transition commits and job rows refer to activations by event id, so ids are never renumbered;
  an excluded event leaves a gap, which the projection already tolerates.
- Watermarks are not reset. The tenant's conformance barrier, its consumer position and any job's
  `TargetEventId` stay valid because they are in the same id space as the copied events. A tenant whose barrier is
  ahead of the copied stream fails migration rather than lowering the barrier.
- Enablement is copied only where indexing is proven. A parameter `Enabled` today stays `Enabled` in a tenant only
  if the job that completed it listed that tenant in its `TenantIds`. That holds for every tenant active when the
  job ran (#469 targets a versioned parameter only on its version's tenants). A tenant that was inactive then, or
  added later, gets the parameter as `Pending`, as does a parameter still `Pending` or `Reindexing`. It completes
  with that tenant's next job. If the job row is gone, the parameter is `Pending`.
- An inactive tenant migrates when it is reactivated, before it serves requests, by the same rules.
- Package resources and package terminology are copied from the shared stores (tenant 1 and the system
  partition), or reloaded by package id and version, into each tenant that lacks them.

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
