# Feature: SQL Data Layer Architecture

**Status**: Phase 0-1 Complete (PR #328), Phase 2-3 Recommended (not yet scoped)
**Created**: 2026-07-11
**Updated**: 2026-07-11 (PR review pass)

## Problem Statement

The SQL Server data layer (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework`) had grown ad-hoc. FHIR search-expression-to-SQL translation was spread across a handful of large, type-switching classes (`SearchParameterQueryGenerator.cs` at 2113 lines — 1902 after Phase 0's dedup, `SqlEntityFrameworkSearchService.cs` at 1329 lines, `CompositeSearchParameterQueryGenerator.cs` at 803 lines) with the same per-search-parameter-type knowledge (comparator handling, token system/code encoding, composite component shapes) duplicated independently on the read path (query generators) and the write path (`RowGenerators/*`). There was no single place that owned "how a FHIR search predicate becomes SQL" — Core already had a proper `Expression`/`IExpressionVisitor<TContext,TOutput>` tree (`src/Core/Ignixa.Search/Expressions/`), but the SQL backend bypassed it in favor of hand-rolled `expression switch`/`is`-chains and, in one spot, reflection. **Phase 1 (PR #328) fixed the visitor-bypass part of this** — `SearchExpressionQueryBuilder` now implements `IExpressionVisitor` directly.

This mirrors a problem the Microsoft FHIR Server team is solving by treating SQL generation as a compiler pipeline (semantic expression retention → logical relational plan → physical planner → typed SQL AST — see `docs/superpowers/plans/2026-07-10-fhir-search-semantic-expression-foundation.md` in the `fhir-server` repo). This feature area assesses Ignixa's current state against that model and proposes a maintainable, incremental path forward.

## Constraints

- Must not break the `IExpressionVisitor<TContext,TOutput>` / `Expression` contract in Core — it's shared across SQL, CosmosDB, and file-based search backends. Changes to the semantic shape affect all three.
- Reversible in increments (CLAUDE.md "Reversibility Check") — this is a live, tested system serving real search traffic; no big-bang rewrite.
- Must preserve the documented `PostMergeExtensionUpdater` / TVP pattern (CLAUDE.md Section 5) unless a phase explicitly revisits it with its own investigation.
- New abstractions stay in DataLayer — must not leak SQL-specific concepts into Application/Domain (CLAUDE.md layer rules).
- No performance regression — this code path runs on every search request.
- ~~Test coverage for the two largest files (`SearchParameterQueryGenerator`, `CompositeSearchParameterQueryGenerator`) appears to be indirect (via E2E search suites) rather than unit-level.~~ **Closed by Phase 0** (PR #328): `SearchParameterQueryGeneratorResourceLevelTests.cs` and `CompositeSearchParameterQueryGeneratorTests.cs` now target these files directly. Any future refactor phase should extend that coverage alongside the change, not defer it.

## Investigations

| Investigation | Status | Summary |
|---|---|---|
| [current-state-audit](investigations/current-state-audit.md) | Complete | Concrete, file:line-cited inventory of duplication, ad-hoc dispatch, and structure loss in the current SQL search/index pipeline |
| [staged-query-compiler](investigations/staged-query-compiler.md) | Phase 0-1 Complete, Phase 2-3 Recommended | Phased adoption of a compiler-shaped pipeline, adapted from the ms-fhir-server "SQL as compiler" plan to Ignixa's EF Core + shared-Expression-tree architecture. Phase 0 (test baseline + mechanical dedup) and Phase 1 (visitor adoption) shipped in PR #328; Phase 2 (composite semantic leaf) and Phase 3 (data-driven catalog) not yet scoped — see `docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md`'s Post-Plan section for prerequisites and findings carried forward |

## Related ADRs

- [ADR 2509: InMemory Search Architecture](../../adr/adr-2509-inmemory-search.md) — the sibling in-memory backend; any SQL-side semantic changes should stay compatible with it.
- No ADR yet exists for SQL Server search-generation architecture specifically. This feature area is the candidate source for one once a phase ships.

## Decision

*No ADR yet — investigations in progress.*
