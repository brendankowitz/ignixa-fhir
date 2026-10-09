# Feature: Reindex

**Status**: Implemented (spec v5)
**Created**: 2026-10-06

## Problem Statement

When a SearchParameter is added, changed, or removed (for example by installing an IG package or by a custom
`SearchParameter`), resources that already exist keep the index rows extracted under the old definitions.
Ignixa has no way to bring those rows up to date:

- **Legacy job storage is retired.** Reindex work is tracked as `BackgroundJob` entries with
  `BackgroundJobType.Reindex` and executed by DurableTask. Schema version 6 removes the unused
  `ReindexJob` table, its five stored procedures, and its unused bulk TVP. `UpdateResourceSearchParams`
  remains the index-only writer.
- **Partially indexed parameters must not give wrong results without warning.**
  `CompositeSearchParameterDefinitionManager` now marks `Pending` and `Reindexing` as non-searchable by default
  while retaining partial-index opt-in; `Staged` is unsupported and `Disabling` is hidden until its transition
  commits. `SearchOptionsBuilderFactory` uses the tenant's searchable definitions, so a package parameter becomes
  searchable only after its reindex completes.
- **The raw data for exact scoping already exists.** Transaction ids are time-ordered, and each one is the base
  of a disjoint range of `ResourceSurrogateId`s. Visibility advances only past contiguous completed
  transactions. `SourceEvents` records the visible watermark at each append, and activation events name the
  exact parameters and resource types that changed. So the set of resources to reindex can be computed from the
  transaction cutoff alone; no per-row hash is needed. (`Resource.SearchParamHash` was never written.)
- **Indexes are extracted before the transaction id is allocated.** Instances also pick up conformance changes by
  polling. A write that lands after the cutoff can therefore still carry old definitions. Each tenant database
  needs a conformance barrier that rejects allocations from writers with stale definitions.
- **Clients and conformance tests expect the operation.** `ms-reindex.json` exercises the Microsoft FHIR Server
  `$reindex` API surface. All of its assertions are `warningOnly` today.

## Constraints

- SQL Server is the target data layer. File system and in-memory providers may rebuild their indexes another way,
  but they must not report a parameter as `Enabled` before its index is complete.
- Background work uses DurableTask orchestrations ([ADR-2510](../../adr/adr-2510-background-jobs.md)), following
  the `$export` pattern (`ExportOrchestration` with `GetExportRangesActivity` and `ExportWorkerActivity`).
- SearchParameter status is **global**: it is event-sourced in tenant 1's database
  ([ADR-2512](../../adr/adr-2512-event-sourced-conformance.md)). Resource data is stored in **one database per
  tenant**. A parameter can be `Enabled` only after every tenant database has been reindexed.
- Tenant `0` is reserved and is never exposed through `/tenant/0`.
- Do not change `MergeResources` or its TVPs. Index-only rewrites go through `UpdateResourceSearchParams`. They
  must not change `Version`, `LastUpdated`, `RawResource`, or history.
- The web farm syncs conformance state by polling (`ConformanceStateSyncService`, default 30s). Polling
  convergence is not a closed barrier: new instances, idle imports, and paused processes can still write with old
  definitions. Correctness must be enforced by the database, not by timing.
- A lagging instance's *searches* are a separate hazard that the write barrier cannot fence. The spec handles them
  with two-phase extraction changes and a fail-closed staleness lease (spec §4.4, §4.5).
- Transaction ids and visibility watermarks belong to each database. Every tenant needs its own cutoff; tenant
  1's `SourceEvents.TransactionId` cannot stand in for the others.
- Normal reads and writes must keep running at production throughput while a reindex job runs on databases with
  10^8+ resources.
- Every degraded state must be visible to operators: partial indexes, failed resources, and superseded jobs. There
  are no success-shaped fallbacks.

## Specification

[spec.md](spec.md): the requirements, API, lifecycle, job design, and test plan for Ignixa `$reindex`. The
reindex scope is an exact per-tenant transaction cutoff, fenced by a database-enforced conformance barrier. No
per-row hash is used.

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [microsoft-fhir-server-prior-art](investigations/microsoft-fhir-server-prior-art.md) | Complete | How microsoft/fhir-server implements `$reindex` (orchestrator/processing jobs, per-row hash, `UpdateResourceSearchParams`, status lifecycle, cache convergence). Lists what Ignixa adopts, adapts, and avoids, including replacing the hash with the transaction cutoff. |

## Decision

*No ADR yet. The spec is ready for review. Its open questions (spec §12) should be resolved before `/create-adr`.*
