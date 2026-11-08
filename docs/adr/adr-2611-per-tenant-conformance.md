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

- **Frozen source.** At cutover, before any tenant-local conformance operation, the server freezes the legacy
  stream through its high-water mark H, the package content those events reference, and the completion evidence
  below. Every tenant migrates from this frozen source: first migrations, delayed ones and retries. A tenant never
  inherits tenant 1's later private changes. The source is retained until every pre-existing tenant has migrated.
- **Copy.** Each tenant other than 0 receives the frozen events that apply to it, so it starts with today's
  definitions. An activation recorded for a different FHIR version is excluded; unversioned activations apply to
  every tenant. The copy is idempotent (keyed by source event id, with a per-tenant completion marker) and runs
  before the tenant serves requests. An active tenant migrates at upgrade; an inactive one migrates when it is
  reactivated. Tenant 1 keeps its events.
- **Event identity.** Copied events keep their source event ids. Lifecycle events, transition commits and job rows
  refer to activations by event id, so ids are never renumbered.
- **High-water mark.** Every destination reserves H. Its projection checkpoint is set to H once the copy completes,
  which acknowledges the excluded events in the gaps, and its new events are allocated above H. A consumer position,
  conformance barrier or job `TargetEventId` taken from the legacy stream is at most H, so it stays valid without
  being reset, and no new event can land below a retained position.
- **Completion evidence.** The frozen source records, for each completed reindex job still on record at cutover,
  the job id and its `TenantIds`. Later deletion of job rows does not change what a migration copies.
- **Enablement.** A parameter `Enabled` today stays `Enabled` in a tenant when the evidence shows that the job
  completing it covered that tenant. That holds for every tenant active when the job ran (#469 targets a versioned
  parameter only on its version's tenants). In every other case the parameter becomes `Pending` and completes with
  that tenant's next job:
  - a tenant inactive when the job ran, or added later;
  - a parameter still `Pending` or `Reindexing`;
  - a completing job whose row was already deleted before cutover.

  This downgrade is deliberate: an unproven `Enabled` would serve searches over unindexed data.
- **Packages.** Package resources and package terminology come from the frozen source, or are reloaded by package
  id and version, into each tenant that lacks them.

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
