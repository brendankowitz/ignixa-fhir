# Feature: Reindex

**Status**: Exploring
**Created**: 2026-10-06

## Problem Statement

When a SearchParameter is added, changed, or removed (for example by installing an IG package or by a custom
`SearchParameter`), resources that already exist keep the index rows extracted under the old definitions.
Ignixa has no way to bring those rows up to date:

- **There is no reindex runner.** `ReindexJob.sql`, its five stored procedures, `UpdateResourceSearchParams.sql`,
  the `Resource.SearchParamHash` column, the compiler's hash-mismatch filter
  (`MatchPageEmitter.EmitSearchParameterHashClause`), and the `SearchParameterReindex{Started,Completed,Failed}`
  events all exist, but no C# code uses them. `EternalOrchestrationStarter.cs:55` contains a commented-out
  `ReindexOrchestration`.
- **Partially indexed parameters give wrong results without warning.** `ConformanceState` sets package parameters
  to `Pending`, but `CompositeSearchParameterDefinitionManager` admits `Enabled` *and* `Pending`. Also, the
  "searchable" resolver in `SearchServicesRegistration.cs:211` returns the full manager, so
  `SearchableSearchParameterDefinitionManager` is never used. A search on a parameter that was never reindexed
  misses older resources. With `:not` or `:missing`, it also returns resources that should not match.
- **The hash cannot detect stale rows.** `ResourceRowGenerator.cs:119` never writes `SearchParamHash`
  (`TODO Phase 2`). `CompositeSearchParameterDefinitionManager.GetSearchParameterHashForResourceType` delegates
  to the base manager, so package parameters never change the hash.
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
- The web farm syncs conformance state by polling (`ConformanceStateSyncService`, default 30s). Range planning
  cannot start until no instance can still write resources with an outdated hash.
- Normal reads and writes must keep running at production throughput while a reindex job runs on databases with
  10^8+ resources.
- Every degraded state must be visible to operators: partial indexes, failed resources, and superseded jobs. There
  are no success-shaped fallbacks.

## Specification

[spec.md](spec.md): the requirements, API, lifecycle, job design, and test plan for Ignixa `$reindex`.

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [microsoft-fhir-server-prior-art](investigations/microsoft-fhir-server-prior-art.md) | Complete | How microsoft/fhir-server implements `$reindex` (orchestrator/processing jobs, per-row hash, `UpdateResourceSearchParams`, status lifecycle, cache convergence). Lists what Ignixa adopts, adapts, and avoids. |

## Decision

*No ADR yet. The spec is ready for review. Its open questions (spec §12) should be resolved before `/create-adr`.*
