# Feature: SQL Data Layer Architecture

**Status**: Exploring
**Created**: 2026-07-11

## Problem Statement

The SQL Server data layer (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework`) has grown ad-hoc. FHIR search-expression-to-SQL translation is spread across a handful of large, type-switching classes (`SearchParameterQueryGenerator.cs` at 2113 lines, `SqlEntityFrameworkSearchService.cs` at 1329 lines, `CompositeSearchParameterQueryGenerator.cs` at 803 lines) with the same per-search-parameter-type knowledge (comparator handling, token system/code encoding, composite component shapes) duplicated independently on the read path (query generators) and the write path (`RowGenerators/*`). There is no single place that owns "how a FHIR search predicate becomes SQL" — Core already has a proper `Expression`/`IExpressionVisitor<TContext,TOutput>` tree (`src/Core/Ignixa.Search/Expressions/`), but the SQL backend bypasses it in favor of hand-rolled `expression switch`/`is`-chains and, in one spot, reflection.

This mirrors a problem the Microsoft FHIR Server team is solving by treating SQL generation as a compiler pipeline (semantic expression retention → logical relational plan → physical planner → typed SQL AST — see `docs/superpowers/plans/2026-07-10-fhir-search-semantic-expression-foundation.md` in the `fhir-server` repo). This feature area assesses Ignixa's current state against that model and proposes a maintainable, incremental path forward.

## Constraints

- Must not break the `IExpressionVisitor<TContext,TOutput>` / `Expression` contract in Core — it's shared across SQL, CosmosDB, and file-based search backends. Changes to the semantic shape affect all three.
- Reversible in increments (CLAUDE.md "Reversibility Check") — this is a live, tested system serving real search traffic; no big-bang rewrite.
- Must preserve the documented `PostMergeExtensionUpdater` / TVP pattern (CLAUDE.md Section 5) unless a phase explicitly revisits it with its own investigation.
- New abstractions stay in DataLayer — must not leak SQL-specific concepts into Application/Domain (CLAUDE.md layer rules).
- No performance regression — this code path runs on every search request.
- Test coverage for the two largest files (`SearchParameterQueryGenerator`, `CompositeSearchParameterQueryGenerator`) appears to be indirect (via E2E search suites) rather than unit-level — see audit investigation. Any refactor phase must close this gap before or alongside the change, not after.

## Investigations

| Investigation | Status | Summary |
|---|---|---|
| [current-state-audit](investigations/current-state-audit.md) | Complete | Concrete, file:line-cited inventory of duplication, ad-hoc dispatch, and structure loss in the current SQL search/index pipeline |
| [staged-query-compiler](investigations/staged-query-compiler.md) | In Progress | Recommended phased adoption of a compiler-shaped pipeline, adapted from the ms-fhir-server "SQL as compiler" plan to Ignixa's EF Core + shared-Expression-tree architecture |

## Related ADRs

- [ADR 2509: InMemory Search Architecture](../../adr/adr-2509-inmemory-search.md) — the sibling in-memory backend; any SQL-side semantic changes should stay compatible with it.
- No ADR yet exists for SQL Server search-generation architecture specifically. This feature area is the candidate source for one once a phase ships.

## Decision

*No ADR yet — investigations in progress.*
